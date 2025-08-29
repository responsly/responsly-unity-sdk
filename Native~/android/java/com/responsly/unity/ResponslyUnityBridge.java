package com.responsly.unity;

import android.content.Context;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;

import com.responsly.sdk.Decision;
import com.responsly.sdk.DisplayGuard;
import com.responsly.sdk.LogLevel;
import com.responsly.sdk.PresentationStyle;
import com.responsly.sdk.QuestionAnsweredEvent;
import com.responsly.sdk.Responsly;
import com.responsly.sdk.ResponslyListener;
import com.responsly.sdk.ResponslyOptions;
import com.responsly.sdk.ShowOptions;
import com.responsly.sdk.Survey;
import com.responsly.sdk.SurveyCompletedEvent;
import com.responsly.sdk.SurveyDismissedEvent;
import com.responsly.sdk.SurveyErrorEvent;
import com.responsly.sdk.SurveyNotShownEvent;
import com.responsly.sdk.SurveyShownEvent;

import org.json.JSONException;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.Iterator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * The Android side of the Responsly Unity package: a facade over {@link Responsly} that C# calls
 * through {@code AndroidJavaClass}. Structured data crosses the boundary as JSON text, and every
 * parameter is a non-null {@code String}, a {@code boolean} or an {@code int}, so the JNI
 * signatures Unity derives from the C# arguments always match exactly.
 *
 * <p>Events and display-guard questions go back through one {@link ResponslyUnityCallback}.
 * They are raised on Android's main thread; the C# side moves them to Unity's main thread.
 */
public final class ResponslyUnityBridge {
    private static final String TAG = "Responsly";

    /** The SDK allows a survey by itself after 2 seconds; a question is forgotten a little later. */
    private static final long GUARD_FORGET_MILLIS = 5000L;

    private static final Object LOCK = new Object();
    private static final Handler MAIN = new Handler(Looper.getMainLooper());
    private static final Map<Integer, Decision> PENDING_DECISIONS = new HashMap<Integer, Decision>();

    private static volatile ResponslyUnityCallback callback;
    private static boolean listenerAdded;
    private static int lastRequestId;

    private static final ResponslyListener LISTENER = new ResponslyListener() {
        @Override
        public void onSurveyShown(SurveyShownEvent event) {
            send("survey_shown", event.toMap());
        }

        @Override
        public void onSurveyNotShown(SurveyNotShownEvent event) {
            send("survey_not_shown", event.toMap());
        }

        @Override
        public void onQuestionAnswered(QuestionAnsweredEvent event) {
            send("question_answered", withValueJson(event.toMap()));
        }

        @Override
        public void onSurveyCompleted(SurveyCompletedEvent event) {
            Map<String, Object> payload = new LinkedHashMap<String, Object>(event.toMap());
            List<Object> answers = new ArrayList<Object>();
            Object raw = payload.get("answers");
            if (raw instanceof Iterable) {
                for (Object item : (Iterable<?>) raw) {
                    if (item instanceof Map) {
                        answers.add(withValueJson((Map<?, ?>) item));
                    }
                }
            }
            payload.put("answers", answers);
            send("survey_completed", payload);
        }

        @Override
        public void onSurveyDismissed(SurveyDismissedEvent event) {
            send("survey_dismissed", event.toMap());
        }

        @Override
        public void onSurveyError(SurveyErrorEvent event) {
            send("survey_error", event.toMap());
        }
    };

    private static final DisplayGuard GUARD = new DisplayGuard() {
        @Override
        public void shouldShow(Survey survey, Decision decision) {
            if (callback == null) {
                decision.allow();
                return;
            }
            final int requestId;
            synchronized (LOCK) {
                lastRequestId = lastRequestId == Integer.MAX_VALUE ? 1 : lastRequestId + 1;
                requestId = lastRequestId;
                PENDING_DECISIONS.put(Integer.valueOf(requestId), decision);
            }
            Map<String, Object> payload = new LinkedHashMap<String, Object>();
            payload.put("request_id", Integer.valueOf(requestId));
            payload.put("survey", survey.toMap());
            send("display_guard", payload);
            MAIN.postDelayed(new Runnable() {
                @Override
                public void run() {
                    synchronized (LOCK) {
                        PENDING_DECISIONS.remove(Integer.valueOf(requestId));
                    }
                }
            }, GUARD_FORGET_MILLIS);
        }
    };

