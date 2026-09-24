// Responsly Unity package - the iOS side.
//
// C entry points over the ResponslySDK framework. The C# side (Runtime/Internal/
// ResponslyIOSPlatform.cs) calls them with DllImport("__Internal"); Unity compiles this file into
// UnityFramework. Structured data crosses the boundary as JSON text, booleans as Int32.
//
// Events and display-guard questions go back through one C callback, on the main thread; the C#
// side queues them for Unity's next frame.

import Foundation
import ResponslySDK

/// `type` is `survey_shown`, `survey_not_shown`, `question_answered`, `survey_completed`,
/// `survey_dismissed`, `survey_error` or `display_guard`; `json` is a JSON object with snake_case
/// keys. Both are UTF-8 C strings that are valid only during the call.
public typealias ResponslyUnityMessageCallback = @convention(c) (UnsafePointer<CChar>?, UnsafePointer<CChar>?) -> Void

private final class ResponslyUnityBridge: ResponslyListener, @unchecked Sendable {
    static let shared = ResponslyUnityBridge()

    /// The SDK allows a survey by itself after 2 seconds; a question is forgotten a little later.
    private static let guardForgetSeconds: TimeInterval = 5

    private let lock = NSLock()
    private let registerListener: (ResponslyListener) -> ResponslyListenerToken
    private var callback: ResponslyUnityMessageCallback?
    private var listenerToken: ResponslyListenerToken?
    private var pendingDecisions: [Int32: (Bool) -> Void] = [:]
    private var lastRequestId: Int32 = 0

    private init(registerListener: @escaping (ResponslyListener) -> ResponslyListenerToken = {
        Responsly.shared.addListener($0)
    }) {
        self.registerListener = registerListener
    }

    func setCallback(_ callback: ResponslyUnityMessageCallback?) {
        lock.lock()
        defer { lock.unlock() }
        self.callback = callback
        if listenerToken == nil {
            listenerToken = registerListener(self)
        }
    }

    func setDisplayGuardEnabled(_ enabled: Bool) {
        guard enabled else {
            Responsly.shared.setDisplayGuard(nil)
            return
        }
        Responsly.shared.setDisplayGuard { survey, decide in
            ResponslyUnityBridge.shared.ask(survey: survey, decide: decide)
        }
    }

    func resolveDisplayGuard(requestId: Int32, allow: Bool) {
        takeDecision(requestId)?(allow)
    }

    private func ask(survey: ResponslySurvey, decide: @escaping (Bool) -> Void) {
        lock.lock()
        guard callback != nil else {
            lock.unlock()
            decide(true)
            return
        }
        lastRequestId = lastRequestId == Int32.max ? 1 : lastRequestId + 1
        let requestId = lastRequestId
        pendingDecisions[requestId] = decide
        lock.unlock()

        send("display_guard", ["request_id": Int(requestId), "survey": survey.toDictionary()])

        DispatchQueue.main.asyncAfter(deadline: .now() + ResponslyUnityBridge.guardForgetSeconds) {
            _ = ResponslyUnityBridge.shared.takeDecision(requestId)
        }
    }

    private func takeDecision(_ requestId: Int32) -> ((Bool) -> Void)? {
        lock.lock()
        defer { lock.unlock() }
        return pendingDecisions.removeValue(forKey: requestId)
    }

    private func send(_ type: String, _ payload: [String: Any]) {
        lock.lock()
        let callback = self.callback
        lock.unlock()
        guard let callback = callback else { return }
        guard
            JSONSerialization.isValidJSONObject(payload),
            let data = try? JSONSerialization.data(withJSONObject: payload, options: [.withoutEscapingSlashes]),
            let json = String(data: data, encoding: .utf8)
        else {
            NSLog("[Responsly] Unity bridge: could not encode a %@ message", type)
            return
        }
        type.withCString { typePointer in
            json.withCString { jsonPointer in
                callback(typePointer, jsonPointer)
            }
        }
    }

    // MARK: - ResponslyListener

    func surveyShown(_ event: ResponslySurveyShownEvent) {
        send("survey_shown", event.toDictionary())
    }

