package com.responsly.unity;

import com.responsly.sdk.*;
import java.lang.reflect.*;
import java.util.*;

/**
 * Runs the Unity bridge on the JVM: the bridge classes come from Plugins/Android/responsly-sdk.aar
 * (the ones that ship), the events are the SDK's real event classes, and android.* is replaced by
 * the stand-ins next to this file. Checks the JSON every message carries to C#, the display-guard
 * round trip and the parsing of arguments. Run by scripts/verify-package.sh.
 */
public class BridgeTest {
    static final List<String> messages = new ArrayList<>();
    static int failures;

    static void check(String name, Object actual, Object expected) {
        boolean ok = String.valueOf(actual).equals(String.valueOf(expected));
        if (!ok) failures++;
        System.out.println((ok ? "ok   " : "FAIL ") + name + (ok ? "" : "\n   actual:   " + actual + "\n   expected: " + expected));
    }

    public static void main(String[] args) throws Exception {
        // --- JSON writer
        check("json string", ResponslyUnityJson.write("a/b \"q\" \\ \n\t\u0001 zażółć 😀"), "\"a/b \\\"q\\\" \\\\ \\n\\t\\u0001 zażółć 😀\"");
        check("json long", ResponslyUnityJson.write(9L), "9");
        check("json whole double", ResponslyUnityJson.write(9.0), "9");
        check("json double", ResponslyUnityJson.write(9.5), "9.5");
        check("json small double", ResponslyUnityJson.write(0.00001), "1.0E-5");
        check("json nan", ResponslyUnityJson.write(Double.NaN), "null");
        check("json bool", ResponslyUnityJson.write(Boolean.TRUE), "true");
        check("json null", ResponslyUnityJson.write(null), "null");
        Map<String, Object> nested = new LinkedHashMap<>();
        nested.put("a", Arrays.asList(1L, "x", null, true));
        nested.put("b", null);
        check("json nested", ResponslyUnityJson.write(nested), "{\"a\":[1,\"x\",null,true],\"b\":null}");

        // --- events through the real listener
        Field callbackField = ResponslyUnityBridge.class.getDeclaredField("callback");
        callbackField.setAccessible(true);
        callbackField.set(null, (ResponslyUnityCallback) (type, json) -> {
            if (type == null || json == null) throw new IllegalStateException("null argument");
            messages.add(type + " " + json);
        });
        Field listenerField = ResponslyUnityBridge.class.getDeclaredField("LISTENER");
        listenerField.setAccessible(true);
        ResponslyListener listener = (ResponslyListener) listenerField.get(null);

        listener.onSurveyShown(new SurveyShownEvent(new Survey("AbCd1234", "NPS \"after\" purchase"), new SurveyTrigger("manual", null)));
        check("survey_shown manual", last(), "survey_shown {\"survey\":{\"id\":\"AbCd1234\",\"name\":\"NPS \\\"after\\\" purchase\"},\"trigger\":{\"type\":\"manual\"}}");
        listener.onSurveyShown(new SurveyShownEvent(new Survey("AbCd1234", "NPS"), new SurveyTrigger("event", "purchase_completed")));
        check("survey_shown event", last(), "survey_shown {\"survey\":{\"id\":\"AbCd1234\",\"name\":\"NPS\"},\"trigger\":{\"type\":\"event\",\"name\":\"purchase_completed\"}}");
        listener.onSurveyNotShown(new SurveyNotShownEvent("AbCd1234", NotShownReason.FREQUENCY_LIMIT));
        check("survey_not_shown", last(), "survey_not_shown {\"survey_id\":\"AbCd1234\",\"reason\":\"frequency_limit\"}");
        listener.onQuestionAnswered(new QuestionAnsweredEvent("AbCd1234", "q1", "nps", "How likely?", 9L));
        check("question_answered number", last(), "question_answered {\"survey_id\":\"AbCd1234\",\"question_id\":\"q1\",\"type\":\"nps\",\"title\":\"How likely?\",\"value_json\":\"9\"}");
        listener.onQuestionAnswered(new QuestionAnsweredEvent("AbCd1234", "q2", "text", "Why?", "http://a/b \"x\""));
        check("question_answered string", last(), "question_answered {\"survey_id\":\"AbCd1234\",\"question_id\":\"q2\",\"type\":\"text\",\"title\":\"Why?\",\"value_json\":\"\\\"http://a/b \\\\\\\"x\\\\\\\"\\\"\"}");
        listener.onQuestionAnswered(new QuestionAnsweredEvent("AbCd1234", "q3", "text", "Skipped", null));
        check("question_answered null", last(), "question_answered {\"survey_id\":\"AbCd1234\",\"question_id\":\"q3\",\"type\":\"text\",\"title\":\"Skipped\",\"value_json\":\"null\"}");
        Map<String, Object> matrix = new LinkedHashMap<>();
        matrix.put("row", Arrays.asList("a", "b"));
        listener.onSurveyCompleted(new SurveyCompletedEvent("AbCd1234", "resp_1", Arrays.asList(
                new Answer("q1", "nps", "How likely?", 10L),
                new Answer("q2", "multiple_choice", "Pick", Arrays.asList("x", "y")),
                new Answer("q3", "matrix", "Grid", matrix),
                new Answer("q4", "yes_no", "OK?", true),
                new Answer("q5", "rating", "Stars", 4.5))));
        check("survey_completed", last(), "survey_completed {\"survey_id\":\"AbCd1234\",\"response_id\":\"resp_1\",\"answers\":["
                + "{\"question_id\":\"q1\",\"type\":\"nps\",\"title\":\"How likely?\",\"value_json\":\"10\"},"
                + "{\"question_id\":\"q2\",\"type\":\"multiple_choice\",\"title\":\"Pick\",\"value_json\":\"[\\\"x\\\",\\\"y\\\"]\"},"
                + "{\"question_id\":\"q3\",\"type\":\"matrix\",\"title\":\"Grid\",\"value_json\":\"{\\\"row\\\":[\\\"a\\\",\\\"b\\\"]}\"},"
                + "{\"question_id\":\"q4\",\"type\":\"yes_no\",\"title\":\"OK?\",\"value_json\":\"true\"},"
                + "{\"question_id\":\"q5\",\"type\":\"rating\",\"title\":\"Stars\",\"value_json\":\"4.5\"}]}");
        listener.onSurveyDismissed(new SurveyDismissedEvent("AbCd1234"));
        check("survey_dismissed", last(), "survey_dismissed {\"survey_id\":\"AbCd1234\"}");
        listener.onSurveyError(new SurveyErrorEvent("AbCd1234", "submit_failed", "Network"));
        check("survey_error", last(), "survey_error {\"survey_id\":\"AbCd1234\",\"code\":\"submit_failed\",\"message\":\"Network\"}");

        // --- display guard round trip
        Field guardField = ResponslyUnityBridge.class.getDeclaredField("GUARD");
        guardField.setAccessible(true);
        DisplayGuard guard = (DisplayGuard) guardField.get(null);
        final List<String> decisions = new ArrayList<>();
        Decision decision = new Decision() {
            public void allow() { decisions.add("allow"); }
            public void veto() { decisions.add("veto"); }
        };
        // The fake Handler runs the "forget" runnable at once, so resolve from inside the callback.
        callbackField.set(null, (ResponslyUnityCallback) (type, json) -> {
            messages.add(type + " " + json);
            if (type.equals("display_guard")) ResponslyUnityBridge.resolveDisplayGuard(1, false);
        });
        guard.shouldShow(new Survey("AbCd1234", "NPS"), decision);
        check("display_guard message", last(), "display_guard {\"request_id\":1,\"survey\":{\"id\":\"AbCd1234\",\"name\":\"NPS\"}}");
        check("display_guard veto", decisions.toString(), "[veto]");
        ResponslyUnityBridge.resolveDisplayGuard(1, true);
        ResponslyUnityBridge.resolveDisplayGuard(77, true);
        check("display_guard answered once", decisions.toString(), "[veto]");
        callbackField.set(null, (ResponslyUnityCallback) (type, json) -> {
            messages.add(type + " " + json);
            if (type.equals("display_guard")) ResponslyUnityBridge.resolveDisplayGuard(2, true);
        });
        guard.shouldShow(new Survey("x", "y"), decision);
        check("display_guard allow", decisions.toString(), "[veto, allow]");
        callbackField.set(null, null);
        guard.shouldShow(new Survey("x", "y"), decision);
        check("display_guard without callback allows", decisions.toString(), "[veto, allow, allow]");

        // --- argument parsing helpers
        Method values = ResponslyUnityBridge.class.getDeclaredMethod("values", String.class);
        values.setAccessible(true);
        Object traits = values.invoke(null, "{\"plan\":\"pro\",\"level\":12,\"ratio\":0.5,\"beta\":true,\"gone\":null,\"when\":\"2026-11-10T12:00:00Z\"}");
        // org.json on the JVM keeps neither the key order nor Android's number classes, so the
        // keys are sorted and a fraction is only checked to be a Number.
        check("values", describe((Map<?, ?>) traits), "beta=true:Boolean, gone=null, level=12:Integer, plan=pro:String, ratio=0.5:Number, when=2026-11-10T12:00:00Z:String");
        check("values empty", values.invoke(null, "").toString(), "{}");
        check("values malformed", values.invoke(null, "{oops").toString(), "{}");
        Method logLevel = ResponslyUnityBridge.class.getDeclaredMethod("logLevel", String.class);
        logLevel.setAccessible(true);
        check("logLevel", logLevel.invoke(null, "none") + " " + logLevel.invoke(null, "error") + " " + logLevel.invoke(null, "info") + " " + logLevel.invoke(null, "debug") + " " + logLevel.invoke(null, (Object) null), "NONE ERROR INFO DEBUG ERROR");

        System.out.println(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        if (failures != 0) System.exit(1);
    }

    static String last() { return messages.get(messages.size() - 1); }

    static String describe(Map<?, ?> map) {
        StringBuilder out = new StringBuilder();
        for (Map.Entry<?, ?> e : new TreeMap<Object, Object>(map).entrySet()) {
            if (out.length() > 0) out.append(", ");
            out.append(e.getKey()).append('=').append(e.getValue());
            Object value = e.getValue();
            if (value instanceof Integer || value instanceof String || value instanceof Boolean) out.append(':').append(value.getClass().getSimpleName());
            else if (value instanceof Number) out.append(":Number");
        }
        return out.toString();
    }
}
