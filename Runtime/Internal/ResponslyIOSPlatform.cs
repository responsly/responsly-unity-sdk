#if UNITY_IOS && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using System.Text;
using AOT;

namespace Responsly.Internal
{
    /// <summary>
    /// iOS: calls the C functions of Plugins/iOS/ResponslyUnityBridge.swift, which Unity compiles
    /// into UnityFramework. The extern names below are the exported C symbols, one to one;
    /// scripts/verify-package.sh compares the two. Strings travel as UTF-8, booleans as int.
    /// </summary>
    internal sealed class ResponslyIOSPlatform : IResponslyPlatform
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MessageCallback(IntPtr type, IntPtr json);

        [DllImport("__Internal")]
        private static extern void responsly_unity_set_callback(MessageCallback callback);

        [DllImport("__Internal")]
        private static extern void responsly_unity_initialize(string sdkKey, string optionsJson);

        [DllImport("__Internal")]
        private static extern void responsly_unity_identify(string userId, string traitsJson);

        [DllImport("__Internal")]
        private static extern void responsly_unity_set_attributes(string traitsJson);

        [DllImport("__Internal")]
        private static extern void responsly_unity_track(string eventName, string propertiesJson);

        [DllImport("__Internal")]
        private static extern void responsly_unity_screen(string name);

        [DllImport("__Internal")]
        private static extern void responsly_unity_show_survey(string surveyId, string optionsJson);

        [DllImport("__Internal")]
        private static extern void responsly_unity_close_survey();

        [DllImport("__Internal")]
        private static extern void responsly_unity_pause();

        [DllImport("__Internal")]
        private static extern void responsly_unity_resume();

        [DllImport("__Internal")]
        private static extern void responsly_unity_set_locale(string languageTag);

        [DllImport("__Internal")]
        private static extern void responsly_unity_set_hidden_fields(string fieldsJson);

        [DllImport("__Internal")]
        private static extern void responsly_unity_set_tracking_enabled(int enabled);

        [DllImport("__Internal")]
        private static extern void responsly_unity_reset();

        [DllImport("__Internal")]
        private static extern void responsly_unity_set_display_guard_enabled(int enabled);

        [DllImport("__Internal")]
        private static extern void responsly_unity_resolve_display_guard(int requestId, int allow);

        // Static, so the delegate handed to native code is never collected.
        private static readonly MessageCallback s_callback = OnMessage;
        private static Action<string, string> s_onMessage;

        public ResponslyIOSPlatform(Action<string, string> onMessage)
        {
            s_onMessage = onMessage;
            responsly_unity_set_callback(s_callback);
        }

        public void Initialize(string sdkKey, string optionsJson)
        {
            responsly_unity_initialize(sdkKey, optionsJson);
        }

        public void Identify(string userId, string traitsJson)
        {
            responsly_unity_identify(userId, traitsJson);
        }

        public void SetAttributes(string traitsJson)
        {
            responsly_unity_set_attributes(traitsJson);
        }

        public void Track(string eventName, string propertiesJson)
        {
            responsly_unity_track(eventName, propertiesJson);
        }

        public void Screen(string name)
        {
            responsly_unity_screen(name);
        }

        public void ShowSurvey(string surveyId, string optionsJson)
        {
            responsly_unity_show_survey(surveyId, optionsJson);
        }

        public void CloseSurvey()
        {
            responsly_unity_close_survey();
        }

        public void Pause()
        {
            responsly_unity_pause();
        }

        public void Resume()
        {
            responsly_unity_resume();
        }

        public void SetLocale(string languageTag)
        {
            responsly_unity_set_locale(languageTag);
        }

        public void SetHiddenFields(string fieldsJson)
        {
            responsly_unity_set_hidden_fields(fieldsJson);
        }

        public void SetTrackingEnabled(bool enabled)
        {
            responsly_unity_set_tracking_enabled(enabled ? 1 : 0);
        }

        public void Reset()
        {
            responsly_unity_reset();
        }

        public void SetDisplayGuardEnabled(bool enabled)
        {
            responsly_unity_set_display_guard_enabled(enabled ? 1 : 0);
        }

        public void ResolveDisplayGuard(int requestId, bool allow)
        {
            responsly_unity_resolve_display_guard(requestId, allow ? 1 : 0);
        }

        // Called by the Swift bridge on the main thread. The pointers are valid only during the
        // call, so the strings are copied here; the handler only queues the message.
        [MonoPInvokeCallback(typeof(MessageCallback))]
        private static void OnMessage(IntPtr type, IntPtr json)
        {
            try
            {
                Action<string, string> handler = s_onMessage;
                if (handler != null)
                {
                    handler(ReadUtf8(type), ReadUtf8(json));
                }
            }
            catch (Exception exception)
            {
                // An exception must not travel into native code.
                UnityEngine.Debug.LogException(exception);
            }
        }

        private static string ReadUtf8(IntPtr pointer)
        {
            if (pointer == IntPtr.Zero)
            {
                return string.Empty;
            }
            int length = 0;
            while (Marshal.ReadByte(pointer, length) != 0)
            {
                length++;
            }
            if (length == 0)
            {
                return string.Empty;
            }
            byte[] bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
#endif