    func surveyNotShown(_ event: ResponslySurveyNotShownEvent) {
        send("survey_not_shown", event.toDictionary())
    }

    func questionAnswered(_ event: ResponslyQuestionAnsweredEvent) {
        send("question_answered", ResponslyUnityBridge.withValueJson(event.toDictionary()))
    }

    func surveyCompleted(_ event: ResponslySurveyCompletedEvent) {
        var payload = event.toDictionary()
        payload["answers"] = event.answers.map { ResponslyUnityBridge.withValueJson($0.toDictionary()) }
        send("survey_completed", payload)
    }

    func surveyDismissed(_ event: ResponslySurveyDismissedEvent) {
        send("survey_dismissed", event.toDictionary())
    }

    func surveyError(_ event: ResponslySurveyErrorEvent) {
        send("survey_error", event.toDictionary())
    }

    // MARK: - JSON

    /// Replaces `value` (any JSON value) with `value_json` (its JSON text), which is how C#
    /// exposes answers.
    private static func withValueJson(_ dictionary: [String: Any]) -> [String: Any] {
        var result = dictionary
        let value = result.removeValue(forKey: "value") ?? NSNull()
        result["value_json"] = jsonText(value)
        return result
    }

    private static func jsonText(_ value: Any) -> String {
        guard
            JSONSerialization.isValidJSONObject([value]),
            let data = try? JSONSerialization.data(
                withJSONObject: value,
                options: [.fragmentsAllowed, .withoutEscapingSlashes]
            ),
            let text = String(data: data, encoding: .utf8)
        else {
            return "null"
        }
        return text
    }
}

// MARK: - Reading arguments

private func responslyUnityString(_ pointer: UnsafePointer<CChar>?) -> String {
    guard let pointer = pointer else { return "" }
    return String(cString: pointer)
}

private func responslyUnityObject(_ pointer: UnsafePointer<CChar>?) -> [String: Any] {
    let text = responslyUnityString(pointer)
    guard !text.isEmpty, let data = text.data(using: .utf8) else { return [:] }
    guard let parsed = try? JSONSerialization.jsonObject(with: data), let dictionary = parsed as? [String: Any] else {
        NSLog("[Responsly] Unity bridge: malformed JSON from C#")
        return [:]
    }
    return dictionary
}

private func responslyUnityStrings(_ value: Any?) -> [String: String] {
    guard let dictionary = value as? [String: Any] else { return [:] }
    var result: [String: String] = [:]
    for (key, raw) in dictionary {
        if let text = raw as? String {
            result[key] = text
        }
    }
    return result
}

private func responslyUnityText(_ dictionary: [String: Any], _ key: String) -> String? {
    guard let text = dictionary[key] as? String, !text.isEmpty else { return nil }
    return text
}

// MARK: - C entry points

/// Registers the receiver of events and display-guard questions. Call it before anything else.
@_cdecl("responsly_unity_set_callback")
public func responsly_unity_set_callback(_ callback: ResponslyUnityMessageCallback?) {
    ResponslyUnityBridge.shared.setCallback(callback)
}

/// `optionsJson`: `{"api_host": "https://...", "log_level": "none|error|info|debug"}`, both optional.
@_cdecl("responsly_unity_initialize")
public func responsly_unity_initialize(_ sdkKey: UnsafePointer<CChar>?, _ optionsJson: UnsafePointer<CChar>?) {
    let dictionary = responslyUnityObject(optionsJson)
    var options = ResponslyOptions()
    if let host = responslyUnityText(dictionary, "api_host") {
        if let url = URL(string: host) {
            options.apiHost = url
        } else {
            NSLog("[Responsly] Unity bridge: the API host is not a valid URL; the default is used")
        }
    }
    if let name = responslyUnityText(dictionary, "log_level"), let level = ResponslyLogLevel(name: name) {
        options.logLevel = level
    }
    Responsly.shared.initialize(sdkKey: responslyUnityString(sdkKey), options: options)
}

/// `traitsJson`: a JSON object of strings, numbers, booleans and nulls (null removes a trait).
@_cdecl("responsly_unity_identify")
public func responsly_unity_identify(_ userId: UnsafePointer<CChar>?, _ traitsJson: UnsafePointer<CChar>?) {
    Responsly.shared.identify(
        userId: responslyUnityString(userId),
        traits: ResponslyValue.traits(from: responslyUnityObject(traitsJson))
    )
}

