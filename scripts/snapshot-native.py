#!/usr/bin/env python3
"""Export a native SDK snapshot and print its deterministic source SHA-256."""

import argparse
import hashlib
from pathlib import Path
import shutil
import stat
import subprocess


def source_digest(root):
    digest = hashlib.sha256()
    for source in sorted(root.rglob("*")):
        if source.is_symlink():
            kind, content = b"link", str(source.readlink()).encode("utf-8", "surrogateescape")
        elif source.is_file():
            kind, content = b"file", source.read_bytes()
        else:
            continue
        relative = source.relative_to(root).as_posix().encode("utf-8", "surrogateescape")
        mode = str(stat.S_IMODE(source.lstat().st_mode)).encode("ascii")
        for value in (relative, kind, mode, content):
            digest.update(len(value).to_bytes(8, "big"))
            digest.update(value)
    return digest.hexdigest()


def snapshot(repository, commit, destination, working_tree=False):
    # The caller supplies a fresh scratch directory. Never erase an existing destination.
    destination.mkdir(parents=True, exist_ok=False)
    if working_tree:
        result = subprocess.run(
            ["git", "-C", str(repository), "ls-files", "-z", "--cached", "--others", "--exclude-standard"],
            check=True, stdout=subprocess.PIPE,
        )
        for raw in sorted(set(result.stdout.split(b"\0")) - {b""}):
            relative = raw.decode("utf-8", "surrogateescape")
            source, target = repository / relative, destination / relative
            if not source.exists() and not source.is_symlink():
                continue  # A tracked file deleted from the working tree.
            if source.is_dir():
                raise ValueError("submodules are not supported: " + relative)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, target, follow_symlinks=False)
    else:
        archive = subprocess.run(
            ["git", "-C", str(repository), "archive", "--format=tar", commit],
            check=True, stdout=subprocess.PIPE,
        )
        subprocess.run(["tar", "-x", "-C", str(destination)], input=archive.stdout, check=True)
    return source_digest(destination)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("repository", type=Path)
    parser.add_argument("commit")
    parser.add_argument("destination", type=Path)
    parser.add_argument("--working-tree", action="store_true")
    arguments = parser.parse_args()
    print(snapshot(arguments.repository, arguments.commit, arguments.destination, arguments.working_tree))