    private ResponslyUnityBridge() {
    }

    /** Registers the receiver of events and display-guard questions. Call it before anything else. */
    public static void setCallback(ResponslyUnityCallback receiver) {
        callback = receiver;
        synchronized (LOCK) {
            if (!listenerAdded) {
                listenerAdded = true;
                Responsly.addListener(LISTENER);
            }
        }
    }

    /**
     * @param context the Unity activity (any context works)
     * @param optionsJson {@code {"api_host": "https://...", "log_level": "none|error|info|debug"}},
     *     both keys optional
     */
    public static void initialize(Context context, String sdkKey, String optionsJson) {
        if (context == null) {
            Log.e(TAG, "Unity bridge: initialize was called without a context; the call is ignored");
            return;
        }
        JSONObject options = object(optionsJson);
        String apiHost = text(options, "api_host");
        ResponslyOptions parsed = new ResponslyOptions(
                apiHost == null ? ResponslyOptions.DEFAULT_API_HOST : apiHost,
                logLevel(text(options, "log_level")));
        Responsly.initialize(context, orEmpty(sdkKey), parsed);
    }

    /** @param traitsJson a JSON object of strings, numbers, booleans and nulls (null removes a trait) */
    public static void identify(String userId, String traitsJson) {
        Responsly.identify(orEmpty(userId), values(traitsJson));
    }

    /** @param traitsJson see {@link #identify} */
    public static void setAttributes(String traitsJson) {
        Responsly.setAttributes(values(traitsJson));
    }

    /** @param propertiesJson a JSON object of strings, numbers and booleans */
    public static void track(String event, String propertiesJson) {
        Responsly.track(orEmpty(event), values(propertiesJson));
    }

    public static void screen(String name) {
        Responsly.screen(orEmpty(name));
    }

    /**
     * @param optionsJson {@code {"force": true, "hidden_fields": {...}, "locale": "pl",
     *     "presentation": "bottom_sheet|dialog|fullscreen"}}, every key optional
     */
    public static void showSurvey(String surveyId, String optionsJson) {
        JSONObject options = object(optionsJson);
        String presentation = text(options, "presentation");
        ShowOptions parsed = new ShowOptions(
                options.optBoolean("force", false),
                strings(options.optJSONObject("hidden_fields")),
                text(options, "locale"),
                presentation == null ? null : PresentationStyle.fromValue(presentation));
        Responsly.showSurvey(orEmpty(surveyId), parsed);
    }

    public static void closeSurvey() {
        Responsly.closeSurvey();
    }

    public static void pause() {
        Responsly.pause();
    }

    public static void resume() {
        Responsly.resume();
    }

    /** @param languageTag a BCP 47 tag; an empty string returns to the device language */
    public static void setLocale(String languageTag) {
        Responsly.setLocale(languageTag == null || languageTag.length() == 0 ? null : languageTag);
    }

    /** @param fieldsJson a JSON object of strings */
    public static void setHiddenFields(String fieldsJson) {
        Responsly.setHiddenFields(strings(object(fieldsJson)));
    }

    public static void setTrackingEnabled(boolean enabled) {
        Responsly.setTrackingEnabled(enabled);
    }

    public static void reset() {
        Responsly.reset();
    }

    /**
     * {@code true}: before a survey becomes visible a {@code display_guard} message is sent and
     * the answer is expected through {@link #resolveDisplayGuard}. {@code false}: no guard.
     */
    public static void setDisplayGuardEnabled(boolean enabled) {
        Responsly.setDisplayGuard(enabled ? GUARD : null);
    }

