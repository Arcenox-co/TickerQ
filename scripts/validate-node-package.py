#!/usr/bin/env python3
"""Validate an immutable @tickerq/sdk npm tarball's license boundary."""

from __future__ import annotations

import argparse
import json
import re
import sys
import tarfile
from pathlib import Path, PurePosixPath

PACKAGE_NAME = "@tickerq/sdk"
LICENSE_VALUE = "SEE LICENSE IN LICENSE.md"
REPOSITORY_URL = "https://github.com/Arcenox-co/TickerQ"
REPOSITORY_DIRECTORY = "hub/sdks/node"
LICENSE_PATH = "package/LICENSE.md"
NOTICES_PATH = "package/THIRD-PARTY-NOTICES.md"
REQUIRED_PATHS = {
    "package/package.json",
    "package/README.md",
    "package/dist/index.js",
    "package/dist/index.d.ts",
    LICENSE_PATH,
    NOTICES_PATH,
}
SEMVER_PATTERN = re.compile(
    r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)"
    r"(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$"
)
FORBIDDEN_LICENSE_FILES = {
    "COMMERCIAL.md",
    "LICENSE",
    "LICENSE-COMMERCIAL.md",
    "LICENSE-OSS.md",
    "OPEN-SOURCE.md",
}


def declared_entrypoints(package: dict[str, object]) -> tuple[set[str], list[str]]:
    targets: set[str] = set()
    errors: list[str] = []

    def add_target(label: str, value: object) -> None:
        if not isinstance(value, str) or not value:
            errors.append(f"{label} entrypoint is not a non-empty string")
            return
        normalized = value.removeprefix("./")
        path = PurePosixPath(normalized)
        if path.is_absolute() or ".." in path.parts or not path.parts:
            errors.append(f"{label} entrypoint {value!r} escapes the package")
            return
        targets.add(f"package/{path.as_posix()}")

    for field in ("main", "module", "types", "typings"):
        if field in package:
            add_target(field, package[field])

    if "bin" in package:
        bin_value = package["bin"]
        if isinstance(bin_value, str):
            add_target("bin", bin_value)
        elif isinstance(bin_value, dict):
            if not bin_value:
                errors.append("bin entrypoint map is empty")
            for name, value in bin_value.items():
                add_target(f"bin.{name}", value)
        else:
            errors.append(f"bin entrypoint has unsupported value {bin_value!r}")

    def walk_exports(value: object, label: str = "exports") -> None:
        if isinstance(value, str):
            add_target(label, value)
        elif isinstance(value, dict):
            for key, child in value.items():
                walk_exports(child, f"{label}.{key}")
        elif isinstance(value, list):
            for index, child in enumerate(value):
                walk_exports(child, f"{label}[{index}]")
        elif value is not None:
            errors.append(f"{label} entrypoint has unsupported value {value!r}")

    if "exports" in package:
        walk_exports(package["exports"])
    return targets, errors


def validate(
    archive_path: Path,
    expected_version: str,
    canonical_license: bytes,
    canonical_notices: bytes,
) -> list[str]:
    errors: list[str] = []
    version_match = SEMVER_PATTERN.fullmatch(expected_version)
    if version_match is None:
        errors.append(f"expected version {expected_version!r} is not semantic versioning")
    else:
        numeric_version = tuple(int(part) for part in version_match.groups()[:3])
        prerelease = version_match.group(4)
        if numeric_version < (1, 0, 0) or (
            numeric_version == (1, 0, 0) and prerelease is not None
        ):
            errors.append(
                f"version {expected_version!r} precedes commercial npm version 1.0.0"
            )
    try:
        with tarfile.open(archive_path, "r:gz") as archive:
            members = archive.getmembers()
            names = [member.name for member in members]
            name_set = set(names)
            folded_names = [name.casefold() for name in names]
            member_by_name = {member.name: member for member in members}
            if len(names) != len(name_set):
                errors.append("archive contains duplicate paths")
            if len(folded_names) != len(set(folded_names)):
                errors.append("archive contains case-insensitive path collisions")

            missing = sorted(REQUIRED_PATHS - name_set)
            if missing:
                errors.append(f"missing required files: {', '.join(missing)}")
            for name in sorted(REQUIRED_PATHS & name_set):
                if not member_by_name[name].isfile():
                    errors.append(f"{name} is not a regular file")

            stale = sorted(
                name
                for name in name_set
                if Path(name).name.casefold()
                in {filename.casefold() for filename in FORBIDDEN_LICENSE_FILES}
            )
            if stale:
                errors.append(f"stale license file(s): {', '.join(stale)}")

            if "package/package.json" in name_set:
                package_file = archive.extractfile("package/package.json")
                if package_file is None:
                    errors.append("package/package.json is not a regular file")
                else:
                    try:
                        package = json.load(package_file)
                    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
                        errors.append(f"invalid package/package.json: {exc}")
                    else:
                        entrypoints, entrypoint_errors = declared_entrypoints(package)
                        errors.extend(entrypoint_errors)
                        for entrypoint in sorted(entrypoints):
                            member = member_by_name.get(entrypoint)
                            if member is None:
                                errors.append(f"declared entrypoint is missing: {entrypoint}")
                            elif not member.isfile():
                                errors.append(
                                    f"declared entrypoint is not a regular file: {entrypoint}"
                                )
                        if package.get("name") != PACKAGE_NAME:
                            errors.append(
                                f"package name {package.get('name')!r} != {PACKAGE_NAME!r}"
                            )
                        if package.get("version") != expected_version:
                            errors.append(
                                f"package version {package.get('version')!r} != {expected_version!r}"
                            )
                        if package.get("license") != LICENSE_VALUE:
                            errors.append(
                                f"license {package.get('license')!r} != {LICENSE_VALUE!r}"
                            )
                        if "publishConfig" in package:
                            errors.append(
                                "package publishConfig is forbidden; publication policy is workflow-controlled"
                            )
                        repository = package.get("repository") or {}
                        repository_url = str(repository.get("url", "")).removesuffix(".git")
                        if repository_url.casefold() != REPOSITORY_URL.casefold():
                            errors.append(
                                f"repository URL {repository_url!r} != {REPOSITORY_URL!r}"
                            )
                        if repository.get("directory") != REPOSITORY_DIRECTORY:
                            errors.append(
                                f"repository directory {repository.get('directory')!r} != "
                                f"{REPOSITORY_DIRECTORY!r}"
                            )

            if LICENSE_PATH in name_set:
                license_file = archive.extractfile(LICENSE_PATH)
                if license_file is None or license_file.read() != canonical_license:
                    errors.append("embedded commercial terms differ from canonical source")
            if NOTICES_PATH in name_set:
                notices_file = archive.extractfile(NOTICES_PATH)
                if notices_file is None or notices_file.read() != canonical_notices:
                    errors.append("embedded third-party notices differ from canonical source")
    except (OSError, tarfile.TarError) as exc:
        errors.append(f"cannot inspect package: {exc}")

    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--archive", required=True, type=Path)
    parser.add_argument("--version", required=True)
    parser.add_argument("--commercial-license", required=True, type=Path)
    parser.add_argument("--third-party-notices", required=True, type=Path)
    args = parser.parse_args()

    for path in (args.archive, args.commercial_license, args.third_party_notices):
        if not path.is_file():
            parser.error(f"file does not exist: {path}")

    errors = validate(
        args.archive,
        args.version,
        args.commercial_license.read_bytes(),
        args.third_party_notices.read_bytes(),
    )
    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        return 1

    print(f"Validated {PACKAGE_NAME}@{args.version}: commercial.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
