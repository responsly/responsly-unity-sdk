// Runs the Unity bridge on macOS. scripts/verify-package.sh appends this file to a copy of
// Plugins/iOS/ResponslyUnityBridge.swift (so the bridge's private parts are in reach), compiles
// the result as main.swift against the iOS SDK's sources built for macOS, and runs it.
//
// It checks the JSON every message carries to C#, the display-guard round trip, and that the C
// entry points reach the SDK: a showSurvey before initialize must come back as survey_not_shown.
// The SDK is never initialised here, so nothing is stored and no network request is made.

extension ResponslyUnityBridge {
    /// The bridge's private display-guard entry, as the SDK calls it.
    func testAsk(survey: ResponslySurvey, decide: @escaping (Bool) -> Void) {
        ask(survey: survey, decide: decide)
    }
}

// The C callback is not tied to an actor, so the state it writes is not either.
nonisolated(unsafe) var testMessages: [(type: String, json: String)] = []
nonisolated(unsafe) var testFailures = 0
nonisolated(unsafe) var testChecks = 0
nonisolated(unsafe) var testDecisions: [Bool] = []

let testCallback: ResponslyUnityMessageCallback = { type, json in
    guard let type = type, let json = json else {
        testFailures += 1
        print("FAIL the callback received a NULL pointer")
        return
    }
    testMessages.append((String(cString: type), String(cString: json)))
}

func testParse(_ json: String) -> NSDictionary? {
    guard let data = json.data(using: .utf8) else { return nil }
    return (try? JSONSerialization.jsonObject(with: data)) as? NSDictionary
}

func testCheck(_ name: String, _ condition: Bool, _ detail: @autoclosure () -> String = "") {
    testChecks += 1
    if condition {
        print("ok   \(name)")
    } else {
        testFailures += 1
        print("FAIL \(name) \(detail())")
    }
}

/// The last message must have this type and, parsed, equal this dictionary.
func testExpect(_ name: String, type: String, _ expected: NSDictionary) {
    guard let last = testMessages.last else {
        testCheck(name, false, "no message")
        return
    }
    testCheck(name, last.type == type && testParse(last.json) == expected, "\(last.type) \(last.json)")
}

@MainActor func testValueJson(_ value: Any) -> String {
    ResponslyUnityBridge.shared.questionAnswered(
        ResponslyQuestionAnsweredEvent(surveyId: "s", questionId: "q", type: "t", title: "T", value: value)
    )
    return (testParse(testMessages.last?.json ?? "")?["value_json"] as? String) ?? "<missing>"
}

