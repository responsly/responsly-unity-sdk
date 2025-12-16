#!/usr/bin/env bash
#
# Checks the package:
#
#   1. package.json, the assembly definitions and the build-info files are valid JSON, and the
#      version numbers agree.
#   2. Every file and folder Unity imports has a .meta, no .meta is orphaned, GUIDs are unique.
#   3. iOS: Plugins/iOS/ResponslyUnityBridge.swift type-checks against every slice of
#      ResponslySDK.xcframework, links with it into a dynamic library (as it does inside
#      UnityFramework), and the C symbols it exports are exactly the DllImport entry points of
#      Runtime/Internal/ResponslyIOSPlatform.cs, with the same number of parameters.
#   4. iOS: runs Native~/ios/test (the bridge, compiled for macOS together with the iOS SDK's
#      sources at the commit the framework was built from) when that repository is next to this
#      one: the JSON of every message, the display guard, and a call through the SDK and back.
#   5. Android: the bridge classes inside Plugins/Android/responsly-sdk.aar have exactly the
#      methods, with exactly the parameter types, that Runtime/Internal/ResponslyAndroidPlatform.cs
#      calls; the AAR keeps them when the app is minified; Editor/ResponslyDependencies.xml lists
#      the libraries the AAR was built against.
#   6. Android: runs Native~/android/test (the bridge classes from the AAR against the SDK's real
#      event classes, on the JVM) when the kotlin-stdlib and org.json jars are in the Gradle cache.
#
# Usage: scripts/verify-package.sh
# Needs: python3, Xcode, a JDK (JAVA_HOME, or the Homebrew openjdk@17).
# Environment: RESPONSLY_IOS_SDK, the iOS SDK repository for step 4 (default ../responsly-ios-sdk).
# Everything is compiled in a temporary directory that is deleted at the end (about 15 MB).

set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/responsly-unity-verify.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

FAILURES=0
pass() { printf 'PASS  %s\n' "$*"; }
fail() { printf 'FAIL  %s\n' "$*"; FAILURES=$((FAILURES + 1)); }
skip() { printf 'SKIP  %s\n' "$*"; }
section() { printf '\n== %s\n' "$*"; }

if [ -z "${JAVA_HOME:-}" ] && [ -d /opt/homebrew/opt/openjdk@17/libexec/openjdk.jdk/Contents/Home ]; then
    export JAVA_HOME=/opt/homebrew/opt/openjdk@17/libexec/openjdk.jdk/Contents/Home
fi
JAVAP="${JAVA_HOME:+$JAVA_HOME/bin/}javap"
JAVAC="${JAVA_HOME:+$JAVA_HOME/bin/}javac"
JAVA="${JAVA_HOME:+$JAVA_HOME/bin/}java"

AAR="Plugins/Android/responsly-sdk.aar"
XCFRAMEWORK="Plugins/iOS/ResponslySDK.xcframework"
SWIFT_BRIDGE="Plugins/iOS/ResponslyUnityBridge.swift"
CS_IOS="Runtime/Internal/ResponslyIOSPlatform.cs"
CS_ANDROID="Runtime/Internal/ResponslyAndroidPlatform.cs"

# ---------------------------------------------------------------------------------------------
section "1. JSON and versions"
# ---------------------------------------------------------------------------------------------
if python3 - <<'PY'
import json, re, sys

ok = True
def check(condition, message):
    global ok
    print(("PASS  " if condition else "FAIL  ") + message)
    ok = ok and condition

documents = {}
for path in ["package.json", "Runtime/Responsly.Runtime.asmdef", "Editor/Responsly.Editor.asmdef",
             "Native~/android/build-info.json", "Native~/ios/build-info.json"]:
    try:
        documents[path] = json.load(open(path))
        check(True, path + " is valid JSON")
    except Exception as error:
        check(False, path + ": " + str(error))

package = documents.get("package.json", {})
check(package.get("name") == "com.responsly.sdk", "package name is com.responsly.sdk")
check(package.get("displayName") == "Responsly SDK", "displayName is Responsly SDK")
check(package.get("unity") == "2022.3", "unity is 2022.3")
check(package.get("license") == "MIT", "license is MIT")
version = package.get("version")
constant = re.search(r'public const string SdkVersion = "([^"]+)"', open("Runtime/ResponslySDK.cs").read())
check(constant is not None and constant.group(1) == version, "ResponslySDK.SdkVersion equals the package version (%s)" % version)
for path in ["Native~/android/build-info.json", "Native~/ios/build-info.json"]:
    info = documents.get(path, {})
    check(info.get("sdkVersion") == version, "%s: native SDK %s at %s" % (path, info.get("sdkVersion"), str(info.get("sdkCommit"))[:7]))
