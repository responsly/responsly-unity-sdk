using System;
using System.Collections.Generic;
using Responsly.Internal;
using UnityEngine;

namespace Responsly
{
    /// <summary>
    /// The Responsly SDK: in-app surveys, targeted on the device.
    ///
    /// <code>
    /// ResponslySDK.Initialize("rsk_...");
    /// ResponslySDK.Identify("user-123", new Dictionary&lt;string, object&gt; { { "plan", "pro" } });
    /// ResponslySDK.Track("level_completed", new Dictionary&lt;string, object&gt; { { "level", 12 } });
    /// </code>
    ///
    /// This class forwards to the native iOS and Android SDKs that ship with the package. Every
    /// call returns at once, is safe from any thread and is safe before <see cref="Initialize"/>
    /// (it is remembered and applied when the SDK starts). Results are reported through the
    /// events, which are raised on Unity's main thread.
    ///
    /// In the Editor and on platforms other than iOS and Android every call does nothing; with
    /// <see cref="ResponslyLogLevel.Debug"/> it writes one line to the Console.
    /// </summary>
    public static class ResponslySDK
    {
        /// <summary>The version of this package and of the native SDKs inside it.</summary>
        public const string SdkVersion = "0.8.1";

        private const string LogPrefix = "[Responsly] ";

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        private static readonly bool s_isNative = true;
#else
        private static readonly bool s_isNative = false;
#endif

        private static readonly object s_platformLock = new object();
        private static IResponslyPlatform s_platform;
        private static volatile Func<ResponslySurvey, bool> s_displayGuard;
        private static volatile ResponslyLogLevel s_logLevel = ResponslyLogLevel.Error;

        /// <summary>The survey became visible. Pause the game here.</summary>
        public static event Action<SurveyShownEventArgs> SurveyShown;

        /// <summary>A survey matched a trigger or was requested with <see cref="ShowSurvey"/>, but was not shown.</summary>
        public static event Action<SurveyNotShownEventArgs> SurveyNotShown;

        /// <summary>The respondent answered a question.</summary>
        public static event Action<QuestionAnsweredEventArgs> QuestionAnswered;

        /// <summary>The respondent submitted the survey. The thank-you page is still on screen.</summary>
        public static event Action<SurveyCompletedEventArgs> SurveyCompleted;

        /// <summary>The survey was closed before it was completed.</summary>
        public static event Action<SurveyDismissedEventArgs> SurveyDismissed;

        /// <summary>The survey reported an error while it was visible.</summary>
        public static event Action<SurveyErrorEventArgs> SurveyError;

        /// <summary>
        /// Starts the SDK: loads the cached configuration, downloads a new one when needed and
        /// applies the calls made so far. Calling it again with the same key does nothing; a
        /// different key resets the local state first.
        /// </summary>
        /// <param name="sdkKey">The team's SDK key (<c>rsk_...</c>).</param>
        /// <param name="options">Log level and API host; <c>null</c> for the defaults.</param>
        public static void Initialize(string sdkKey, ResponslyOptions options = null)
        {
            ResponslyOptions effective = options ?? new ResponslyOptions();
            s_logLevel = effective.LogLevel;
            if (string.IsNullOrEmpty(sdkKey))
            {
                Warn("Initialize: the SDK key is empty; the call is ignored.");
                return;
            }
            string optionsJson = ResponslyJson.WriteInitializeOptions(effective);
            Call(() => Platform.Initialize(sdkKey, optionsJson));
        }

        /// <summary>
        /// Sets the user ID and merges the traits into the stored ones. Values are strings,
        /// numbers, booleans or dates (<see cref="DateTime"/> is sent as an ISO 8601 UTC string);
        /// a <c>null</c> value removes a trait.
        /// </summary>
        public static void Identify(string userId, IDictionary<string, object> traits = null)
        {
            if (string.IsNullOrEmpty(userId))
            {
                Warn("Identify: the user ID is empty; the call is ignored.");
                return;
            }
            string traitsJson = ResponslyJson.WriteValues(traits);
            Call(() => Platform.Identify(userId, traitsJson));
        }

        /// <summary>Merges traits without changing the user ID. A <c>null</c> value removes a trait.</summary>
        public static void SetAttributes(IDictionary<string, object> traits)
        {
            string traitsJson = ResponslyJson.WriteValues(traits);
            Call(() => Platform.SetAttributes(traitsJson));
        }

        /// <summary>Adds one to the event's count and shows a survey triggered by it, if one is eligible.</summary>
        public static void Track(string eventName, IDictionary<string, object> properties = null)
        {
            if (string.IsNullOrEmpty(eventName))
            {
                Warn("Track: the event name is empty; the call is ignored.");
                return;
            }
            string propertiesJson = ResponslyJson.WriteValues(properties);
            Call(() => Platform.Track(eventName, propertiesJson));
        }

