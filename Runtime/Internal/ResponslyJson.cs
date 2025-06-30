using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Responsly.Internal
{
    /// <summary>
    /// The JSON this package needs: writing the arguments that go to the native SDKs, and reading
    /// a single answer value (<c>ValueJson</c>). Events are read with <c>JsonUtility</c>.
    /// </summary>
    internal static class ResponslyJson
    {
        /// <summary>Trait and property maps: strings, numbers, booleans, dates and nulls.</summary>
        public static string WriteValues(IDictionary<string, object> values)
        {
            StringBuilder builder = new StringBuilder(64);
            builder.Append('{');
            if (values != null)
            {
                bool first = true;
                foreach (KeyValuePair<string, object> pair in values)
                {
                    if (pair.Key == null)
                    {
                        continue;
                    }
                    if (!first)
                    {
                        builder.Append(',');
                    }
                    first = false;
                    AppendString(builder, pair.Key);
                    builder.Append(':');
                    AppendValue(builder, pair.Value);
                }
            }
            builder.Append('}');
            return builder.ToString();
        }

        /// <summary>Hidden-field maps. Entries with a null value are left out.</summary>
        public static string WriteStrings(IDictionary<string, string> values)
        {
            StringBuilder builder = new StringBuilder(64);
            AppendStrings(builder, values);
            return builder.ToString();
        }

        public static string WriteInitializeOptions(ResponslyOptions options)
        {
            StringBuilder builder = new StringBuilder(96);
            builder.Append("{\"log_level\":");
            AppendString(builder, LogLevelName(options.LogLevel));
            if (!string.IsNullOrEmpty(options.ApiHost))
            {
                builder.Append(",\"api_host\":");
                AppendString(builder, options.ApiHost);
            }
            builder.Append('}');
            return builder.ToString();
        }

        public static string WriteShowOptions(ShowOptions options)
        {
            StringBuilder builder = new StringBuilder(96);
            builder.Append("{\"force\":");
            builder.Append(options.Force ? "true" : "false");
            if (options.HiddenFields != null && options.HiddenFields.Count > 0)
            {
                builder.Append(",\"hidden_fields\":");
                AppendStrings(builder, options.HiddenFields);
            }
            if (!string.IsNullOrEmpty(options.Locale))
            {
                builder.Append(",\"locale\":");
                AppendString(builder, options.Locale);
            }
            if (options.Presentation.HasValue)
            {
                builder.Append(",\"presentation\":");
                AppendString(builder, PresentationName(options.Presentation.Value));
            }
            builder.Append('}');
            return builder.ToString();
        }

        public static string LogLevelName(ResponslyLogLevel level)
        {
            switch (level)
            {
                case ResponslyLogLevel.None:
                    return "none";
                case ResponslyLogLevel.Info:
                    return "info";
                case ResponslyLogLevel.Debug:
                    return "debug";
                default:
                    return "error";
            }
        }

        public static string PresentationName(ResponslyPresentation presentation)
        {
            switch (presentation)
            {
                case ResponslyPresentation.Dialog:
                    return "dialog";
                case ResponslyPresentation.Fullscreen:
                    return "fullscreen";
                default:
                    return "bottom_sheet";
            }
        }

        /// <summary>ISO 8601, UTC, seconds precision: <c>2026-11-10T12:00:00Z</c>.</summary>
        public static string FormatDate(DateTime value)
        {
            return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        /// <summary>The text of a JSON string literal, or null when <paramref name="json"/> is not one.</summary>
        public static string ReadString(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }
            string text = json.Trim();
            if (text.Length < 2 || text[0] != '"' || text[text.Length - 1] != '"')
            {
                return null;
            }

            int end = text.Length - 1;
            StringBuilder builder = new StringBuilder(text.Length);
            for (int index = 1; index < end; index++)
            {
                char c = text[index];
                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                index++;
                if (index >= end)
                {
                    return null;
                }
                switch (text[index])
                {
                    case '"':
                        builder.Append('"');
                        break;
                    case '\\':
                        builder.Append('\\');
                        break;
                    case '/':
                        builder.Append('/');
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u':
                    {
                        // Four hexadecimal digits follow.
                        if (index + 4 >= end)
                        {
                            return null;
                        }
                        int code = 0;
                        if (!int.TryParse(text.Substring(index + 1, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code))
                        {
                            return null;
                        }
                        builder.Append((char)code);
                        index += 4;
                        break;
                    }
                    default:
                        return null;
                }
            }
            return builder.ToString();
        }

        /// <summary>The value of a JSON number, or null when <paramref name="json"/> is not one.</summary>
        public static double? ReadDouble(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }
            string text = json.Trim();
            if (text.Length == 0)
            {
                return null;
            }
            char first = text[0];
            if (first != '-' && (first < '0' || first > '9'))
            {
                return null;
            }
            double value;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return null;
            }
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return null;
            }
            return value;
        }

        /// <summary>The value of a JSON <c>true</c> or <c>false</c>, or null for anything else.</summary>
        public static bool? ReadBool(string json)
        {
            if (json == null)
            {
                return null;
            }
            string text = json.Trim();
            if (text == "true")
            {
                return true;
            }
            if (text == "false")
            {
                return false;
            }
            return null;
        }

        private static void AppendStrings(StringBuilder builder, IDictionary<string, string> values)
        {
            builder.Append('{');
            if (values != null)
            {
                bool first = true;
                foreach (KeyValuePair<string, string> pair in values)
                {
                    if (pair.Key == null || pair.Value == null)
                    {
                        continue;
                    }
                    if (!first)
                    {
                        builder.Append(',');
                    }
                    first = false;
                    AppendString(builder, pair.Key);
                    builder.Append(':');
                    AppendString(builder, pair.Value);
                }
            }
            builder.Append('}');
        }

        private static void AppendValue(StringBuilder builder, object value)
        {
            if (value == null)
            {
                // Removes a trait.
                builder.Append("null");
            }
            else if (value is string)
            {
                AppendString(builder, (string)value);
            }
            else if (value is bool)
            {
                builder.Append((bool)value ? "true" : "false");
            }
            else if (value is int || value is long || value is short || value is byte
                || value is uint || value is ulong || value is ushort || value is sbyte)
            {
                builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
            else if (value is double)
            {
                AppendNumber(builder, (double)value, ((double)value).ToString("R", CultureInfo.InvariantCulture));
            }
            else if (value is float)
            {
                AppendNumber(builder, (float)value, ((float)value).ToString("R", CultureInfo.InvariantCulture));
            }
            else if (value is decimal)
            {
                builder.Append(((decimal)value).ToString(CultureInfo.InvariantCulture));
            }
            else if (value is DateTime)
            {
                AppendString(builder, FormatDate((DateTime)value));
            }
            else if (value is DateTimeOffset)
            {
                AppendString(builder, FormatDate(((DateTimeOffset)value).UtcDateTime));
            }
            else
            {
                // Enums, chars and everything else travel as their text.
                AppendString(builder, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
            }
        }

        private static void AppendNumber(StringBuilder builder, double value, string text)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                // JSON has no such numbers; they are kept as text.
                AppendString(builder, text);
            }
            else
            {
                builder.Append(text);
            }
        }

        private static void AppendString(StringBuilder builder, string text)
        {
            builder.Append('"');
            for (int index = 0; index < text.Length; index++)
            {
                char c = text[index];
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    default:
                        if (c < ' ')
                        {
                            builder.Append("\\u");
                            builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }
            builder.Append('"');
        }
    }
}