check(documents.get("Runtime/Responsly.Runtime.asmdef", {}).get("name") == "Responsly.Runtime", "runtime assembly is Responsly.Runtime")
check(documents.get("Editor/Responsly.Editor.asmdef", {}).get("includePlatforms") == ["Editor"], "editor assembly is Editor-only")
for sample in package.get("samples", []):
    import os
    check(os.path.isdir(sample["path"]), "sample path exists: " + sample["path"])
sys.exit(0 if ok else 1)
PY
then :; else FAILURES=$((FAILURES + 1)); fi

# ---------------------------------------------------------------------------------------------
section "2. Meta files"
# ---------------------------------------------------------------------------------------------
if python3 - <<'PY'
import os, re, sys

# What Unity imports: everything except hidden entries (leading dot), folders that end with ~,
# and the inside of a folder it treats as one asset.
OPAQUE = (".xcframework", ".framework", ".bundle")
assets, metas = [], []
for current, dirs, files in os.walk("."):
    rel = os.path.relpath(current, ".")
    keep = []
    for d in sorted(dirs):
        if d.startswith(".") or d.endswith("~"):
            continue
        assets.append(os.path.normpath(os.path.join(rel, d)))
        if not d.endswith(OPAQUE):
            keep.append(d)
    dirs[:] = keep
    for f in sorted(files):
        if f.startswith("."):
            continue
        path = os.path.normpath(os.path.join(rel, f))
        (metas if f.endswith(".meta") else assets).append(path)

# A sample is copied into the project with its metas, so they are checked too.
for current, dirs, files in os.walk("Samples~"):
    for name in sorted(dirs) + sorted(files):
        path = os.path.join(current, name)
        (metas if name.endswith(".meta") else assets).append(path)

ok = True
missing = [a for a in assets if a + ".meta" not in metas]
orphans = [m for m in metas if m[:-5] not in assets]
for path in missing:
    print("FAIL  no .meta for " + path); ok = False
for path in orphans:
    print("FAIL  .meta without a file or folder: " + path); ok = False

guids = {}
for meta in metas:
    text = open(meta).read()
    match = re.search(r"^guid: ([0-9a-f]{32})$", text, re.M)
    if not text.startswith("fileFormatVersion: 2\n") or not match:
        print("FAIL  malformed .meta: " + meta); ok = False
        continue
    guids.setdefault(match.group(1), []).append(meta)
    is_dir = os.path.isdir(meta[:-5])
    if is_dir != ("folderAsset: yes" in text):
        print("FAIL  folderAsset does not match: " + meta); ok = False
for guid, paths in guids.items():
    if len(paths) > 1:
        print("FAIL  GUID %s is used by %s" % (guid, ", ".join(paths))); ok = False

def platforms(meta):
    text = open(meta).read()
    return [name for name, key in (("Android", "Android: Android"), ("iOS", "iPhone: iOS"), ("Editor", "Editor: Editor"), ("Any", "Any: "))
            if re.search(re.escape(key) + r"\s*\n\s+second:\n\s+enabled: 1", text)]

expected = {
    "Plugins/Android/responsly-sdk.aar.meta": ["Android"],
    "Plugins/iOS/ResponslySDK.xcframework.meta": ["iOS"],
    "Plugins/iOS/ResponslyUnityBridge.swift.meta": ["iOS"],
    "Plugins/iOS/PrivacyInfo.xcprivacy.meta": ["iOS"],
}
for meta, wanted in expected.items():
    found = platforms(meta) if os.path.exists(meta) else None
    if found != wanted:
        print("FAIL  %s enables %s, expected %s" % (meta, found, wanted)); ok = False
    else:
        print("PASS  %s: enabled for %s only" % (meta, ", ".join(wanted)))

scripts = [a for a in assets if a.endswith(".cs")]
print(("PASS  " if ok else "FAIL  ") + "%d assets (%d .cs files, %d folders), %d metas, %d unique GUIDs"
      % (len(assets), len(scripts), len([a for a in assets if os.path.isdir(a)]), len(metas), len(guids)))
