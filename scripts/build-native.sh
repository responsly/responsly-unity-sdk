#!/usr/bin/env bash
#
# Rebuilds the native libraries this package ships from the native SDK repositories:
#
#   Plugins/Android/responsly-sdk.aar        the Android SDK plus the Unity bridge (Native~/android)
#   Plugins/iOS/ResponslySDK.xcframework     the iOS SDK as a static framework, device + simulator
#   Plugins/iOS/PrivacyInfo.xcprivacy        the iOS SDK's privacy manifest
#
# and records what they were built from in Native~/android/build-info.json and
# Native~/ios/build-info.json.
#
# Usage: scripts/build-native.sh [android] [ios] [--allow-dirty|--include-uncommitted] [--keep-build]
#
# The native repositories are only read. By default the recorded commit is exported into a
# scratch directory and everything is built there.
# A repository with uncommitted changes is refused unless --allow-dirty is given (the uncommitted
# changes are then NOT part of the build). --include-uncommitted explicitly builds a snapshot of
# git-listed working files instead, and records its source SHA-256 and base commit in build-info.
#
# The scratch directory (exported sources, Gradle output, Xcode archives and derived data, a few
# hundred MB) is deleted as soon as the binaries are in Plugins/, and on failure, unless
# --keep-build is given. Every heavy step first checks the free disk space and stops when less
# than RESPONSLY_MIN_FREE_GB is left.
#
# Environment:
#   RESPONSLY_ANDROID_SDK   path of the Android SDK repository (default ../responsly-android-sdk)
#   RESPONSLY_IOS_SDK       path of the iOS SDK repository     (default ../responsly-ios-sdk)
#   RESPONSLY_BUILD_DIR     parent for a fresh scratch directory (default $TMPDIR)
#   RESPONSLY_MIN_FREE_GB   free space a heavy step needs      (default 6)
#   JAVA_HOME               a JDK 17 (default: the Homebrew openjdk@17, when installed)
#   ANDROID_HOME            the Android SDK (default ~/Library/Android/sdk)
#
# Needs: git, Xcode (xcodebuild, plutil, lipo), a JDK 17, the Android SDK.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ANDROID_REPO="${RESPONSLY_ANDROID_SDK:-$ROOT/../responsly-android-sdk}"
IOS_REPO="${RESPONSLY_IOS_SDK:-$ROOT/../responsly-ios-sdk}"
MIN_FREE_GB="${RESPONSLY_MIN_FREE_GB:-6}"
WORK=""

BUILD_ANDROID=0
BUILD_IOS=0
ALLOW_DIRTY=0
INCLUDE_UNCOMMITTED=0
SOURCE_KIND="commit"
KEEP_BUILD=0
for argument in "$@"; do
    case "$argument" in
        android) BUILD_ANDROID=1 ;;
        ios) BUILD_IOS=1 ;;
        --allow-dirty) ALLOW_DIRTY=1 ;;
        --include-uncommitted) ALLOW_DIRTY=1; INCLUDE_UNCOMMITTED=1; SOURCE_KIND="working-tree" ;;
        --keep-build) KEEP_BUILD=1 ;;
        -h|--help) sed -n '2,32p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
        *) echo "error: unknown argument '$argument' (expected: android, ios, --allow-dirty, --include-uncommitted, --keep-build)" >&2; exit 2 ;;
    esac
done
if [ "$BUILD_ANDROID" = 0 ] && [ "$BUILD_IOS" = 0 ]; then
    BUILD_ANDROID=1
    BUILD_IOS=1
fi

log() { printf '\n==> %s\n' "$*"; }
fail() { echo "error: $*" >&2; exit 1; }

# Prints the commit a repository is at, after checking that it can be built from.
repository_commit() {
    local repository="$1"
    [ -d "$repository/.git" ] || [ -f "$repository/.git" ] || fail "$repository is not a git repository"
    local changes
    changes="$(git -C "$repository" status --porcelain)"
    if [ -n "$changes" ]; then
        if [ "$INCLUDE_UNCOMMITTED" = 1 ]; then
            echo "warning: $repository has uncommitted changes; a working-tree snapshot will be recorded" >&2
        elif [ "$ALLOW_DIRTY" = 1 ]; then
            echo "warning: $repository has uncommitted changes; they are NOT part of this build" >&2
        else
            echo "$changes" >&2
            fail "$repository has uncommitted changes. Commit them first, or pass --allow-dirty to build HEAD without them."
        fi
    fi
    git -C "$repository" rev-parse HEAD
}

