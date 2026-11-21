#!/usr/bin/env python3
"""Validate Unity package metadata and native artifact integrity without build tools."""

from pathlib import Path
import hashlib
import json
import os
import re
import sys


def check_versions():
    ok = True
    def check(condition, message):
        nonlocal ok
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
        if "sdkSource" in info or "sdkSourceSha256" in info:
            check(info.get("sdkSource") in ("commit", "working-tree")
                  and re.fullmatch(r"[0-9a-f]{64}", str(info.get("sdkSourceSha256", ""))) is not None,
                  path + ": native source snapshot is identified")
    check(documents.get("Runtime/Responsly.Runtime.asmdef", {}).get("name") == "Responsly.Runtime", "runtime assembly is Responsly.Runtime")
    check(documents.get("Editor/Responsly.Editor.asmdef", {}).get("includePlatforms") == ["Editor"], "editor assembly is Editor-only")
    for sample in package.get("samples", []):
        check(os.path.isdir(sample["path"]), "sample path exists: " + sample["path"])
    return ok


def native_integrity_errors(root):
    """Compare packaged bytes with their recorded hashes; never trust metadata paths."""
    errors = []

    def load(relative):
        try:
            document = json.loads((root / relative).read_text())
            if not isinstance(document, dict):
                raise ValueError("expected a JSON object")
            return document
        except (OSError, ValueError) as error:
            errors.append("%s: %s" % (relative, error))
            return {}

    def checksum(relative, expected):
        if not isinstance(expected, str) or re.fullmatch(r"[0-9a-f]{64}", expected) is None:
            errors.append("%s: missing or malformed SHA-256" % relative)
            return
        try:
            digest = hashlib.sha256()
            with (root / relative).open("rb") as source:
                for chunk in iter(lambda: source.read(1024 * 1024), b""):
                    digest.update(chunk)
            if digest.hexdigest() != expected:
                errors.append("%s: SHA-256 differs from build-info.json" % relative)
        except OSError as error:
            errors.append("%s: %s" % (relative, error))

    android = load("Native~/android/build-info.json")
    android_artifact = "Plugins/Android/responsly-sdk.aar"
    if android.get("artifact") != android_artifact:
        errors.append("Android build-info.json: unexpected artifact path")
    checksum(android_artifact, android.get("sha256"))

    ios = load("Native~/ios/build-info.json")
    ios_artifact = "Plugins/iOS/ResponslySDK.xcframework"
    if ios.get("artifact") != ios_artifact:
        errors.append("iOS build-info.json: unexpected artifact path")
    slices = ios.get("slices")
    if not isinstance(slices, list) or not slices:
        errors.append("iOS build-info.json: no framework slices recorded")
        slices = []
    declared = set()
    for entry in slices:
        identifier = entry.get("id") if isinstance(entry, dict) else None
        if not isinstance(identifier, str) or re.fullmatch(r"ios-[A-Za-z0-9_-]+", identifier) is None:
            errors.append("iOS build-info.json: invalid slice identifier")
            continue
        if identifier in declared:
            errors.append("iOS build-info.json: duplicate slice %s" % identifier)
        declared.add(identifier)
        checksum("%s/%s/ResponslySDK.framework/ResponslySDK" % (ios_artifact, identifier), entry.get("sha256"))
    present = {framework.parent.name for framework in
               (root / ios_artifact).glob("*/ResponslySDK.framework")}
    if present != declared:
        errors.append("iOS framework slices differ from build-info.json (packaged: %s; recorded: %s)" %
                      (", ".join(sorted(present)), ", ".join(sorted(declared))))
    return errors


def check_native_integrity():
    errors = native_integrity_errors(Path.cwd())
    for error in errors:
        print("FAIL  " + error)
    if not errors:
        print("PASS  native artifact SHA-256 checksums and framework slices match build-info.json")
    return not errors


def check_metas():
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
    return ok


if __name__ == "__main__":
    os.chdir(Path(__file__).resolve().parent.parent)
    results = [check_versions(), check_native_integrity(), check_metas()]
    sys.exit(0 if all(results) else 1)