sys.exit(0 if ok else 1)
PY
then :; else FAILURES=$((FAILURES + 1)); fi

# ---------------------------------------------------------------------------------------------
section "3. iOS bridge"
# ---------------------------------------------------------------------------------------------
if [ ! -d "$XCFRAMEWORK" ]; then
    fail "$XCFRAMEWORK is missing (run scripts/build-native.sh)"
elif ! xcrun --find swiftc > /dev/null 2>&1; then
    skip "no Swift compiler (Xcode) on this machine"
else
    for slice in "$XCFRAMEWORK"/ios-*; do
        binary="$slice/ResponslySDK.framework/ResponslySDK"
        kind="$(file -b "$binary" | head -1)"
        case "$kind" in
            *"ar archive"*) pass "$(basename "$slice"): static, architectures: $(lipo -archs "$binary"), minimum iOS $(plutil -extract MinimumOSVersion raw "$slice/ResponslySDK.framework/Info.plist")" ;;
            *) fail "$(basename "$slice"): expected a static archive, found: $kind" ;;
        esac
    done

    # slice folder : SDK : target triple
    for entry in \
        "ios-arm64:iphoneos:arm64-apple-ios15.0" \
        "ios-arm64_x86_64-simulator:iphonesimulator:arm64-apple-ios15.0-simulator" \
        "ios-arm64_x86_64-simulator:iphonesimulator:x86_64-apple-ios15.0-simulator"; do
        slice="${entry%%:*}"; rest="${entry#*:}"; sdk="${rest%%:*}"; triple="${rest#*:}"
        sdk_path="$(xcrun --sdk "$sdk" --show-sdk-path)"
        for language in 5 6; do
            if xcrun swiftc -typecheck -swift-version "$language" -module-name UnityFramework \
                    -target "$triple" -sdk "$sdk_path" -F "$XCFRAMEWORK/$slice" "$SWIFT_BRIDGE" > "$WORK/typecheck.log" 2>&1; then
                pass "swiftc -typecheck (Swift $language mode) for $triple"
            else
                fail "swiftc -typecheck (Swift $language mode) for $triple"; cat "$WORK/typecheck.log"
            fi
        done
        # The real thing: compile the bridge and link it with the static framework into a
        # dynamic library, which is what happens to both inside UnityFramework.
        library="$WORK/UnityFramework-$triple.dylib"
        if xcrun swiftc -emit-library -O -swift-version 5 -module-name UnityFramework \
                -target "$triple" -sdk "$sdk_path" -F "$XCFRAMEWORK/$slice" -framework ResponslySDK \
                "$SWIFT_BRIDGE" -o "$library" > "$WORK/link.log" 2>&1; then
            pass "compile + link with the static framework for $triple ($(stat -f %z "$library") bytes)"
            xcrun nm -gU "$library" | sed -n 's/.* T _\(responsly_unity_[a-z_]*\)$/\1/p' | sort > "$WORK/symbols-$triple.txt"
        else
            fail "compile + link with the static framework for $triple"; tail -20 "$WORK/link.log"
        fi
    done

    if SYMBOLS_DIR="$WORK" python3 - "$SWIFT_BRIDGE" "$CS_IOS" <<'PY'
import glob, os, re, sys

swift, csharp = open(sys.argv[1]).read(), open(sys.argv[2]).read()

def count(parameters):
    parameters = parameters.strip()
    return 0 if not parameters else len(parameters.split(","))

# @_cdecl("name") public func name(_ a: T, _ b: T)
swift_functions = {}
for name, function, parameters in re.findall(r'@_cdecl\("([^"]+)"\)\s*public func (\w+)\(([^)]*)\)', swift):
    swift_functions[name] = (function, count(re.sub(r"\([^()]*\)", "", parameters)))
# [DllImport("__Internal")] private static extern void name(type a, type b);
cs_functions = {}
for library, name, parameters in re.findall(r'\[DllImport\("([^"]+)"\)\]\s*private static extern void (\w+)\(([^)]*)\);', csharp):
    cs_functions[name] = (library, count(parameters), [p.strip().split(" ")[0] for p in parameters.split(",") if p.strip()])

ok = True
def check(condition, message):
    global ok
    print(("PASS  " if condition else "FAIL  ") + message)
    ok = ok and condition