# Exports the exact commit, or an explicitly requested working snapshot, and prints its digest.
export_commit() {
    local repository="$1" commit="$2" destination="$3"
    if [ "$INCLUDE_UNCOMMITTED" = 1 ]; then
        python3 "$ROOT/scripts/snapshot-native.py" "$repository" "$commit" "$destination" --working-tree
    else
        python3 "$ROOT/scripts/snapshot-native.py" "$repository" "$commit" "$destination"
    fi
}

sha256() { shasum -a 256 "$1" | cut -d ' ' -f 1; }

# Stops before a heavy step when the disk that holds the scratch directory is nearly full.
require_free_space() {
    local step="$1" free_kb
    free_kb="$(df -k "$WORK" | awk 'NR == 2 { print $4 }')"
    echo "    free disk space before $step: $((free_kb / 1024 / 1024)) GB (at least $MIN_FREE_GB GB needed)"
    if [ "$free_kb" -lt $((MIN_FREE_GB * 1024 * 1024)) ]; then
        fail "less than $MIN_FREE_GB GB of free disk space; $step was not started"
    fi
}

# Deletes build output that is no longer needed.
discard() {
    if [ "$KEEP_BUILD" = 0 ]; then
        rm -rf "$@"
    fi
}

# ---------------------------------------------------------------------------------------------
# Android: the SDK's :sdk module with the Unity bridge sources added, as one AAR.
# ---------------------------------------------------------------------------------------------

# Reads `name = "1.2.3"` from the SDK's version catalog.
catalog_version() {
    sed -n "s/^$2 = \"\(.*\)\"/\1/p" "$1/gradle/libs.versions.toml" | head -1
}

# Reads `name = "1.2.3"` from the SDK module's build file.
build_file_version() {
    sed -n "s/.*$2 = \"\(.*\)\".*/\1/p" "$1/sdk/build.gradle.kts" | head -1
}

build_android() {
    local commit source source_hash aar classes class version
    commit="$(repository_commit "$ANDROID_REPO")"
    source="$WORK/responsly-android-sdk"

    log "Android: exporting $(basename "$ANDROID_REPO") at $commit"
    source_hash="$(export_commit "$ANDROID_REPO" "$commit" "$source")"

    # The bridge is compiled together with the SDK, so a signature change in the SDK fails here
    # and not in somebody's game.
    mkdir -p "$source/sdk/src/main/java"
    cp -R "$ROOT/Native~/android/java/." "$source/sdk/src/main/java/"
    cat "$ROOT/Native~/android/consumer-rules.pro" >> "$source/sdk/consumer-rules.pro"

    if [ -z "${JAVA_HOME:-}" ] && [ -d /opt/homebrew/opt/openjdk@17/libexec/openjdk.jdk/Contents/Home ]; then
        export JAVA_HOME=/opt/homebrew/opt/openjdk@17/libexec/openjdk.jdk/Contents/Home
    fi
    export ANDROID_HOME="${ANDROID_HOME:-$HOME/Library/Android/sdk}"
    [ -d "$ANDROID_HOME" ] || fail "the Android SDK was not found at $ANDROID_HOME (set ANDROID_HOME)"

    log "Android: ./gradlew :sdk:assembleRelease"
    require_free_space "the Gradle build"
    # --no-daemon: a build process of our own that exits when it is done; other Gradle daemons
    # on this machine are left alone.
    (cd "$source" && ./gradlew --no-daemon --console=plain :sdk:assembleRelease)

    aar="$source/sdk/build/outputs/aar/sdk-release.aar"
    [ -f "$aar" ] || fail "the build did not produce $aar"

    classes="$WORK/android-classes.jar"
    unzip -p "$aar" classes.jar > "$classes"
    for class in com/responsly/sdk/Responsly.class com/responsly/unity/ResponslyUnityBridge.class com/responsly/unity/ResponslyUnityCallback.class; do
        unzip -l "$classes" "$class" > /dev/null || fail "$class is missing from the AAR"
    done
    unzip -p "$aar" proguard.txt | grep -q 'com.responsly.unity.ResponslyUnityBridge' \
        || fail "the AAR's consumer rules do not keep the Unity bridge"

    mkdir -p "$ROOT/Plugins/Android"
    cp "$aar" "$ROOT/Plugins/Android/responsly-sdk.aar"

    version="$(sed -n 's/.*const val SDK_VERSION: String = "\(.*\)".*/\1/p' "$source/sdk/src/main/kotlin/com/responsly/sdk/Responsly.kt" | head -1)"
    cat > "$ROOT/Native~/android/build-info.json" <<EOF
{
  "artifact": "Plugins/Android/responsly-sdk.aar",
  "sha256": "$(sha256 "$ROOT/Plugins/Android/responsly-sdk.aar")",
  "sdk": "responsly-android-sdk",
  "sdkVersion": "$version",
  "sdkCommit": "$commit",
  "sdkSource": "$SOURCE_KIND",
  "sdkSourceSha256": "$source_hash",
  "contents": "com.responsly.sdk (the SDK) and com.responsly.unity (the Unity bridge, Native~/android/java)",
  "dependencies": [
    "org.jetbrains.kotlin:kotlin-stdlib:$(build_file_version "$source" coreLibrariesVersion)",
    "androidx.webkit:webkit:$(catalog_version "$source" androidx-webkit)",
    "androidx.annotation:annotation:$(catalog_version "$source" androidx-annotation)"
  ]
}
EOF
    discard "$source" "$classes"
}

