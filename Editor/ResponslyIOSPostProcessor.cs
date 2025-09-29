// UnityEditor.iOS.Xcode only exists when the iOS build support module is installed, which is
// certain while iOS is the active build target.
#if UNITY_IOS
using System;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace Responsly.Editor
{
    /// <summary>
    /// Prepares the Xcode project Unity generates for the Responsly SDK:
    ///
    /// - Swift. Plugins/iOS/ResponslyUnityBridge.swift is compiled into UnityFramework, so that
    ///   target needs a Swift version and modules; the app target embeds the Swift runtime
    ///   libraries a deployment target may need, and UnityFramework itself must not.
    /// - iOS 15. ResponslySDK.xcframework is built for iOS 15, and a target with a lower
    ///   deployment target cannot import it.
    ///
    /// Nothing is embedded: ResponslySDK.xcframework is a static framework, which Unity links
    /// into UnityFramework like any other plug-in library.
    ///
    /// Every step only raises or fills in a setting; a failure is logged and never fails a build.
    /// </summary>
    internal static class ResponslyIOSPostProcessor
    {
        private const string MinimumIOSVersion = "15.0";

        [PostProcessBuild(900)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS)
            {
                return;
            }

            try
            {
                string projectPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
                PBXProject project = new PBXProject();
                project.ReadFromFile(projectPath);

                string appTarget = project.GetUnityMainTargetGuid();
                string frameworkTarget = project.GetUnityFrameworkTargetGuid();

                project.SetBuildProperty(appTarget, "ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES", "YES");
                project.SetBuildProperty(frameworkTarget, "ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES", "NO");
                project.SetBuildProperty(frameworkTarget, "CLANG_ENABLE_MODULES", "YES");
                if (string.IsNullOrEmpty(project.GetBuildPropertyForAnyConfig(frameworkTarget, "SWIFT_VERSION")))
                {
                    project.SetBuildProperty(frameworkTarget, "SWIFT_VERSION", "5.0");
                }

                bool raised = false;
                raised |= RaiseDeploymentTarget(project, project.ProjectGuid());
                raised |= RaiseDeploymentTarget(project, appTarget);
                raised |= RaiseDeploymentTarget(project, frameworkTarget);
                if (raised)
                {
                    Debug.LogWarning(
                        "[Responsly] The SDK needs iOS " + MinimumIOSVersion + " or later. The deployment target of the " +
                        "Xcode project was raised; set Project Settings > Player > iOS > Target minimum iOS Version to " +
                        MinimumIOSVersion + " to make it permanent.");
                }

                project.WriteToFile(projectPath);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[Responsly] Could not prepare the Xcode project (" + exception.Message + "). In Xcode, set the " +
                    "deployment target to iOS " + MinimumIOSVersion + " and, for the UnityFramework target, a Swift " +
                    "Language Version.");
            }
        }

        // True when the setting was below iOS 15 and has been raised.
        private static bool RaiseDeploymentTarget(PBXProject project, string guid)
        {
            string current = project.GetBuildPropertyForAnyConfig(guid, "IPHONEOS_DEPLOYMENT_TARGET");
            if (string.IsNullOrEmpty(current) || !IsBelowMinimum(current))
            {
                // Not set here (inherited from the project), not a version, or already high enough.
                return false;
            }
            project.SetBuildProperty(guid, "IPHONEOS_DEPLOYMENT_TARGET", MinimumIOSVersion);
            return true;
        }

        private static bool IsBelowMinimum(string version)
        {
            string text = version.Trim();
            if (text.IndexOf('.') < 0)
            {
                text += ".0";
            }
            System.Version parsed;
            if (!System.Version.TryParse(text, out parsed))
            {
                return false;
            }
            return parsed < new System.Version(MinimumIOSVersion);
        }
    }
}
#endif