print("      %-44s %-10s %-10s %s" % ("C symbol", "C# params", "Swift", "C# parameter types"))
for name in sorted(set(swift_functions) | set(cs_functions)):
    cs = cs_functions.get(name)
    sw = swift_functions.get(name)
    same = cs is not None and sw is not None and cs[1] == sw[1] and sw[0] == name and cs[0] == "__Internal"
    print("%s  %-44s %-10s %-10s %s" % ("ok  " if same else "DIFF", name, cs[1] if cs else "missing", sw[1] if sw else "missing", ", ".join(cs[2]) if cs else ""))
    ok = ok and same

check(len(cs_functions) > 0 and set(cs_functions) == set(swift_functions),
      "%d DllImport entry points in C# = %d @_cdecl functions in Swift" % (len(cs_functions), len(swift_functions)))
exported = sorted(glob.glob(os.path.join(os.environ["SYMBOLS_DIR"], "symbols-*.txt")))
check(len(exported) == 3, "symbols were read from 3 linked libraries")
for path in exported:
    symbols = set(open(path).read().split())
    triple = os.path.basename(path)[len("symbols-"):-len(".txt")]
    check(symbols == set(cs_functions), "nm: the %d exported responsly_unity_* symbols for %s are exactly the DllImport entry points" % (len(symbols), triple))
# bool never crosses the boundary: C# bool is 4 bytes in P/Invoke, a C bool is 1.
check(not re.search(r"extern void \w+\([^)]*\bbool\b", csharp), "no bool parameter in a DllImport signature")
check("Bool" not in "".join(p for _, _, p in re.findall(r'@_cdecl\("([^"]+)"\)\s*public func (\w+)\(([^)]*)\)', swift)), "no Bool parameter in a @_cdecl function")
check(re.search(r"delegate void MessageCallback\(IntPtr \w+, IntPtr \w+\);", csharp) is not None
      and "@convention(c) (UnsafePointer<CChar>?, UnsafePointer<CChar>?) -> Void" in swift,
      "callback: C# delegate (IntPtr, IntPtr) = Swift @convention(c) (UnsafePointer<CChar>?, UnsafePointer<CChar>?) -> Void")
check("[MonoPInvokeCallback(typeof(MessageCallback))]" in csharp, "the C# callback has [MonoPInvokeCallback]")
sys.exit(0 if ok else 1)
PY
    then :; else FAILURES=$((FAILURES + 1)); fi
fi

# ---------------------------------------------------------------------------------------------
section "4. iOS bridge on macOS"
# ---------------------------------------------------------------------------------------------
IOS_REPO="${RESPONSLY_IOS_SDK:-$ROOT/../responsly-ios-sdk}"
IOS_COMMIT="$(sed -n 's/.*"sdkCommit": "\([0-9a-f]*\)".*/\1/p' "Native~/ios/build-info.json" 2>/dev/null | head -1)"
if ! xcrun --find swiftc > /dev/null 2>&1; then
    skip "no Swift compiler (Xcode) on this machine"
elif [ -z "$IOS_COMMIT" ] || ! git -C "$IOS_REPO" cat-file -e "$IOS_COMMIT^{commit}" 2> /dev/null; then
    skip "the iOS SDK repository with commit ${IOS_COMMIT:-?} is not at $IOS_REPO"
else
    mkdir -p "$WORK/mac/src" "$WORK/mac/out"
    git -C "$IOS_REPO" archive "$IOS_COMMIT" Sources | tar -x -C "$WORK/mac/src"
    mac_target="$(uname -m)-apple-macos12.0"
    mac_sdk="$(xcrun --sdk macosx --show-sdk-path)"
    cat "$SWIFT_BRIDGE" "Native~/ios/test/BridgeTest.swift" > "$WORK/mac/out/main.swift"
    if xcrun swiftc -swift-version 5 -parse-as-library -module-name ResponslySDK \
            -emit-module -emit-module-path "$WORK/mac/out/ResponslySDK.swiftmodule" -emit-library -static \
            -target "$mac_target" -sdk "$mac_sdk" $(find "$WORK/mac/src/Sources/ResponslySDK" -name '*.swift') \
            -o "$WORK/mac/out/libResponslySDK.a" > "$WORK/mac/build.log" 2>&1 \
        && xcrun swiftc -swift-version 5 -module-name BridgeTest -target "$mac_target" -sdk "$mac_sdk" \
            -I "$WORK/mac/out" -L "$WORK/mac/out" -lResponslySDK "$WORK/mac/out/main.swift" \
            -o "$WORK/mac/out/bridge-test" >> "$WORK/mac/build.log" 2>&1 \
        && "$WORK/mac/out/bridge-test" > "$WORK/mac/test.log" 2>&1; then
        pass "Native~/ios/test against the iOS SDK at ${IOS_COMMIT:0:7}: $(grep -c '^ok' "$WORK/mac/test.log") checks"
    else
        fail "Native~/ios/test"; grep -E 'error|FAIL' "$WORK/mac/build.log" "$WORK/mac/test.log" 2> /dev/null | head -20
    fi
