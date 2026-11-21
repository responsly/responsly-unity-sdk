# Changelog

All notable changes to this SDK are documented here.

## [1.2.3] - 2026-11-21

### Security

- Rebuild the bundled native libraries from the iOS and Android SDKs 1.2.3: API requests no longer follow redirects, survey links open outside the app only after a tap and only for `http`, `https`, `mailto`, `tel` and `sms`, `apiHost` and survey addresses must use https (plain http only for local development hosts), and debug logs no longer print answers.

### Changed

- `scripts/verify-metadata.py` checks the package metadata, the metas and the SHA-256 of the native binaries without platform build tools; `scripts/build-native.sh --include-uncommitted` builds from a snapshot of local changes and records it.

## [1.2.2] - 2026-11-12

### Fixed

- Register the iOS bridge listener once when callbacks are configured concurrently, preventing duplicate survey events.

### Changed

- Update the bundled iOS and Android SDKs to 1.2.2.

## [1.2.1] - 2026-08-31

- Refresh package and integration metadata.

## [1.2.0] - 2026-05-25

- Record the native SDK commits.

## [1.1.1] - 2026-03-20

- README: shorter note on the External Dependency Manager.

## [1.1.0] - 2026-01-30

- Synchronize native SDK sources and packages.

## [1.0.0] - 2025-12-19

- First stable release.

- First stable SDK release with installation guides.

## [0.9.5] - 2025-12-16

- Verify package metadata and native bridge contracts.

## [0.9.4] - 2025-12-06

- Build native libraries from pinned SDK commits.

## [0.9.3] - 2025-11-26

- Provide a survey integration demo.

## [0.9.2] - 2025-11-22

- Verify Swift bridge events and display decisions.

## [0.9.1] - 2025-11-18

- Verify Android bridge behavior on the JVM.

## [0.9.0] - 2025-11-08

- Include the native iOS privacy manifest.

## [0.8.7] - 2025-11-05

- Ship the iOS simulator framework and build metadata.

## [0.8.6] - 2025-10-31

- Ship the iOS device framework.

## [0.8.5] - 2025-10-25

- Define the bundled iOS XCFramework.

## [0.8.4] - 2025-10-19

- Bundle the Android SDK and Unity bridge.

## [0.8.3] - 2025-10-09

- Validate Unity and mobile build requirements.

## [0.8.2] - 2025-09-29

- Configure generated iOS Xcode projects.

## [0.8.1] - 2025-09-20

- Declare Android dependencies for Unity.

## [0.8.0] - 2025-09-15

- Export the Swift bridge for Unity.

## [0.7.7] - 2025-09-06

- Connect the C# facade to iOS.

## [0.7.6] - 2025-08-29

- Implement Android native calls and display guards.

## [0.7.5] - 2025-08-20

- Serialize Android bridge event payloads.

## [0.7.4] - 2025-08-13

- Define Android callback delivery.

## [0.7.3] - 2025-08-03

- Connect the C# facade to Android.

## [0.7.2] - 2025-07-26

- Support calls in the Editor and unsupported platforms.

## [0.7.1] - 2025-07-17

- Dispatch callbacks on the Unity main thread.

## [0.7.0] - 2025-07-10

- Expose the public Unity survey API.

## [0.6.7] - 2025-07-05

- Encode options and traits for native bridges.

## [0.6.6] - 2025-06-30

- Implement JSON serialization for SDK values.

## [0.6.5] - 2025-06-21

- Define the native platform contract.

## [0.6.4] - 2025-06-13

- Model survey events and answer values.

## [0.6.3] - 2025-06-04

- Add survey initialization and display options.

## [0.6.2] - 2025-05-31

- Define the editor assembly.

## [0.6.1] - 2025-05-26

- Define the runtime assembly.

## [0.6.0] - 2025-05-20

- Establish the Unity package structure.
