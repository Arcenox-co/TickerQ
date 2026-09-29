#!/usr/bin/env python3
"""Validate the immutable license boundary of a TickerQ NuGet artifact set."""

from __future__ import annotations

import argparse
import re
import sys
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

COMMERCIAL_PACKAGE_IDS = {
    "TickerQ",
    "TickerQ.Utilities",
    "TickerQ.EntityFrameworkCore",
    "TickerQ.Caching.StackExchangeRedis",
    "TickerQ.Dashboard",
    "TickerQ.Instrumentation.OpenTelemetry",
    "TickerQ.SourceGenerator",
    "TickerQ.SDK",
    "TickerQ.RemoteExecutor",
}
OSS_PACKAGE_IDS = {"TickerQ.MongoDB"}
EXPECTED_PACKAGE_IDS = COMMERCIAL_PACKAGE_IDS | OSS_PACKAGE_IDS
COMMERCIAL_LICENSE_FILE = "COMMERCIAL.md"
OSS_LICENSE_FILE = "OPEN-SOURCE.md"
OSS_LICENSE_EXPRESSION = "MIT OR Apache-2.0"
NUGET_FILE_LICENSE_SENTINEL_URL = "https://aka.ms/deprecateLicenseUrl"
REPOSITORY_URL = "https://github.com/arcenox-co/TickerQ"


def _text(element: ET.Element | None) -> str:
    return "" if element is None or element.text is None else element.text.strip()


def _metadata(root: ET.Element) -> ET.Element:
    metadata = root.find("{*}metadata")
    if metadata is None:
        raise ValueError("nuspec has no metadata element")
    return metadata


def _read_package(path: Path) -> tuple[str, str, ET.Element, set[str], zipfile.ZipFile]:
    archive = zipfile.ZipFile(path)
    names = set(archive.namelist())
    nuspec_names = [name for name in names if name.lower().endswith(".nuspec")]
    if len(nuspec_names) != 1:
        archive.close()
        raise ValueError(f"expected exactly one nuspec, found {len(nuspec_names)}")

    root = ET.fromstring(archive.read(nuspec_names[0]))
    metadata = _metadata(root)
    package_id = _text(metadata.find("{*}id"))
    version = _text(metadata.find("{*}version"))
    return package_id, version, metadata, names, archive


def validate(
    directory: Path,
    expected_version: str,
    canonical_license: bytes,
    expected_repository_commit: str | None = None,
) -> list[str]:
    errors: list[str] = []
    packages: dict[str, Path] = {}
    version_match = re.match(r"^\d+\.(\d+)(?:\.|-)", expected_version)
    if version_match is None or int(version_match.group(1)) < 5:
        errors.append(
            f"version {expected_version!r} is before the commercial functional-line boundary (minor >= 5)"
        )

    for path in sorted(directory.glob("*.nupkg")):
        try:
            package_id, version, metadata, names, archive = _read_package(path)
        except (OSError, ValueError, ET.ParseError, zipfile.BadZipFile) as exc:
            errors.append(f"{path.name}: cannot inspect package: {exc}")
            continue

        try:
            if package_id in packages:
                errors.append(f"duplicate package ID {package_id}: {packages[package_id].name}, {path.name}")
            packages[package_id] = path

            if version != expected_version:
                errors.append(f"{package_id}: version {version!r} != expected {expected_version!r}")

            repository = metadata.find("{*}repository")
            if repository is None:
                errors.append(f"{package_id}: missing repository metadata")
            else:
                repository_url = repository.attrib.get("url", "").removesuffix(".git")
                if repository_url.casefold() != REPOSITORY_URL.casefold():
                    errors.append(
                        f"{package_id}: repository URL {repository_url!r} != {REPOSITORY_URL!r}"
                    )
                if expected_repository_commit is not None:
                    package_commit = repository.attrib.get("commit")
                    if package_commit != expected_repository_commit:
                        errors.append(
                            f"{package_id}: repository commit {package_commit!r} != "
                            f"expected {expected_repository_commit!r}"
                        )

            license_element = metadata.find("{*}license")
            license_type = "" if license_element is None else license_element.attrib.get("type", "")
            license_value = _text(license_element)

            if package_id in COMMERCIAL_PACKAGE_IDS:
                if license_type != "file" or license_value != COMMERCIAL_LICENSE_FILE:
                    errors.append(
                        f"{package_id}: expected file license {COMMERCIAL_LICENSE_FILE}, "
                        f"found type={license_type!r} value={license_value!r}"
                    )
                if COMMERCIAL_LICENSE_FILE not in names:
                    errors.append(f"{package_id}: missing embedded {COMMERCIAL_LICENSE_FILE}")
                elif archive.read(COMMERCIAL_LICENSE_FILE) != canonical_license:
                    errors.append(f"{package_id}: embedded commercial terms differ from canonical source")
                license_url = _text(metadata.find("{*}licenseUrl"))
                if license_url and license_url != NUGET_FILE_LICENSE_SENTINEL_URL:
                    errors.append(f"{package_id}: unexpected legacy licenseUrl {license_url!r}")
            elif package_id in OSS_PACKAGE_IDS:
                if license_type != "expression" or license_value != OSS_LICENSE_EXPRESSION:
                    errors.append(
                        f"{package_id}: expected expression {OSS_LICENSE_EXPRESSION!r}, "
                        f"found type={license_type!r} value={license_value!r}"
                    )
                if OSS_LICENSE_FILE not in names:
                    errors.append(f"{package_id}: missing preserved {OSS_LICENSE_FILE}")
            else:
                errors.append(f"unexpected package ID {package_id!r} in {path.name}")

            if package_id == "TickerQ.SourceGenerator" and not any(
                name.lower() == "analyzers/dotnet/cs/tickerq.sourcegenerator.dll" for name in names
            ):
                errors.append("TickerQ.SourceGenerator: analyzer DLL is not under analyzers/dotnet/cs")
        finally:
            archive.close()

    missing = sorted(EXPECTED_PACKAGE_IDS - packages.keys())
    if missing:
        errors.append(f"missing expected packages: {', '.join(missing)}")

    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--directory", required=True, type=Path)
    parser.add_argument("--version", required=True)
    parser.add_argument("--commercial-license", required=True, type=Path)
    parser.add_argument("--repository-commit")
    args = parser.parse_args()

    if not args.directory.is_dir():
        parser.error(f"package directory does not exist: {args.directory}")
    if not args.commercial_license.is_file():
        parser.error(f"canonical commercial license does not exist: {args.commercial_license}")

    errors = validate(
        args.directory,
        args.version,
        args.commercial_license.read_bytes(),
        args.repository_commit,
    )
    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        return 1

    print(
        f"Validated {len(EXPECTED_PACKAGE_IDS)} packages at {args.version}: "
        f"{len(COMMERCIAL_PACKAGE_IDS)} commercial, {len(OSS_PACKAGE_IDS)} open-source."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
