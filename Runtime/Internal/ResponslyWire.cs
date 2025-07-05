using System;
using UnityEngine.Scripting;

// The fields below are filled by JsonUtility, never by code.
#pragma warning disable 0649

namespace Responsly.Internal
{
    // The messages the native bridges send (Plugins/iOS/ResponslyUnityBridge.swift and
    // Native~/android/java/.../ResponslyUnityBridge.java), in the shape JsonUtility reads:
    // snake_case keys as field names. An answer's value arrives as JSON text in value_json.
    // [Preserve]: the classes are only ever created by JsonUtility, which managed code stripping
    // cannot see.

    [Serializable, Preserve]
    internal sealed class WireSurvey
    {
        public string id;
        public string name;
    }

    [Serializable, Preserve]
    internal sealed class WireTrigger
    {
        public string type;
        public string name;
    }

    [Serializable, Preserve]
    internal sealed class WireAnswer
    {
        public string question_id;
        public string type;
        public string title;
        public string value_json;
    }

    /// <summary>survey_shown</summary>
    [Serializable, Preserve]
    internal sealed class WireSurveyShown
    {
        public WireSurvey survey;
        public WireTrigger trigger;
    }

    /// <summary>survey_not_shown, survey_dismissed and survey_error</summary>
    [Serializable, Preserve]
    internal sealed class WireSurveyEvent
    {
        public string survey_id;
        public string reason;
        public string code;
        public string message;
    }

    /// <summary>question_answered</summary>
    [Serializable, Preserve]
    internal sealed class WireQuestionAnswered
    {
        public string survey_id;
        public string question_id;
        public string type;
        public string title;
        public string value_json;
    }

    /// <summary>survey_completed</summary>
    [Serializable, Preserve]
    internal sealed class WireSurveyCompleted
    {
        public string survey_id;
        public string response_id;
        public WireAnswer[] answers;
    }

    /// <summary>display_guard</summary>
    [Serializable, Preserve]
    internal sealed class WireDisplayGuard
    {
        public int request_id;
        public WireSurvey survey;
    }
}

#pragma warning restore 0649