# ---------------------------------------------------------------------------------------------
# iOS: the Swift package as a static framework for devices and simulators, as one XCFramework.
# ---------------------------------------------------------------------------------------------
IOS_SOURCE=""
IOS_VERSION=""

build_ios_slice() {
    local destination="$1" sdk="$2"
    local archive="$WORK/ios/$sdk.xcarchive"
    local derived="$WORK/ios/DerivedData-$sdk"
    local output="$WORK/ios/xcodebuild-$sdk.log"

    log "iOS: xcodebuild archive ($destination)"
    require_free_space "xcodebuild archive"
    if ! (cd "$IOS_SOURCE" && xcodebuild archive \
            -scheme ResponslySDK \
            -configuration Release \
            -destination "$destination" \
            -archivePath "$archive" \
            -derivedDataPath "$derived" \
            SKIP_INSTALL=NO \
            BUILD_LIBRARY_FOR_DISTRIBUTION=YES \
            MACH_O_TYPE=staticlib \
            CODE_SIGNING_ALLOWED=NO) > "$output" 2>&1; then
        tail -40 "$output" >&2
        fail "xcodebuild archive failed ($destination)"
    fi

    local framework="$archive/Products/usr/local/lib/ResponslySDK.framework"
    local modules="$derived/Build/Intermediates.noindex/ArchiveIntermediates/ResponslySDK/BuildProductsPath/Release-$sdk/ResponslySDK.swiftmodule"
    [ -f "$framework/ResponslySDK" ] || fail "xcodebuild did not produce $framework"
    [ -d "$modules" ] || fail "the Swift module was not found at $modules"

    # xcodebuild leaves the Swift module of a package product outside the framework. Only the
    # textual interfaces are shipped: they are what other compiler versions can read.
    mkdir -p "$framework/Modules/ResponslySDK.swiftmodule"
    local file copied=0
    for file in "$modules"/*.swiftinterface "$modules"/*.swiftdoc; do
        case "$file" in
            *.package.swiftinterface) continue ;;
        esac
        cp "$file" "$framework/Modules/ResponslySDK.swiftmodule/"
        copied=$((copied + 1))
    done
    [ "$copied" -gt 0 ] || fail "no .swiftinterface files in $modules"

    rm -rf "$framework/_CodeSignature"
    plutil -replace CFBundleIdentifier -string "com.responsly.ResponslySDK" "$framework/Info.plist"
    plutil -replace CFBundleShortVersionString -string "$IOS_VERSION" "$framework/Info.plist"

    # Only the framework is needed from here on.
    discard "$derived"
}

# One JSON line per slice: its folder, architectures and binary checksum.
xcframework_slices() {
    local xcframework="$1" slice first=1
    for slice in "$xcframework"/ios-*; do
        [ "$first" = 1 ] || printf ',\n'
        first=0
        printf '    { "id": "%s", "architectures": "%s", "sha256": "%s" }' \
            "$(basename "$slice")" \
            "$(lipo -archs "$slice/ResponslySDK.framework/ResponslySDK")" \
            "$(sha256 "$slice/ResponslySDK.framework/ResponslySDK")"
    done
    printf '\n'
}

build_ios() {
    local commit source_hash xcode swift
    commit="$(repository_commit "$IOS_REPO")"
    # The directory name becomes the package's name in the build.
    IOS_SOURCE="$WORK/responsly-ios-sdk"

    log "iOS: exporting $(basename "$IOS_REPO") at $commit"
    source_hash="$(export_commit "$IOS_REPO" "$commit" "$IOS_SOURCE")"
    rm -rf "$WORK/ios"
    mkdir -p "$WORK/ios"

    IOS_VERSION="$(sed -n 's/.*static let sdkVersion: String = "\(.*\)".*/\1/p' "$IOS_SOURCE/Sources/ResponslySDK/Responsly.swift" | head -1)"
    [ -n "$IOS_VERSION" ] || fail "could not read Responsly.sdkVersion"

    # xcodebuild only wraps a package product in a .framework when the product is a dynamic
    # library; MACH_O_TYPE=staticlib then makes the framework's binary a static archive.
    sed -i '' 's/\.library(name: "ResponslySDK", targets:/.library(name: "ResponslySDK", type: .dynamic, targets:/' "$IOS_SOURCE/Package.swift"
    grep -q 'type: \.dynamic' "$IOS_SOURCE/Package.swift" \
        || fail "Package.swift has changed: could not turn the ResponslySDK product into a framework"

    build_ios_slice "generic/platform=iOS" iphoneos
    build_ios_slice "generic/platform=iOS Simulator" iphonesimulator

    log "iOS: xcodebuild -create-xcframework"
    require_free_space "xcodebuild -create-xcframework"
    xcodebuild -create-xcframework \
        -framework "$WORK/ios/iphoneos.xcarchive/Products/usr/local/lib/ResponslySDK.framework" \
        -framework "$WORK/ios/iphonesimulator.xcarchive/Products/usr/local/lib/ResponslySDK.framework" \
        -output "$WORK/ios/ResponslySDK.xcframework"

    mkdir -p "$ROOT/Plugins/iOS" "$ROOT/Native~/ios"
    rm -rf "$ROOT/Plugins/iOS/ResponslySDK.xcframework"
    cp -R "$WORK/ios/ResponslySDK.xcframework" "$ROOT/Plugins/iOS/ResponslySDK.xcframework"
    # A static framework is linked, not embedded, so its privacy manifest travels as a plug-in
    # file of its own; Unity merges it into the app's manifest.
    cp "$IOS_SOURCE/Sources/ResponslySDK/Resources/PrivacyInfo.xcprivacy" "$ROOT/Plugins/iOS/PrivacyInfo.xcprivacy"

    xcode="$(xcodebuild -version | tr '\n' ' ' | sed 's/ *$//')"
    swift="$(xcrun swiftc --version 2>/dev/null | sed -n 's/.*Apple Swift version \([0-9.]*\).*/\1/p' | head -1)"
    cat > "$ROOT/Native~/ios/build-info.json" <<EOF
{
  "artifact": "Plugins/iOS/ResponslySDK.xcframework",
  "linkage": "static framework",
  "slices": [
$(xcframework_slices "$ROOT/Plugins/iOS/ResponslySDK.xcframework")
  ],
  "sdk": "responsly-ios-sdk",
  "sdkVersion": "$IOS_VERSION",
  "sdkCommit": "$commit",
  "sdkSource": "$SOURCE_KIND",
  "sdkSourceSha256": "$source_hash",
  "minimumIOS": "$(plutil -extract MinimumOSVersion raw "$ROOT/Plugins/iOS/ResponslySDK.xcframework/ios-arm64/ResponslySDK.framework/Info.plist")",
  "builtWith": "$xcode, Swift $swift"
}
EOF
    discard "$IOS_SOURCE" "$WORK/ios"
}