    /** Answers a {@code display_guard} message. Unknown or expired ids are ignored. */
    public static void resolveDisplayGuard(int requestId, boolean allow) {
        Decision decision;
        synchronized (LOCK) {
            decision = PENDING_DECISIONS.remove(Integer.valueOf(requestId));
        }
        if (decision == null) {
            return;
        }
        if (allow) {
            decision.allow();
        } else {
            decision.veto();
        }
    }

    private static void send(String type, Map<?, ?> payload) {
        ResponslyUnityCallback receiver = callback;
        if (receiver == null) {
            return;
        }
        try {
            receiver.onMessage(type, ResponslyUnityJson.write(withoutNulls(payload)));
        } catch (RuntimeException e) {
            Log.e(TAG, "Unity bridge: the C# callback failed", e);
        }
    }

    /**
     * Replaces {@code value} (any JSON value) with {@code value_json} (its JSON text), which is
     * how C# exposes answers.
     */
    private static Map<String, Object> withValueJson(Map<?, ?> source) {
        Map<String, Object> result = new LinkedHashMap<String, Object>();
        Object value = null;
        for (Map.Entry<?, ?> entry : source.entrySet()) {
            String key = String.valueOf(entry.getKey());
            if ("value".equals(key)) {
                value = entry.getValue();
            } else {
                result.put(key, entry.getValue());
            }
        }
        result.put("value_json", ResponslyUnityJson.write(value));
        return result;
    }

    /** Drops null entries of an event (for example a trigger without a name), also in nested objects. */
    private static Map<String, Object> withoutNulls(Map<?, ?> source) {
        Map<String, Object> result = new LinkedHashMap<String, Object>();
        for (Map.Entry<?, ?> entry : source.entrySet()) {
            Object value = entry.getValue();
            if (value == null) {
                continue;
            }
            if (value instanceof Map) {
                value = withoutNulls((Map<?, ?>) value);
            }
            result.put(String.valueOf(entry.getKey()), value);
        }
        return result;
    }

    private static String orEmpty(String value) {
        return value == null ? "" : value;
    }

    private static JSONObject object(String json) {
        if (json != null && json.length() > 0) {
            try {
                return new JSONObject(json);
            } catch (JSONException e) {
                Log.e(TAG, "Unity bridge: malformed JSON from C#", e);
            }
        }
        return new JSONObject();
    }

    /** A non-empty string value, or null. */
    private static String text(JSONObject object, String key) {
        Object value = object.opt(key);
        if (value instanceof String && ((String) value).length() > 0) {
            return (String) value;
        }
        return null;
    }

    private static LogLevel logLevel(String name) {
        if ("none".equals(name)) {
            return LogLevel.NONE;
        }
        if ("info".equals(name)) {
            return LogLevel.INFO;
        }
        if ("debug".equals(name)) {
            return LogLevel.DEBUG;
        }
        return LogLevel.ERROR;
    }

    /** Trait and property values; a JSON null stays null. */
    private static Map<String, Object> values(String json) {
        JSONObject object = object(json);
        Map<String, Object> result = new LinkedHashMap<String, Object>();
        Iterator<String> keys = object.keys();
        while (keys.hasNext()) {
            String key = keys.next();
            Object value = object.opt(key);
            result.put(key, value == null || value == JSONObject.NULL ? null : value);
        }
        return result;
    }

    private static Map<String, String> strings(JSONObject object) {
        Map<String, String> result = new LinkedHashMap<String, String>();
        if (object == null) {
            return result;
        }
        Iterator<String> keys = object.keys();
        while (keys.hasNext()) {
            String key = keys.next();
            Object value = object.opt(key);
            if (value != null && value != JSONObject.NULL) {
                result.put(key, String.valueOf(value));
            }
        }
        return result;
    }
}