        /// <summary>Sets the current screen and shows a survey triggered by it, if one is eligible.</summary>
        public static void Screen(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                Warn("Screen: the screen name is empty; the call is ignored.");
                return;
            }
            Call(() => Platform.Screen(name));
        }

        /// <summary>
        /// Shows a survey by its Survey ID. Audience, schedule and frequency apply unless
        /// <see cref="ShowOptions.Force"/> is set. The outcome arrives as <see cref="SurveyShown"/>
        /// or <see cref="SurveyNotShown"/>.
        /// </summary>
        public static void ShowSurvey(string surveyId, ShowOptions options = null)
        {
            if (string.IsNullOrEmpty(surveyId))
            {
                Warn("ShowSurvey: the survey ID is empty; the call is ignored.");
                return;
            }
            string optionsJson = ResponslyJson.WriteShowOptions(options ?? new ShowOptions());
            Call(() => Platform.ShowSurvey(surveyId, optionsJson));
        }

        /// <summary>
        /// Closes the survey that is visible or waiting to appear. Counts as dismissed unless the
        /// survey was completed.
        /// </summary>
        public static void CloseSurvey()
        {
            Call(() => Platform.CloseSurvey());
        }

        /// <summary>Stops surveys started by triggers. <see cref="ShowSurvey"/> still works. Not saved.</summary>
        public static void Pause()
        {
            Call(() => Platform.Pause());
        }

        /// <summary>Resumes surveys started by triggers.</summary>
        public static void Resume()
        {
            Call(() => Platform.Resume());
        }

        /// <summary>
        /// Overrides the language for surveys and for the languages audience rule, for example
        /// <c>pl</c> or <c>pt-BR</c>. <c>null</c> returns to the device language.
        /// </summary>
        public static void SetLocale(string languageTag)
        {
            string tag = languageTag ?? string.Empty;
            Call(() => Platform.SetLocale(tag));
        }

        /// <summary>
        /// Sets hidden-field values sent with every survey. Replaces the previous map; <c>null</c>
        /// or an empty map clears it.
        /// </summary>
        public static void SetHiddenFields(IDictionary<string, string> fields)
        {
            string fieldsJson = ResponslyJson.WriteStrings(fields);
            Call(() => Platform.SetHiddenFields(fieldsJson));
        }

        /// <summary>
        /// <c>false</c> switches the SDK off: no network, no targeting, nothing shown. Saved on the
        /// device and kept by <see cref="Reset"/>.
        /// </summary>
        public static void SetTrackingEnabled(bool enabled)
        {
            Call(() => Platform.SetTrackingEnabled(enabled));
        }

        /// <summary>
        /// Clears the user ID, traits, language override, hidden fields, event counts, display
        /// history and cached configuration, and closes a visible survey. Call it on logout.
        /// </summary>
        public static void Reset()
        {
            Call(() => Platform.Reset());
        }

        /// <summary>
        /// Sets a function that is asked before a survey becomes visible: return <c>false</c> to
        /// veto it. It runs on Unity's main thread. An exception counts as allow, and so does no
        /// answer within 2 seconds (for example while the game is in the background). Not asked
        /// for <see cref="ShowOptions.Force"/>. <c>null</c> removes it.
        /// </summary>
        public static void SetDisplayGuard(Func<ResponslySurvey, bool> guard)
        {
            s_displayGuard = guard;
            bool enabled = guard != null;
            Call(() => Platform.SetDisplayGuardEnabled(enabled));
        }

        // -----------------------------------------------------------------------------------------

        private static IResponslyPlatform Platform
        {
            get
            {
                lock (s_platformLock)
                {
                    if (s_platform == null)
                    {
                        s_platform = CreatePlatform();
                    }
                    return s_platform;
                }
            }
        }