# ---------------------------------------------------------------------------------------------

# Refuse before building anything, so a dirty repository never leaves half of the binaries new.
[ "$BUILD_ANDROID" = 0 ] || repository_commit "$ANDROID_REPO" > /dev/null
[ "$BUILD_IOS" = 0 ] || repository_commit "$IOS_REPO" > /dev/null

if [ -n "${RESPONSLY_BUILD_DIR:-}" ]; then
    mkdir -p "$RESPONSLY_BUILD_DIR"
    RESPONSLY_BUILD_PARENT="$(cd "$RESPONSLY_BUILD_DIR" && pwd)"
    WORK="$(mktemp -d "$RESPONSLY_BUILD_PARENT/responsly-unity-native.XXXXXX")"
else
    WORK="$(mktemp -d "${TMPDIR:-/tmp}/responsly-unity-native.XXXXXX")"
fi
# Also when a step fails: nothing of a build stays on the disk.
trap 'discard "$WORK"' EXIT

[ "$BUILD_ANDROID" = 0 ] || build_android
[ "$BUILD_IOS" = 0 ] || build_ios

log "Done"
if [ "$BUILD_ANDROID" = 1 ]; then
    printf '%s  %s bytes\n' "Plugins/Android/responsly-sdk.aar" "$(stat -f %z "$ROOT/Plugins/Android/responsly-sdk.aar")"
    cat "$ROOT/Native~/android/build-info.json"
fi
if [ "$BUILD_IOS" = 1 ]; then
    printf '%s  %s KB\n' "Plugins/iOS/ResponslySDK.xcframework" "$(du -sk "$ROOT/Plugins/iOS/ResponslySDK.xcframework" | cut -f 1)"
    cat "$ROOT/Native~/ios/build-info.json"
fi