@_cdecl("responsly_unity_set_attributes")
public func responsly_unity_set_attributes(_ traitsJson: UnsafePointer<CChar>?) {
    Responsly.shared.setAttributes(ResponslyValue.traits(from: responslyUnityObject(traitsJson)))
}

/// `propertiesJson`: a JSON object of strings, numbers and booleans.
@_cdecl("responsly_unity_track")
public func responsly_unity_track(_ event: UnsafePointer<CChar>?, _ propertiesJson: UnsafePointer<CChar>?) {
    Responsly.shared.track(
        responslyUnityString(event),
        properties: ResponslyValue.properties(from: responslyUnityObject(propertiesJson))
    )
}

@_cdecl("responsly_unity_screen")
public func responsly_unity_screen(_ name: UnsafePointer<CChar>?) {
    Responsly.shared.screen(responslyUnityString(name))
}

/// `optionsJson`: `{"force": true, "hidden_fields": {...}, "locale": "pl",
/// "presentation": "bottom_sheet|dialog|fullscreen"}`, every key optional.
@_cdecl("responsly_unity_show_survey")
public func responsly_unity_show_survey(_ surveyId: UnsafePointer<CChar>?, _ optionsJson: UnsafePointer<CChar>?) {
    let dictionary = responslyUnityObject(optionsJson)
    let options = ResponslyShowOptions(
        force: (dictionary["force"] as? Bool) ?? false,
        hiddenFields: responslyUnityStrings(dictionary["hidden_fields"]),
        locale: responslyUnityText(dictionary, "locale"),
        presentation: responslyUnityText(dictionary, "presentation").flatMap { ResponslyPresentationStyle(rawValue: $0) }
    )
    Responsly.shared.showSurvey(responslyUnityString(surveyId), options: options)
}

@_cdecl("responsly_unity_close_survey")
public func responsly_unity_close_survey() {
    Responsly.shared.closeSurvey()
}

@_cdecl("responsly_unity_pause")
public func responsly_unity_pause() {
    Responsly.shared.pause()
}

@_cdecl("responsly_unity_resume")
public func responsly_unity_resume() {
    Responsly.shared.resume()
}

/// `languageTag`: a BCP 47 tag; NULL or an empty string returns to the device language.
@_cdecl("responsly_unity_set_locale")
public func responsly_unity_set_locale(_ languageTag: UnsafePointer<CChar>?) {
    let tag = responslyUnityString(languageTag)
    Responsly.shared.setLocale(tag.isEmpty ? nil : tag)
}

/// `fieldsJson`: a JSON object of strings.
@_cdecl("responsly_unity_set_hidden_fields")
public func responsly_unity_set_hidden_fields(_ fieldsJson: UnsafePointer<CChar>?) {
    Responsly.shared.setHiddenFields(responslyUnityStrings(responslyUnityObject(fieldsJson)))
}

@_cdecl("responsly_unity_set_tracking_enabled")
public func responsly_unity_set_tracking_enabled(_ enabled: Int32) {
    Responsly.shared.setTrackingEnabled(enabled != 0)
}

@_cdecl("responsly_unity_reset")
public func responsly_unity_reset() {
    Responsly.shared.reset()
}

/// Non-zero: before a survey becomes visible a `display_guard` message is sent and the answer is
/// expected through `responsly_unity_resolve_display_guard`. Zero: no guard.
@_cdecl("responsly_unity_set_display_guard_enabled")
public func responsly_unity_set_display_guard_enabled(_ enabled: Int32) {
    ResponslyUnityBridge.shared.setDisplayGuardEnabled(enabled != 0)
}

/// Answers a `display_guard` message. Unknown or expired ids are ignored.
@_cdecl("responsly_unity_resolve_display_guard")
public func responsly_unity_resolve_display_guard(_ requestId: Int32, _ allow: Int32) {
    ResponslyUnityBridge.shared.resolveDisplayGuard(requestId: requestId, allow: allow != 0)
}
