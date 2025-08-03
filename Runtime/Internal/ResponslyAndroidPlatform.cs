#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace Responsly.Internal
{
    /// <summary>
    /// Android: calls <c>com.responsly.unity.ResponslyUnityBridge</c>, which is part of
    /// Plugins/Android/responsly-sdk.aar (source: Native~/android/java). Every argument is a
    /// non-null string, a bool or an int, so the JNI signature Unity derives from the arguments is
    /// exactly the Java one. scripts/verify-package.sh compares the two.
    /// </summary>
    internal sealed class ResponslyAndroidPlatform : IResponslyPlatform
    {
        private const string BridgeClassName = "com.responsly.unity.ResponslyUnityBridge";
        private const string CallbackInterfaceName = "com.responsly.unity.ResponslyUnityCallback";

        private readonly AndroidJavaClass _bridge;

        // Kept so the proxy lives as long as the Java side may call it.
        private readonly MessageProxy _proxy;

        public ResponslyAndroidPlatform(Action<string, string> onMessage)
        {
            _bridge = new AndroidJavaClass(BridgeClassName);
            _proxy = new MessageProxy(onMessage);
            _bridge.CallStatic("setCallback", _proxy);
        }

        public void Initialize(string sdkKey, string optionsJson)
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                if (activity == null)
                {
                    Debug.LogError("[Responsly] Initialize: Unity has no current activity yet; the call is ignored.");
                    return;
                }
                _bridge.CallStatic("initialize", activity, sdkKey, optionsJson);
            }
        }

        public void Identify(string userId, string traitsJson)
        {
            _bridge.CallStatic("identify", userId, traitsJson);
        }

        public void SetAttributes(string traitsJson)
        {
            _bridge.CallStatic("setAttributes", traitsJson);
        }

        public void Track(string eventName, string propertiesJson)
        {
            _bridge.CallStatic("track", eventName, propertiesJson);
        }

        public void Screen(string name)
        {
            _bridge.CallStatic("screen", name);
        }

        public void ShowSurvey(string surveyId, string optionsJson)
        {
            _bridge.CallStatic("showSurvey", surveyId, optionsJson);
        }

        public void CloseSurvey()
        {
            _bridge.CallStatic("closeSurvey");
        }

        public void Pause()
        {
            _bridge.CallStatic("pause");
        }

        public void Resume()
        {
            _bridge.CallStatic("resume");
        }

        public void SetLocale(string languageTag)
        {
            _bridge.CallStatic("setLocale", languageTag);
        }

        public void SetHiddenFields(string fieldsJson)
        {
            _bridge.CallStatic("setHiddenFields", fieldsJson);
        }

        public void SetTrackingEnabled(bool enabled)
        {
            _bridge.CallStatic("setTrackingEnabled", enabled);
        }

        public void Reset()
        {
            _bridge.CallStatic("reset");
        }

        public void SetDisplayGuardEnabled(bool enabled)
        {
            _bridge.CallStatic("setDisplayGuardEnabled", enabled);
        }

        public void ResolveDisplayGuard(int requestId, bool allow)
        {
            _bridge.CallStatic("resolveDisplayGuard", requestId, allow);
        }

        /// <summary>Implements <c>com.responsly.unity.ResponslyUnityCallback</c>.</summary>
        [Preserve]
        private sealed class MessageProxy : AndroidJavaProxy
        {
            private readonly Action<string, string> _onMessage;

            public MessageProxy(Action<string, string> onMessage) : base(CallbackInterfaceName)
            {
                _onMessage = onMessage;
            }

            // Found by name from Java and called on Android's main thread. Both arguments are
            // never null (the Java side guarantees it); the handler only queues the message.
            [Preserve]
            public void onMessage(string type, string json)
            {
                _onMessage(type, json);
            }
        }
    }
}
#endif
