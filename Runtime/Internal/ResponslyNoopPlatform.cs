using System.Globalization;

namespace Responsly.Internal
{
    /// <summary>
    /// The Editor and every platform other than iOS and Android: nothing happens. With the log
    /// level set to Debug each call writes one line to the Unity Console, so an integration can
    /// be followed in Play mode.
    /// </summary>
    internal sealed class ResponslyNoopPlatform : IResponslyPlatform
    {
        public void Initialize(string sdkKey, string optionsJson)
        {
            ResponslySDK.LogNoOp("Initialize(" + Mask(sdkKey) + ", " + optionsJson + ")");
        }

        public void Identify(string userId, string traitsJson)
        {
            ResponslySDK.LogNoOp("Identify(\"" + userId + "\", " + traitsJson + ")");
        }

        public void SetAttributes(string traitsJson)
        {
            ResponslySDK.LogNoOp("SetAttributes(" + traitsJson + ")");
        }

        public void Track(string eventName, string propertiesJson)
        {
            ResponslySDK.LogNoOp("Track(\"" + eventName + "\", " + propertiesJson + ")");
        }

        public void Screen(string name)
        {
            ResponslySDK.LogNoOp("Screen(\"" + name + "\")");
        }

        public void ShowSurvey(string surveyId, string optionsJson)
        {
            ResponslySDK.LogNoOp("ShowSurvey(\"" + surveyId + "\", " + optionsJson + ")");
        }

        public void CloseSurvey()
        {
            ResponslySDK.LogNoOp("CloseSurvey()");
        }

        public void Pause()
        {
            ResponslySDK.LogNoOp("Pause()");
        }

        public void Resume()
        {
            ResponslySDK.LogNoOp("Resume()");
        }

        public void SetLocale(string languageTag)
        {
            ResponslySDK.LogNoOp(languageTag.Length == 0 ? "SetLocale(null)" : "SetLocale(\"" + languageTag + "\")");
        }

        public void SetHiddenFields(string fieldsJson)
        {
            ResponslySDK.LogNoOp("SetHiddenFields(" + fieldsJson + ")");
        }

        public void SetTrackingEnabled(bool enabled)
        {
            ResponslySDK.LogNoOp("SetTrackingEnabled(" + (enabled ? "true" : "false") + ")");
        }

        public void Reset()
        {
            ResponslySDK.LogNoOp("Reset()");
        }

        public void SetDisplayGuardEnabled(bool enabled)
        {
            ResponslySDK.LogNoOp(enabled ? "SetDisplayGuard(guard)" : "SetDisplayGuard(null)");
        }

        public void ResolveDisplayGuard(int requestId, bool allow)
        {
            ResponslySDK.LogNoOp("display guard answer " + requestId.ToString(CultureInfo.InvariantCulture) + ": " + (allow ? "allow" : "veto"));
        }

        // The key is public, but a log line does not need all of it.
        private static string Mask(string sdkKey)
        {
            if (sdkKey.Length <= 8)
            {
                return "\"" + sdkKey + "\"";
            }
            return "\"" + sdkKey.Substring(0, 8) + "...\"";
        }
    }
}
