# Responsly SDK for Unity

Show [Responsly](https://responsly.com) surveys inside your iOS and Android game: after a level, on a menu screen, at launch, or whenever you ask for one. You choose in Responsly who sees a survey and when; the SDK downloads that configuration, decides on the device and shows the survey in a native bottom sheet, dialog or full-screen view above the game.

This package is a thin C# layer over the native Responsly iOS and Android SDKs, which are included. Behaviour is the same as in a native app.

Full documentation: **https://developers.responsly.com/mobile-sdk/**

## Requirements

| | |
|---|---|
| Unity | 2022.3 LTS or later. iOS builds need **2022.3.23f1** or later (the first 2022.3 release that imports `.xcframework` plug-ins). For Android builds use **2022.3.38f1** or later (see [Known limits](#known-limits)). |
| iOS | iOS 15 or later. Xcode 26.4 or later. |
| Android | Minimum API level 23, target API level 33 or higher, AndroidX. |
| Responsly | The team's SDK key (`rsk_…`) from the form's **Share → Mobile app** tab. |

In **Project Settings → Player** set **Target minimum iOS Version** to `15.0` and **Minimum API Level** to `23`.

## Install

1. Open **Window → Package Manager**.
2. Click **+** and choose **Add package from git URL…**
3. Enter the URL and click **Add**.

```
https://github.com/responsly/responsly-unity-sdk.git
```

Or add it to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.responsly.sdk": "https://github.com/responsly/responsly-unity-sdk.git"
  }
}
```

The package name is `com.responsly.sdk` and the namespace is `Responsly`.

### Android dependencies

`Plugins/Android/responsly-sdk.aar` contains the Responsly Android SDK, but not the three libraries the SDK depends on. They must be part of the Gradle build, or no survey can be shown:

```
androidx.webkit:webkit:1.15.0
androidx.annotation:annotation:1.9.1
org.jetbrains.kotlin:kotlin-stdlib:2.0.21
```

**With the External Dependency Manager for Unity (EDM4U).** Nothing to do. The package ships `Editor/ResponslyDependencies.xml`; EDM4U reads it and adds the libraries when it resolves (**Assets → External Dependency Manager → Android Resolver → Resolve**). Many SDKs bring EDM4U with them, so you may already have it.

**Without EDM4U.** Add the libraries to the Gradle templates yourself:

1. In **Project Settings → Player → Android → Publishing Settings** enable **Custom Main Gradle Template** and **Custom Gradle Properties Template**.
2. In `Assets/Plugins/Android/mainTemplate.gradle` add two lines to the `dependencies` block:

   ```groovy
   dependencies {
       implementation fileTree(dir: 'libs', include: ['*.jar'])
       implementation 'androidx.webkit:webkit:1.15.0'
       implementation 'org.jetbrains.kotlin:kotlin-stdlib:2.0.21'
   **DEPS**}
   ```

   (`androidx.annotation` comes with `androidx.webkit`.)
3. In `Assets/Plugins/Android/gradleTemplate.properties` make sure AndroidX is on:

   ```properties
   android.useAndroidX=true
   ```

The package warns in the Console before an Android build when it finds neither EDM4U nor these lines.

Nothing else is needed on Android: the only permission is `INTERNET`, the AAR brings its own manifest entries, and its R8 / ProGuard rules are applied automatically when you minify.

### iOS

Nothing to do. On every iOS build the package's post-process step (`Editor/ResponslyIOSPostProcessor.cs`) prepares the Xcode project that Unity generates:

- gives the `UnityFramework` target a Swift version and switches on modules (the package's bridge, `Plugins/iOS/ResponslyUnityBridge.swift`, is compiled into it);
- sets **Always Embed Swift Standard Libraries** to Yes on the app target and No on `UnityFramework`;
- raises the deployment target to iOS 15.0 if it is lower, and tells you to change it in the Player settings.

`Plugins/iOS/ResponslySDK.xcframework` is a **static** framework with slices for devices (`arm64`) and simulators (`arm64`, `x86_64`). Unity links it into `UnityFramework`; there is nothing to embed or sign, and no CocoaPods are involved. A static framework cannot carry its privacy manifest into the app, so the SDK's manifest ships next to it as a plug-in file, `Plugins/iOS/PrivacyInfo.xcprivacy` (the SDK stores its state in `UserDefaults`, reason `CA92.1`, and does no tracking). Unity 2022.3.18f1 and later combine the privacy manifests of plug-ins with their own.

## Quick start

Initialize once, before the first scene loads. A static bootstrap script needs no GameObject.

```csharp
using Responsly;
using UnityEngine;

public static class ResponslyBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Init()
    {
        ResponslySDK.Initialize("rsk_…", new ResponslyOptions
        {
            LogLevel = Debug.isDebugBuild ? ResponslyLogLevel.Debug : ResponslyLogLevel.Error,
        });

        // A survey is drawn above the game; the game does not stop by itself.
        ResponslySDK.SurveyShown += e => { Time.timeScale = 0; };
        ResponslySDK.SurveyCompleted += e => { Time.timeScale = 1; };
        ResponslySDK.SurveyDismissed += e => { Time.timeScale = 1; };
    }
}
```

Then tell the SDK who the player is and what happens in the game. Surveys with a matching trigger appear by themselves.

```csharp
using System.Collections.Generic;