// The SDK calls its listeners on the main actor; so does this test.
@MainActor func testRun() {

responsly_unity_set_callback(testCallback)
let testBridge = ResponslyUnityBridge.shared

// --- events

testBridge.surveyShown(ResponslySurveyShownEvent(
    survey: ResponslySurvey(id: "AbCd1234", name: "NPS \"after\" purchase"),
    trigger: ResponslyTrigger(type: .manual)
))
testExpect("survey_shown manual", type: "survey_shown", [
    "survey": ["id": "AbCd1234", "name": "NPS \"after\" purchase"],
    "trigger": ["type": "manual"],
])

testBridge.surveyShown(ResponslySurveyShownEvent(
    survey: ResponslySurvey(id: "AbCd1234", name: "NPS"),
    trigger: ResponslyTrigger(type: .event, name: "purchase_completed")
))
testExpect("survey_shown event", type: "survey_shown", [
    "survey": ["id": "AbCd1234", "name": "NPS"],
    "trigger": ["type": "event", "name": "purchase_completed"],
])

testBridge.surveyNotShown(ResponslySurveyNotShownEvent(surveyId: "AbCd1234", reason: .frequencyLimit))
testExpect("survey_not_shown", type: "survey_not_shown", ["survey_id": "AbCd1234", "reason": "frequency_limit"])

testBridge.questionAnswered(ResponslyQuestionAnsweredEvent(
    surveyId: "AbCd1234", questionId: "q1", type: "nps", title: "How likely?", value: NSNumber(value: 9)
))
testExpect("question_answered", type: "question_answered", [
    "survey_id": "AbCd1234", "question_id": "q1", "type": "nps", "title": "How likely?", "value_json": "9",
])

testCheck("value_json number", testValueJson(NSNumber(value: 9)) == "9")
testCheck("value_json fraction", testValueJson(NSNumber(value: 4.5)) == "4.5")
testCheck("value_json true", testValueJson(NSNumber(value: true)) == "true")
testCheck("value_json null", testValueJson(NSNull()) == "null")
testCheck("value_json string", testValueJson("http://a/b \"x\" zażółć") == "\"http://a/b \\\"x\\\" zażółć\"", testValueJson("http://a/b \"x\" zażółć"))
testCheck("value_json array", testValueJson(["x", "y"]) == "[\"x\",\"y\"]")
testCheck("value_json object", testValueJson(["row": ["a", "b"]]) == "{\"row\":[\"a\",\"b\"]}")
// Values as JSONSerialization really produces them for a survey's message.
let testParsedValue = (try? JSONSerialization.jsonObject(with: Data("{\"v\":[1,2.5,true,null,\"x\"]}".utf8)) as? [String: Any])?["v"] ?? NSNull()
testCheck("value_json parsed array", testValueJson(testParsedValue) == "[1,2.5,true,null,\"x\"]", testValueJson(testParsedValue))

testBridge.surveyCompleted(ResponslySurveyCompletedEvent(surveyId: "AbCd1234", responseId: "resp_1", answers: [
    ResponslyAnswer(questionId: "q1", type: "nps", title: "How likely?", value: NSNumber(value: 10)),
    ResponslyAnswer(questionId: "q2", type: "multiple_choice", title: "Pick", value: ["x", "y"]),
    ResponslyAnswer(questionId: "q3", type: "text", title: "Why?", value: NSNull()),
]))
testExpect("survey_completed", type: "survey_completed", [
    "survey_id": "AbCd1234",
    "response_id": "resp_1",
    "answers": [
        ["question_id": "q1", "type": "nps", "title": "How likely?", "value_json": "10"],
        ["question_id": "q2", "type": "multiple_choice", "title": "Pick", "value_json": "[\"x\",\"y\"]"],
        ["question_id": "q3", "type": "text", "title": "Why?", "value_json": "null"],
    ],
])

testBridge.surveyDismissed(ResponslySurveyDismissedEvent(surveyId: "AbCd1234"))
testExpect("survey_dismissed", type: "survey_dismissed", ["survey_id": "AbCd1234"])

testBridge.surveyError(ResponslySurveyErrorEvent(surveyId: "AbCd1234", code: "submit_failed", message: "Network"))
testExpect("survey_error", type: "survey_error", ["survey_id": "AbCd1234", "code": "submit_failed", "message": "Network"])

// --- display guard

testBridge.testAsk(survey: ResponslySurvey(id: "AbCd1234", name: "NPS")) { testDecisions.append($0) }
testExpect("display_guard message", type: "display_guard", ["request_id": 1, "survey": ["id": "AbCd1234", "name": "NPS"]])
testCheck("display_guard waits for the answer", testDecisions.isEmpty)
responsly_unity_resolve_display_guard(1, 0)
testCheck("display_guard veto", testDecisions == [false])
responsly_unity_resolve_display_guard(1, 1)
responsly_unity_resolve_display_guard(77, 1)
testCheck("display_guard answered once", testDecisions == [false])
testBridge.testAsk(survey: ResponslySurvey(id: "x", name: "y")) { testDecisions.append($0) }
responsly_unity_resolve_display_guard(2, 1)
testCheck("display_guard allow", testDecisions == [false, true])

// --- the C entry points, through the SDK (not initialised: calls are remembered, nothing is stored)

responsly_unity_identify("user-123", "{\"plan\":\"pro\",\"level\":12,\"beta\":true,\"gone\":null,\"when\":\"2026-11-10T12:00:00Z\"}")
responsly_unity_set_attributes("{\"plan\":\"team\",\"beta\":null}")
responsly_unity_track("level_completed", "{\"level\":12,\"stars\":3}")
responsly_unity_track("no_properties", "{}")
responsly_unity_track("malformed", "{oops")
responsly_unity_screen("MainMenu")
responsly_unity_set_locale("pl")
responsly_unity_set_locale("")
responsly_unity_set_locale(nil)
responsly_unity_set_hidden_fields("{\"run_id\":\"42\"}")
responsly_unity_set_tracking_enabled(1)
responsly_unity_pause()
responsly_unity_resume()
responsly_unity_set_display_guard_enabled(1)
responsly_unity_set_display_guard_enabled(0)
responsly_unity_close_survey()

let testBefore = testMessages.count
responsly_unity_show_survey("AbCd1234", "{\"force\":true,\"hidden_fields\":{\"run_id\":\"42\"},\"locale\":\"pl\",\"presentation\":\"dialog\"}")
// The SDK reports on the main queue.
RunLoop.main.run(until: Date().addingTimeInterval(1))
testCheck("showSurvey before initialize comes back through the SDK", testMessages.count == testBefore + 1)
testExpect("survey_not_shown not_initialized", type: "survey_not_shown", ["survey_id": "AbCd1234", "reason": "not_initialized"])

}

await testRun()

print(testFailures == 0 ? "ALL \(testChecks) CHECKS PASSED" : "\(testFailures) of \(testChecks) CHECKS FAILED")
exit(testFailures == 0 ? 0 : 1)
