using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Responsly.Editor
{
    /// <summary>
    /// Looks at the project before an iOS or Android build and warns in the Console about the
    /// setup mistakes that otherwise show up late: as a Gradle or Xcode error, or as a survey
    /// that never appears on the device. It only warns; it never changes the project and never
    /// fails a build.
    /// </summary>
    internal sealed class ResponslyBuildChecks : IPreprocessBuildWithReport
    {
        private const string Prefix = "[Responsly] ";
        private const int MinimumAndroidApiLevel = 23;
        private const string WebkitDependency = "androidx.webkit:webkit";
        private const string KotlinDependency = "org.jetbrains.kotlin:kotlin-stdlib";

        public int callbackOrder
        {
            get { return 0; }
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            try
            {
                BuildTarget target = report.summary.platform;
                if (target == BuildTarget.Android)
                {
                    CheckAndroid();
                }
                else if (target == BuildTarget.iOS)
                {
                    CheckIOS();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(Prefix + "The build checks could not run: " + exception.Message);
            }
        }

        private static void CheckAndroid()
        {
            if ((int)PlayerSettings.Android.minSdkVersion < MinimumAndroidApiLevel)
            {
                Debug.LogWarning(
                    Prefix + "The SDK needs Android 6.0 (API level 23) or later. Set Project Settings > Player > " +
                    "Android > Minimum API Level to 23, or the Gradle build stops at the manifest merge.");
            }

            // Unity builds with Android Gradle Plugin 7.4.2 from 2022.3.38f1 on and with 7.1.2
            // before; androidx.webkit 1.15.0 declares 7.2 as its minimum.
            int major;
            int minor;
            int patch;
            if (TryReadUnityVersion(Application.unityVersion, out major, out minor, out patch)
                && major == 2022 && minor == 3 && patch < 38)
            {
                Debug.LogWarning(
                    Prefix + "This Unity version (" + Application.unityVersion + ") builds with Android Gradle Plugin 7.1.2, " +
                    "and androidx.webkit 1.15.0, which the SDK depends on, asks for 7.2 or later. If the Gradle build " +
                    "stops at checkAarMetadata, update to Unity 2022.3.38f1 or later.");
            }

            if (HasDependencyManager())
            {
                // It reads Editor/ResponslyDependencies.xml and adds the libraries by itself.
                return;
            }

            string template = Path.Combine(Application.dataPath, "Plugins/Android/mainTemplate.gradle");
            string gradle = File.Exists(template) ? File.ReadAllText(template) : string.Empty;
            if (gradle.IndexOf(WebkitDependency, StringComparison.Ordinal) >= 0
                && gradle.IndexOf(KotlinDependency, StringComparison.Ordinal) >= 0)
            {
                return;
            }

            Debug.LogWarning(
                Prefix + "The Android libraries the SDK depends on are not part of this build, so no survey can be shown. " +
                "Either install the External Dependency Manager for Unity (it reads the package's " +
                "Editor/ResponslyDependencies.xml), or enable Project Settings > Player > Android > Publishing Settings > " +
                "Custom Main Gradle Template and add to the dependencies block of Assets/Plugins/Android/mainTemplate.gradle:\n" +
                "    implementation 'androidx.webkit:webkit:1.15.0'\n" +
                "    implementation 'org.jetbrains.kotlin:kotlin-stdlib:2.0.21'\n" +
                "See the package README, \"Android dependencies\".");
        }

        private static void CheckIOS()
        {
            // Unity imports .xcframework plug-ins from 2022.3.23f1 on. Earlier 2022.3 releases
            // leave ResponslySDK.xcframework out of the Xcode project.
            int major;
            int minor;
            int patch;
            if (TryReadUnityVersion(Application.unityVersion, out major, out minor, out patch)
                && major == 2022 && minor == 3 && patch < 23)
            {
                Debug.LogWarning(
                    Prefix + "iOS builds need Unity 2022.3.23f1 or later: this version (" + Application.unityVersion +
                    ") does not import .xcframework plug-ins, so Xcode will report \"No such module 'ResponslySDK'\".");
            }
        }

        // "2022.3.23f1" gives 2022, 3, 23.
        private static bool TryReadUnityVersion(string version, out int major, out int minor, out int patch)
        {
            major = 0;
            minor = 0;
            patch = 0;
            if (string.IsNullOrEmpty(version))
            {
                return false;
            }
            string[] parts = version.Split('.');
            if (parts.Length < 3)
            {
                return false;
            }
            int digits = 0;
            while (digits < parts[2].Length && char.IsDigit(parts[2][digits]))
            {
                digits++;
            }
            return int.TryParse(parts[0], out major)
                && int.TryParse(parts[1], out minor)
                && digits > 0
                && int.TryParse(parts[2].Substring(0, digits), out patch);
        }

        // The External Dependency Manager, whichever way it was installed.
        private static bool HasDependencyManager()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int index = 0; index < assemblies.Length; index++)
            {
                string name = assemblies[index].GetName().Name;
                if (name == "Google.JarResolver" || name.StartsWith("Google.JarResolver_", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