fi

# ---------------------------------------------------------------------------------------------
section "5. Android bridge"
# ---------------------------------------------------------------------------------------------
if [ ! -f "$AAR" ]; then
    fail "$AAR is missing (run scripts/build-native.sh)"
elif ! "$JAVAP" -version > /dev/null 2>&1; then
    skip "no JDK on this machine (set JAVA_HOME)"
else
    mkdir -p "$WORK/aar"
    unzip -q -o "$AAR" -d "$WORK/aar"
    "$JAVAP" -cp "$WORK/aar/classes.jar" -public \
        com.responsly.unity.ResponslyUnityBridge com.responsly.unity.ResponslyUnityCallback > "$WORK/javap.txt" 2>&1 \
        || fail "javap could not read the bridge classes from the AAR"
    "$JAVAP" -cp "$WORK/aar/classes.jar" -c -p \
        com.responsly.unity.ResponslyUnityBridge 'com.responsly.unity.ResponslyUnityBridge$1' \
        'com.responsly.unity.ResponslyUnityBridge$2' 'com.responsly.unity.ResponslyUnityBridge$2$1' \
        com.responsly.unity.ResponslyUnityJson > "$WORK/javap-code.txt" 2>&1

    if python3 - "$WORK/javap.txt" "$CS_ANDROID" "$WORK/aar/proguard.txt" "$WORK/javap-code.txt" \
            Editor/ResponslyDependencies.xml "Native~/android/build-info.json" "$WORK/aar/AndroidManifest.xml" <<'PY'
import json, re, sys

javap, csharp, proguard, bytecode, dependencies_xml = (open(p).read() for p in sys.argv[1:6])
build_info = json.load(open(sys.argv[6]))
manifest = open(sys.argv[7]).read()

ok = True
def check(condition, message):
    global ok
    print(("PASS  " if condition else "FAIL  ") + message)
    ok = ok and condition

# Java: public static void name(types);
java_methods = {}
bridge_section = javap.split("public interface")[0]
for name, parameters in re.findall(r"public static void (\w+)\(([^)]*)\);", bridge_section):
    java_methods[name] = [p.strip() for p in parameters.split(",") if p.strip()]
callback = re.search(r"public abstract void (\w+)\(([^)]*)\);", javap.split("public interface")[1])

# C#: what Unity derives a JNI signature from is the type of each argument.
JAVA_TYPE = {"string": "java.lang.String", "bool": "boolean", "int": "int"}
SPECIAL = {"activity": "android.content.Context (C#: the Unity activity)",
           "_proxy": "com.responsly.unity.ResponslyUnityCallback (C#: AndroidJavaProxy)"}
cs_calls = {}
for return_type, method, parameters, body in re.findall(
        r"public (void|ResponslyAndroidPlatform)\s*(\w*)\(([^)]*)\)\s*\{(.*?)\n        \}", csharp, re.S):
    types = {}
    for parameter in parameters.split(","):
        parts = parameter.strip().split(" ")
        if len(parts) == 2:
            types[parts[1]] = parts[0]
    for name, arguments in re.findall(r'_bridge\.CallStatic\("(\w+)"((?:, \w+)*)\);', body):
        resolved = []
        for argument in [a.strip() for a in arguments.split(",") if a.strip()]:
            if argument in SPECIAL:
                resolved.append(SPECIAL[argument])
            else:
                resolved.append(JAVA_TYPE.get(types.get(argument, "?"), "? (" + argument + ")"))
        cs_calls[name] = resolved

print("      %-24s %-58s %s" % ("method", "Java (javap, from the AAR)", "C# argument types as JNI sees them"))
for name in sorted(set(java_methods) | set(cs_calls)):
    java = java_methods.get(name)
    cs = cs_calls.get(name)
    same = java is not None and cs is not None and len(java) == len(cs) and all(c.split(" ")[0] == j for j, c in zip(java, cs))
    print("%s  %-24s %-58s %s" % ("ok  " if same else "DIFF", name, "(" + ", ".join(java) + ")" if java is not None else "missing",
                                  "(" + ", ".join(cs) + ")" if cs is not None else "not called"))
    ok = ok and same