        private static IResponslyPlatform CreatePlatform()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return new ResponslyAndroidPlatform(OnNativeMessage);
#elif UNITY_IOS && !UNITY_EDITOR
            return new ResponslyIOSPlatform(OnNativeMessage);
#else
            return new ResponslyNoopPlatform();
#endif
        }

        // The native SDKs are always called from Unity's main thread: at once when the caller is
        // on it, otherwise on the next frame. An SDK problem never throws into the game.
        private static void Call(Action call)
        {
            if (s_isNative)
            {
                ResponslyDispatcher.Run(call);
                return;
            }
            try
            {
                call();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        // Called by a native bridge on the platform's UI thread.
        private static void OnNativeMessage(string type, string json)
        {
            ResponslyDispatcher.Post(() => HandleMessage(type, json));
        }

        // Unity's main thread.
        private static void HandleMessage(string type, string json)
        {
            try
            {
                switch (type)
                {
                    case "survey_shown":
                    {
                        WireSurveyShown wire = JsonUtility.FromJson<WireSurveyShown>(json);
                        if (wire != null)
                        {
                            WireTrigger trigger = wire.trigger;
                            Raise(SurveyShown, new SurveyShownEventArgs(
                                ToSurvey(wire.survey),
                                new ResponslyTrigger(trigger != null ? trigger.type : null, trigger != null ? trigger.name : null)));
                        }
                        break;
                    }
                    case "survey_not_shown":
                    {
                        WireSurveyEvent wire = JsonUtility.FromJson<WireSurveyEvent>(json);
                        if (wire != null)
                        {
                            Raise(SurveyNotShown, new SurveyNotShownEventArgs(wire.survey_id, wire.reason));
                        }
                        break;
                    }
                    case "question_answered":
                    {
                        WireQuestionAnswered wire = JsonUtility.FromJson<WireQuestionAnswered>(json);
                        if (wire != null)
                        {
                            Raise(QuestionAnswered, new QuestionAnsweredEventArgs(
                                wire.survey_id, wire.question_id, wire.type, wire.title, wire.value_json));
                        }
                        break;
                    }
                    case "survey_completed":
                    {
                        WireSurveyCompleted wire = JsonUtility.FromJson<WireSurveyCompleted>(json);
                        if (wire != null)
                        {
                            List<ResponslyAnswer> answers = new List<ResponslyAnswer>();
                            if (wire.answers != null)
                            {
                                for (int index = 0; index < wire.answers.Length; index++)
                                {
                                    WireAnswer answer = wire.answers[index];
                                    if (answer != null)
                                    {
                                        answers.Add(new ResponslyAnswer(answer.question_id, answer.type, answer.title, answer.value_json));
                                    }
                                }
                            }
                            Raise(SurveyCompleted, new SurveyCompletedEventArgs(wire.survey_id, wire.response_id, answers));
                        }
                        break;
                    }
                    case "survey_dismissed":
                    {
                        WireSurveyEvent wire = JsonUtility.FromJson<WireSurveyEvent>(json);
                        if (wire != null)
                        {
                            Raise(SurveyDismissed, new SurveyDismissedEventArgs(wire.survey_id));
                        }
                        break;
                    }
                    case "survey_error":
                    {
                        WireSurveyEvent wire = JsonUtility.FromJson<WireSurveyEvent>(json);
                        if (wire != null)
                        {
                            Raise(SurveyError, new SurveyErrorEventArgs(wire.survey_id, wire.code, wire.message));
                        }
                        break;
                    }
                    case "display_guard":
                    {
                        WireDisplayGuard wire = JsonUtility.FromJson<WireDisplayGuard>(json);
                        if (wire != null)
                        {
                            AnswerDisplayGuard(wire);
                        }
                        break;
                    }
                    default:
                        // A newer native SDK may send messages this version does not know.
                        break;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(LogPrefix + "Could not handle a '" + type + "' message: " + exception.Message);
            }
        }

        private static void AnswerDisplayGuard(WireDisplayGuard wire)
        {
            bool allow = true;
            Func<ResponslySurvey, bool> guard = s_displayGuard;
            if (guard != null)
            {
                try
                {
                    allow = guard(ToSurvey(wire.survey));
                }
                catch (Exception exception)
                {
                    // A broken guard must not block surveys.
                    Debug.LogException(exception);
                    allow = true;
                }
            }
            Platform.ResolveDisplayGuard(wire.request_id, allow);
        }

        private static ResponslySurvey ToSurvey(WireSurvey wire)
        {
            return wire == null ? new ResponslySurvey(null, null) : new ResponslySurvey(wire.id, wire.name);
        }

        // Every handler is called, also when an earlier one throws.
        private static void Raise<T>(Action<T> handlers, T args)
        {
            if (handlers == null)
            {
                return;
            }
            Delegate[] list = handlers.GetInvocationList();
            for (int index = 0; index < list.Length; index++)
            {
                try
                {
                    ((Action<T>)list[index])(args);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private static void Warn(string message)
        {
            if (s_logLevel != ResponslyLogLevel.None)
            {
                Debug.LogWarning(LogPrefix + message);
            }
        }

        // Used by ResponslyNoopPlatform.
        internal static void LogNoOp(string call)
        {
            if (s_logLevel == ResponslyLogLevel.Debug)
            {
                Debug.Log(LogPrefix + call + " - no-op: the SDK only runs in iOS and Android players.");
            }
        }
    }
}