ResponslySDK.Identify("user-123", new Dictionary<string, object> { { "plan", "pro" }, { "level", 12 }, { "beta", true } });
ResponslySDK.Track("level_completed", new Dictionary<string, object> { { "level", 12 }, { "stars", 3 } });
ResponslySDK.Screen("MainMenu");

// or show one yourself; Force skips audience, schedule and frequency while you test
ResponslySDK.ShowSurvey("AbCd1234", new ShowOptions { Force = true });
```

Do not let a survey interrupt gameplay: trigger it from calm moments, pause targeting during a level (`ResponslySDK.Pause()` / `Resume()`), or veto it with a display guard: `ResponslySDK.SetDisplayGuard(survey => !inBossFight);`.

## API

All calls are static methods of `Responsly.ResponslySDK`. Every call returns at once, is safe from any thread and is safe before `Initialize` (it is remembered and applied when the SDK starts; `ShowSurvey` before `Initialize` reports `not_initialized`).

| Call | What it does |
|---|---|
| `Initialize(sdkKey, options)` | Starts the SDK. `ResponslyOptions`: `LogLevel` (`ResponslyLogLevel.None`, `Error`, `Info`, `Debug`; default `Error`) and `ApiHost` (leave the default unless Responsly support gives you another host). Calling it again with the same key does nothing. |
| `Identify(userId, traits)` | Sets the user ID and merges the traits into the stored ones. |
| `SetAttributes(traits)` | Merges traits without changing the user ID. A `null` value removes a trait. |
| `Track(eventName, properties)` | Adds one to the event's count and evaluates event triggers. |
| `Screen(name)` | Sets the current screen and evaluates screen triggers. |
| `ShowSurvey(surveyId, options)` | Shows a survey by its Survey ID. `ShowOptions`: `Force`, `HiddenFields`, `Locale`, `Presentation` (`ResponslyPresentation.BottomSheet`, `Dialog`, `Fullscreen`). |
| `CloseSurvey()` | Closes the survey that is visible or waiting to appear. |
| `Pause()` / `Resume()` | Stops / resumes surveys started by triggers. `ShowSurvey` still works. |
| `SetLocale(tag)` | Overrides the language, for example `"pl"`. `null` returns to the device language. |
| `SetHiddenFields(fields)` | Hidden-field values sent with every survey. Replaces the previous map. |
| `SetTrackingEnabled(enabled)` | `false` switches the SDK off: no network, no targeting, nothing shown. Saved on the device. |
| `Reset()` | Forgets the user, traits, counters and cached configuration; closes a visible survey. Call it on logout. |
| `SetDisplayGuard(guard)` | A `Func<ResponslySurvey, bool>` asked on Unity's main thread before a survey becomes visible; `false` vetoes it. An exception counts as allow, and so does no answer within 2 seconds. Not asked for `Force`. `null` removes it. |
| `ResponslySDK.SdkVersion` | `"1.2.1"` |

Trait and property values are `string`, numbers (`int`, `long`, `float`, `double`, `decimal`), `bool` or dates. A `DateTime` or `DateTimeOffset` is sent as an ISO 8601 UTC string with seconds precision (`2026-11-10T12:00:00Z`); passing such a string yourself is the same thing. Anything else is sent as its text.

### Events

C# events on `ResponslySDK`, raised on Unity's main thread, so handlers can touch `Time`, GameObjects and UI.

| Event | Fires when | `e` has |
|---|---|---|
| `SurveyShown` | The survey became visible. | `Survey.Id`, `Survey.Name`, `Trigger.Type`, `Trigger.Name` |
| `SurveyNotShown` | A survey matched a trigger or was requested, but was not shown. | `SurveyId`, `Reason` |
| `QuestionAnswered` | The respondent answered a question. | `SurveyId`, `QuestionId`, `Type`, `Title`, `ValueJson` |
| `SurveyCompleted` | The respondent submitted the survey. | `SurveyId`, `ResponseId`, `Answers` |
| `SurveyDismissed` | The survey was closed before it was completed. | `SurveyId` |
| `SurveyError` | The survey reported an error while it was visible. | `SurveyId`, `Code`, `Message` |

```csharp
ResponslySDK.SurveyNotShown += e => Debug.Log($"Survey {e.SurveyId} not shown: {e.Reason}");
ResponslySDK.QuestionAnswered += e => Debug.Log($"answered {e.QuestionId} ({e.Type}): {e.ValueJson}");
```

An answer's value is JSON text in `ValueJson`: `9`, `"some text"`, `true`, `["a","b"]`, `{"row":"a"}` or `null`. The typed helpers on `QuestionAnsweredEventArgs` and on each item of `Answers` (`ResponslyAnswer`) read the simple cases:

| Helper | Returns |
|---|---|
| `AsString()` | the text when the value is a string, otherwise `null` |
| `AsDouble()` | a `double?`: the number when the value is a number, otherwise `null` |
| `AsBool()` | a `bool?`: the value when it is `true` or `false`, otherwise `null` |

`Trigger.Type` is `event`, `screen`, `app_launch` or `manual`; `Trigger.Name` is empty for the last two. `Reason` is one of the not-shown reasons listed in the [API reference](https://developers.responsly.com/mobile-sdk/api-reference/#not-shown-reasons).

The event argument types are `SurveyShownEventArgs`, `SurveyNotShownEventArgs`, `QuestionAnsweredEventArgs`, `SurveyCompletedEventArgs`, `SurveyDismissedEventArgs` and `SurveyErrorEventArgs`. Subscribe with a named method if you need to unsubscribe with `-=` later.

## Testing

### In the Editor

The SDK does not run in the Editor, or on any platform other than iOS and Android: every call does nothing and no event is ever raised. With `LogLevel = ResponslyLogLevel.Debug` each call writes one line to the Console, for example

```
[Responsly] Track("level_completed", {"level":12}) - no-op: the SDK only runs in iOS and Android players.
```

so you can follow your integration in Play mode and leave the SDK calls in place without platform checks.

### On a device

Build and run on an iOS or Android device, simulator or emulator. With the `Debug` log level the native SDK logs every decision, prefixed `[Responsly]`: read it in the Xcode console on iOS and with `adb logcat -s Responsly` on Android (not in the Unity Console). `SurveyNotShown` gives you the same reasons in code.

### The demo

**Package Manager → Responsly SDK → Samples → Demo → Import**, then add the `ResponslyDemo` component to an empty GameObject in an empty scene. It draws a panel with fields for the SDK key and the API host and buttons for Initialize, Identify, Track, Screen, Show survey (with Force), Pause / Resume, Close and Reset, logs every event on screen and stops the game clock while a survey is shown.

## Known limits

- **Unity patch versions.** `.xcframework` plug-ins are imported from Unity 2022.3.23f1; on earlier 2022.3 releases the iOS build fails with `No such module 'ResponslySDK'`. Unity builds Android with Android Gradle Plugin 7.4.2 from 2022.3.38f1; earlier 2022.3 releases use 7.1.2, while `androidx.webkit` 1.15.0 declares 7.2 as its minimum, so the Gradle build may stop there. The package warns before a build in both cases.
- **Events wait for the next frame.** The native SDKs report on the platform's UI thread; the package queues events and raises them from `Update` of a hidden `DontDestroyOnLoad` object, created by the first SDK call. While the Unity player is not running (app in the background, another activity on top) events wait, and the display guard cannot answer: after 2 seconds the survey is allowed.
- **Calls from background threads** are also queued and reach the native SDK on the next frame.
- **Dates** in traits are text (ISO 8601 UTC). They compare correctly with the date conditions set in Responsly.
- **The Editor shows nothing.** There is no preview of a survey in Play mode.
- **tvOS, visionOS, WebGL and desktop** are not supported; the calls are no-ops there.
- **Simulator builds** work on Apple silicon and Intel Macs (the framework has both simulator slices).

## What is in the package

```
Runtime/                      C# API (assembly Responsly.Runtime, namespace Responsly)
Editor/                       iOS post-process step, pre-build checks, ResponslyDependencies.xml
Plugins/Android/              responsly-sdk.aar: the Android SDK and the Unity bridge
Plugins/iOS/                  ResponslySDK.xcframework, ResponslyUnityBridge.swift, PrivacyInfo.xcprivacy
Samples~/Demo/                the demo panel
Native~/                      source of the Android bridge, bridge tests, and what the binaries were built from
scripts/                      build-native.sh, verify-package.sh
```

Data crosses the native boundary as JSON text. On Android, C# calls `com.responsly.unity.ResponslyUnityBridge` through `AndroidJavaClass` and receives events through one `AndroidJavaProxy`. On iOS, C# calls `responsly_unity_*` C functions (`DllImport("__Internal")`) and receives events through one C callback.

### Rebuilding the native libraries

The binaries in `Plugins/` are built from the native SDK repositories, checked out next to this one (`../responsly-ios-sdk`, `../responsly-android-sdk`):

```sh
scripts/build-native.sh        # both; or: scripts/build-native.sh android | ios
scripts/verify-package.sh      # checks metas and JSON, compares the bridges with the C# declarations, runs the bridge tests
```

`build-native.sh` exports the committed state of each repository into a scratch directory, builds there (it never writes into the native repositories), and records the commits in `Native~/android/build-info.json` and `Native~/ios/build-info.json`. It needs Xcode, a JDK 17 and the Android SDK.

## Links

- Documentation: https://developers.responsly.com/mobile-sdk/
- Unity installation: https://developers.responsly.com/mobile-sdk/installation/unity/
- API reference: https://developers.responsly.com/mobile-sdk/api-reference/
- Testing and debugging: https://developers.responsly.com/mobile-sdk/testing/

## License

MIT. See [LICENSE.md](LICENSE.md).