check(len(cs_calls) > 0 and set(cs_calls) == set(java_methods), "%d CallStatic targets in C# = %d public static methods of ResponslyUnityBridge" % (len(cs_calls), len(java_methods)))
total_calls = len(re.findall(r"\.CallStatic\(", csharp))
check(total_calls == len(cs_calls), "every CallStatic in the C# file was understood (%d of %d)" % (len(cs_calls), total_calls))

proxy = re.search(r"public void (\w+)\(string \w+, string \w+\)", csharp.split("class MessageProxy")[-1])
check(callback is not None and proxy is not None and callback.group(1) == proxy.group(1)
      and [p.strip() for p in callback.group(2).split(",")] == ["java.lang.String", "java.lang.String"],
      "callback: Java %s(%s) = C# proxy method %s(string, string)" % (callback.group(1) if callback else "?", callback.group(2) if callback else "?", proxy.group(1) if proxy else "?"))
for constant, wanted in (("BridgeClassName", "com.responsly.unity.ResponslyUnityBridge"), ("CallbackInterfaceName", "com.responsly.unity.ResponslyUnityCallback")):
    check('%s = "%s"' % (constant, wanted) in csharp and wanted in javap, "C# %s is a class in the AAR: %s" % (constant, wanted))
check("[Preserve]" in csharp, "the proxy method is marked [Preserve] against managed code stripping")
check("-keep public class com.responsly.unity.ResponslyUnityBridge" in proguard
      and "-keep public interface com.responsly.unity.ResponslyUnityCallback" in proguard,
      "the AAR's consumer rules keep the bridge and the callback when the app is minified")
check("invokedynamic" not in bytecode, "the bridge classes contain no invokedynamic (nothing to desugar)")
check('android:minSdkVersion="23"' in manifest, "the AAR's manifest asks for minSdk 23")

declared = sorted(re.findall(r'<androidPackage spec="([^"]+)"', dependencies_xml))
check(declared == sorted(build_info["dependencies"]), "Editor/ResponslyDependencies.xml lists what the AAR was built against: " + ", ".join(declared))
sys.exit(0 if ok else 1)
PY
    then :; else FAILURES=$((FAILURES + 1)); fi

    # -----------------------------------------------------------------------------------------
    section "6. Android bridge on the JVM"
    # -----------------------------------------------------------------------------------------
    CACHE="$HOME/.gradle/caches/modules-2/files-2.1"
    KOTLIN_JAR="$(find "$CACHE/org.jetbrains.kotlin/kotlin-stdlib" -name 'kotlin-stdlib-2.0.21.jar' 2>/dev/null | head -1)"
    JSON_JAR="$(find "$CACHE/org.json/json" -name 'json-*.jar' 2>/dev/null | grep -v sources | head -1)"
    if [ -z "$KOTLIN_JAR" ] || [ -z "$JSON_JAR" ]; then
        skip "kotlin-stdlib 2.0.21 or org.json is not in the Gradle cache"
    else
        CLASSPATH_JARS="$WORK/aar/classes.jar:$KOTLIN_JAR:$JSON_JAR"
        mkdir -p "$WORK/test-classes"
        # Only the test and its stand-ins for android.* are compiled here; the bridge classes
        # under test are the ones inside the AAR.
        if "$JAVAC" -nowarn -d "$WORK/test-classes" -cp "$CLASSPATH_JARS" $(find "Native~/android/test" -name '*.java') > "$WORK/javac.log" 2>&1 \
                && "$JAVA" -cp "$WORK/test-classes:$CLASSPATH_JARS" com.responsly.unity.BridgeTest > "$WORK/test.log" 2>&1; then
            pass "Native~/android/test: $(grep -c '^ok' "$WORK/test.log") checks"
        else
            fail "Native~/android/test"; cat "$WORK/javac.log" "$WORK/test.log" 2>/dev/null
        fi
    fi
fi

# ---------------------------------------------------------------------------------------------
printf '\n'
if [ "$FAILURES" = 0 ]; then
    echo "All checks passed."
else
    echo "$FAILURES group(s) of checks FAILED."
    exit 1
fi
