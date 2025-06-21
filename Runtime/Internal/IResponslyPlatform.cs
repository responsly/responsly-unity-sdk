namespace Responsly.Internal
{
    /// <summary>
    /// What a native bridge offers. One implementation per platform; every method maps to one
    /// function of the bridge. Structured arguments are JSON text and no argument is ever null.
    /// Called on Unity's main thread only.
    /// </summary>
    internal interface IResponslyPlatform
    {
        void Initialize(string sdkKey, string optionsJson);

        void Identify(string userId, string traitsJson);

        void SetAttributes(string traitsJson);

        void Track(string eventName, string propertiesJson);

        void Screen(string name);

        void ShowSurvey(string surveyId, string optionsJson);

        void CloseSurvey();

        void Pause();

        void Resume();

        /// <param name="languageTag">An empty string returns to the device language.</param>
        void SetLocale(string languageTag);

        void SetHiddenFields(string fieldsJson);

        void SetTrackingEnabled(bool enabled);

        void Reset();

        /// <summary>
        /// Enabled: the bridge sends a <c>display_guard</c> message before a survey becomes
        /// visible and waits for <see cref="ResolveDisplayGuard"/>.
        /// </summary>
        void SetDisplayGuardEnabled(bool enabled);

        void ResolveDisplayGuard(int requestId, bool allow);
    }
}
