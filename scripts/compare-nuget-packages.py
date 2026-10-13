#!/usr/bin/env python3
"""Compare a local NuGet package with a registry-signed copy."""

from __future__ import annotations

import argparse
import sys
import zipfile
from pathlib import Path

IGNORED_MEMBERS = {".signature.p7s"}


def package_members(path: Path) -> tuple[dict[str, bytes], list[str]]:
    errors: list[str] = []
    members: dict[str, bytes] = {}
    try:
        with zipfile.ZipFile(path) as archive:
            names = [info.filename for info in archive.infolist() if not info.is_dir()]
            if len(names) != len(set(names)):
                errors.append(f"{path.name}: archive contains duplicate paths")
            for name in names:
                if name.casefold() in IGNORED_MEMBERS:
                    continue
                members[name] = archive.read(name)
    except (OSError, KeyError, zipfile.BadZipFile) as error:
        errors.append(f"{path.name}: cannot inspect package: {error}")
    return members, errors


def compare(local_path: Path, registry_path: Path) -> list[str]:
    local, errors = package_members(local_path)
    registry, registry_errors = package_members(registry_path)
    errors.extend(registry_errors)

    local_names = set(local)
    registry_names = set(registry)
    for name in sorted(local_names - registry_names):
        errors.append(f"registry package is missing {name}")
    for name in sorted(registry_names - local_names):
        errors.append(f"registry package has unexpected {name}")
    for name in sorted(local_names & registry_names):
        if local[name] != registry[name]:
            errors.append(f"registry package differs at {name}")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--local", required=True, type=Path)
    parser.add_argument("--registry", required=True, type=Path)
    args = parser.parse_args()

    for path in (args.local, args.registry):
        if not path.is_file():
            parser.error(f"package does not exist: {path}")

    errors = compare(args.local, args.registry)
    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        return 1

    print(f"Registry package matches {args.local.name} excluding repository signature metadata.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
