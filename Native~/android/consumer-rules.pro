
# Responsly Unity package - appended to the SDK's consumer rules by scripts/build-native.sh.
# C# calls the bridge by name through JNI (AndroidJavaClass, AndroidJavaProxy), so it must
# survive shrinking and keep its names.
-keep public class com.responsly.unity.ResponslyUnityBridge {
    public *;
}
-keep public interface com.responsly.unity.ResponslyUnityCallback {
    *;
}
