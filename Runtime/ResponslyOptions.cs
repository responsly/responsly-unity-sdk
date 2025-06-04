using System.Collections.Generic;

namespace Responsly
{
    /// <summary>
    /// How much the SDK writes to the device log (Xcode console on iOS, Logcat on Android),
    /// from silent to everything. Every line is prefixed <c>[Responsly]</c>.
    /// </summary>
    public enum ResponslyLogLevel
    {
        None = 0,
        Error = 1,
        Info = 2,

        /// <summary>Every targeting decision, configuration download and survey message.</summary>
        Debug = 3,
    }

    /// <summary>The container a survey is presented in.</summary>
    public enum ResponslyPresentation
    {
        BottomSheet = 0,
        Dialog = 1,
        Fullscreen = 2,
    }

    /// <summary>Options for <see cref="ResponslySDK.Initialize"/>.</summary>
    public sealed class ResponslyOptions
    {
        /// <summary><c>https://api.responsly.com</c></summary>
        public const string DefaultApiHost = "https://api.responsly.com";

        public ResponslyOptions()
        {
            ApiHost = DefaultApiHost;
            LogLevel = ResponslyLogLevel.Error;
        }

        /// <summary>
        /// Base address of the Responsly API. Leave it alone unless Responsly support gives you
        /// another host. <c>null</c> or an empty string means the default.
        /// </summary>
        public string ApiHost { get; set; }

        /// <summary>Defaults to <see cref="ResponslyLogLevel.Error"/>.</summary>
        public ResponslyLogLevel LogLevel { get; set; }
    }

    /// <summary>Options for <see cref="ResponslySDK.ShowSurvey"/>. Every property is optional.</summary>
    public sealed class ShowOptions
    {
        /// <summary>
        /// Skip audience, schedule, frequency and the display guard. A survey is still not shown
        /// while tracking is disabled, when the ID is unknown or while another survey is visible.
        /// </summary>
        public bool Force { get; set; }

        /// <summary>
        /// Hidden-field values for this survey only. Combined with the
        /// <see cref="ResponslySDK.SetHiddenFields"/> map; a key present in both takes this value.
        /// </summary>
        public IDictionary<string, string> HiddenFields { get; set; }

        /// <summary>Language tag for this survey only, for example <c>pl</c> or <c>pt-BR</c>.</summary>
        public string Locale { get; set; }

        /// <summary>Overrides the style set for the survey in Responsly. <c>null</c> keeps it.</summary>
        public ResponslyPresentation? Presentation { get; set; }
    }
}
