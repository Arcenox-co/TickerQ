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
    "TickerQ.MongoDB",
    "TickerQ.Integrations",
    "TickerQ.SDK",
    "TickerQ.RemoteExecutor",
}
EXPECTED_PACKAGE_IDS = COMMERCIAL_PACKAGE_IDS
EXPECTED_PACKAGE_IDS_BY_FOLD = {
    package_id.casefold(): package_id for package_id in EXPECTED_PACKAGE_IDS
}
COMMERCIAL_LICENSE_FILE = "LICENSE.md"
THIRD_PARTY_NOTICES_FILE = "THIRD-PARTY-NOTICES.md"
DASHBOARD_THIRD_PARTY_NOTICES_FILE = "DASHBOARD-THIRD-PARTY-NOTICES.md"
FORBIDDEN_LEGACY_LICENSE_FILES = {
    "COMMERCIAL.md",
    "LICENSE-COMMERCIAL.md",
    "LICENSE-OSS.md",
    "OPEN-SOURCE.md",
}
NUGET_FILE_LICENSE_SENTINEL_URL = "https://aka.ms/deprecateLicenseUrl"
REPOSITORY_URL = "https://github.com/arcenox-co/TickerQ"
DEFAULT_AUTHORS = "Arcenox LLC"
PACKAGE_AUTHORS = {
    "TickerQ.MongoDB": "Arcenox,Alex Brown",
}
EXPECTED_COPYRIGHT = "Copyright 2025-present Arcenox LLC and contributors"
# Every library package ships a .snupkg (portable PDBs with Source Link) for the NuGet.org symbol
# server. The source generator is analyzer-only: it has no lib PDB, so it has no symbol package.
SYMBOL_PACKAGE_EXEMPT_IDS = {"TickerQ.SourceGenerator"}
SYMBOL_PACKAGE_OPC_ENTRIES = {"[Content_Types].xml", "_rels/.rels"}


def _text(element: ET.Element | None) -> str:
    return "" if element is None or element.text is None else element.text.strip()


def _metadata(root: ET.Element) -> ET.Element:
    metadata = root.find("{*}metadata")
    if metadata is None:
        raise ValueError("nuspec has no metadata element")
    return metadata


def _read_package(path: Path) -> tuple[str, str, ET.Element, set[str], zipfile.ZipFile]:
    archive = zipfile.ZipFile(path)
    try:
        names = [info.filename for info in archive.infolist() if not info.is_dir()]
        if len(names) != len(set(names)):
            raise ValueError("archive contains duplicate paths")
        folded_names = [name.casefold() for name in names]
        if len(folded_names) != len(set(folded_names)):
            raise ValueError("archive contains case-insensitive path collisions")
        name_set = set(names)
        nuspec_names = [name for name in name_set if name.lower().endswith(".nuspec")]
        if len(nuspec_names) != 1:
            raise ValueError(f"expected exactly one nuspec, found {len(nuspec_names)}")

        root = ET.fromstring(archive.read(nuspec_names[0]))
        metadata = _metadata(root)
        package_id = _text(metadata.find("{*}id"))
        version = _text(metadata.find("{*}version"))
        return package_id, version, metadata, name_set, archive
    except Exception:
        archive.close()
        raise


def _validate_symbol_package(
    path: Path, expected_version: str, packages: dict[str, Path]
) -> list[str]:
    """A .snupkg must belong to a validated .nupkg and contain nothing but PDBs and package metadata."""
    try:
        package_id, version, metadata, names, archive = _read_package(path)
    except (OSError, ValueError, ET.ParseError, zipfile.BadZipFile) as exc:
        return [f"unexpected symbol package {path.name}: cannot inspect package: {exc}"]
    archive.close()

    errors: list[str] = []
    package_key = package_id.casefold()
    if package_key not in packages or package_id in SYMBOL_PACKAGE_EXEMPT_IDS:
        return [f"unexpected symbol package {path.name}: no matching library package"]
    if path.name != f"{package_id}.{version}.snupkg":
        errors.append(f"{path.name}: symbol package name must be {package_id}.{version}.snupkg")
    if version != expected_version:
        errors.append(f"{path.name}: version {version!r} != expected {expected_version!r}")
    package_types = {
        element.attrib.get("name", "") for element in metadata.findall(".//{*}packageType")
    }
    if "SymbolsPackage" not in package_types:
        errors.append(f"{path.name}: not declared as a SymbolsPackage")
    pdbs = [name for name in names if name.startswith("lib/") and name.endswith(".pdb")]
    if not pdbs:
        errors.append(f"{path.name}: contains no lib PDB")
    other_entries = sorted(
        name
        for name in names
        if name not in pdbs
        and name not in SYMBOL_PACKAGE_OPC_ENTRIES
        and not name.endswith(".nuspec")
        and not (name.startswith("package/services/metadata/core-properties/") and name.endswith(".psmdcp"))
    )
    if other_entries:
        errors.append(f"{path.name}: unexpected symbol package entries: {', '.join(other_entries)}")
    return errors


def validate(
    directory: Path,
    expected_version: str,
    canonical_license: bytes,
    canonical_notices: bytes,
    canonical_dashboard_notices: bytes,
    expected_repository_commit: str | None = None,
    require_symbol_packages: bool = True,
) -> list[str]:
    """Validates a package directory. With require_symbol_packages, every library package must have a
    matching .snupkg; without it (packages read back from the registry), no .snupkg is allowed."""
    errors: list[str] = []
    allowed_suffixes = (".nupkg", ".snupkg") if require_symbol_packages else (".nupkg",)
    unexpected_entries = sorted(
        path.name
        for path in directory.iterdir()
        if path.is_symlink() or not path.is_file() or not path.name.endswith(allowed_suffixes)
    )
    if unexpected_entries:
        errors.append(
            "package directory contains unexpected entries: " + ", ".join(unexpected_entries)
        )
    packages: dict[str, Path] = {}
    version_match = re.match(r"^(8|9|10|11)\.(\d+)(?:\.|-)", expected_version)
    if version_match is None or int(version_match.group(2)) < 5:
        errors.append(
            f"version {expected_version!r} is outside the commercial version family "
            "(.NET major 8, 9, 10, or 11; functional minor >= 5)"
        )

    for path in sorted(directory.glob("*.nupkg")):
        try:
            package_id, version, metadata, names, archive = _read_package(path)
        except (OSError, ValueError, ET.ParseError, zipfile.BadZipFile) as exc:
            errors.append(f"{path.name}: cannot inspect package: {exc}")
            continue

        try:
            package_key = package_id.casefold()
            canonical_package_id = EXPECTED_PACKAGE_IDS_BY_FOLD.get(package_key)
            if package_key in packages:
                errors.append(
                    f"duplicate package ID {package_id}: {packages[package_key].name}, {path.name}"
                )
            packages[package_key] = path
            if canonical_package_id is not None and package_id != canonical_package_id:
                errors.append(
                    f"package ID {package_id!r} must use canonical casing {canonical_package_id!r}"
                )

            if version != expected_version:
                errors.append(f"{package_id}: version {version!r} != expected {expected_version!r}")

            authors = _text(metadata.find("{*}authors"))
            expected_authors = PACKAGE_AUTHORS.get(package_id, DEFAULT_AUTHORS)
            if authors != expected_authors:
                errors.append(f"{package_id}: authors {authors!r} != {expected_authors!r}")
            copyright_notice = _text(metadata.find("{*}copyright"))
            if copyright_notice != EXPECTED_COPYRIGHT:
                errors.append(
                    f"{package_id}: copyright {copyright_notice!r} != {EXPECTED_COPYRIGHT!r}"
                )

            for dependency in metadata.findall(".//{*}dependency"):
                dependency_id = dependency.attrib.get("id", "")
                dependency_version = dependency.attrib.get("version", "")
                if (
                    dependency_id.casefold() in EXPECTED_PACKAGE_IDS_BY_FOLD
                    and dependency_version != expected_version
                ):
                    errors.append(
                        f"{package_id}: {dependency_id} dependency version "
                        f"{dependency_version!r} != expected {expected_version!r}"
                    )

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

            if package_key in EXPECTED_PACKAGE_IDS_BY_FOLD:
                require_acceptance = _text(metadata.find("{*}requireLicenseAcceptance"))
                if require_acceptance.casefold() != "true":
                    errors.append(
                        f"{package_id}: requireLicenseAcceptance must be true, found {require_acceptance!r}"
                    )
                if license_type != "file" or license_value != COMMERCIAL_LICENSE_FILE:
                    errors.append(
                        f"{package_id}: expected file license {COMMERCIAL_LICENSE_FILE}, "
                        f"found type={license_type!r} value={license_value!r}"
                    )
                if COMMERCIAL_LICENSE_FILE not in names:
                    errors.append(f"{package_id}: missing embedded {COMMERCIAL_LICENSE_FILE}")
                elif archive.read(COMMERCIAL_LICENSE_FILE) != canonical_license:
                    errors.append(f"{package_id}: embedded commercial terms differ from canonical source")
                if THIRD_PARTY_NOTICES_FILE not in names:
                    errors.append(f"{package_id}: missing embedded {THIRD_PARTY_NOTICES_FILE}")
                elif archive.read(THIRD_PARTY_NOTICES_FILE) != canonical_notices:
                    errors.append(f"{package_id}: embedded third-party notices differ from canonical source")
                if package_id == "TickerQ.Dashboard":
                    if DASHBOARD_THIRD_PARTY_NOTICES_FILE not in names:
                        errors.append(
                            f"{package_id}: missing embedded {DASHBOARD_THIRD_PARTY_NOTICES_FILE}"
                        )
                    elif (
                        archive.read(DASHBOARD_THIRD_PARTY_NOTICES_FILE)
                        != canonical_dashboard_notices
                    ):
                        errors.append(
                            f"{package_id}: embedded dashboard third-party notices differ "
                            "from canonical source"
                        )
                stale_license_files = sorted(
                    name
                    for name in names
                    if Path(name).name.casefold()
                    in {filename.casefold() for filename in FORBIDDEN_LEGACY_LICENSE_FILES}
                )
                if stale_license_files:
                    errors.append(
                        f"{package_id}: stale OSS license file(s): {', '.join(stale_license_files)}"
                    )
                license_url = _text(metadata.find("{*}licenseUrl"))
                if license_url and license_url != NUGET_FILE_LICENSE_SENTINEL_URL:
                    errors.append(f"{package_id}: unexpected legacy licenseUrl {license_url!r}")
            else:
                errors.append(f"unexpected package ID {package_id!r} in {path.name}")

            if package_id == "TickerQ.SourceGenerator" and not any(
                name.lower() == "analyzers/dotnet/cs/tickerq.sourcegenerator.dll" for name in names
            ):
                errors.append("TickerQ.SourceGenerator: analyzer DLL is not under analyzers/dotnet/cs")
        finally:
            archive.close()

    missing = sorted(
        package_id
        for package_key, package_id in EXPECTED_PACKAGE_IDS_BY_FOLD.items()
        if package_key not in packages
    )
    if missing:
        errors.append(f"missing expected packages: {', '.join(missing)}")

    if require_symbol_packages:
        symbol_packages: set[str] = set()
        for path in sorted(directory.glob("*.snupkg")):
            symbol_errors = _validate_symbol_package(path, expected_version, packages)
            errors.extend(symbol_errors)
            if not symbol_errors:
                symbol_packages.add(path.name.casefold())
        missing_symbols = sorted(
            package_id
            for package_key, package_id in EXPECTED_PACKAGE_IDS_BY_FOLD.items()
            if package_key in packages
            and package_id not in SYMBOL_PACKAGE_EXEMPT_IDS
            and f"{package_id}.{expected_version}.snupkg".casefold() not in symbol_packages
        )
        if missing_symbols:
            errors.append(f"missing symbol packages: {', '.join(missing_symbols)}")

    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--directory", required=True, type=Path)
    parser.add_argument("--version", required=True)
    parser.add_argument("--commercial-license", required=True, type=Path)
    parser.add_argument("--third-party-notices", required=True, type=Path)
    parser.add_argument("--dashboard-third-party-notices", required=True, type=Path)
    parser.add_argument("--repository-commit")
    parser.add_argument(
        "--no-symbol-packages",
        action="store_true",
        help="The directory holds packages read back from the registry: no .snupkg is expected or allowed",
    )
    args = parser.parse_args()

    if not args.directory.is_dir():
        parser.error(f"package directory does not exist: {args.directory}")
    if not args.commercial_license.is_file():
        parser.error(f"canonical commercial license does not exist: {args.commercial_license}")
    if not args.third_party_notices.is_file():
        parser.error(f"canonical third-party notices do not exist: {args.third_party_notices}")
    if not args.dashboard_third_party_notices.is_file():
        parser.error(
            "canonical dashboard third-party notices do not exist: "
            f"{args.dashboard_third_party_notices}"
        )

    errors = validate(
        args.directory,
        args.version,
        args.commercial_license.read_bytes(),
        args.third_party_notices.read_bytes(),
        args.dashboard_third_party_notices.read_bytes(),
        args.repository_commit,
        require_symbol_packages=not args.no_symbol_packages,
    )
    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        return 1

    print(
        f"Validated {len(EXPECTED_PACKAGE_IDS)} packages at {args.version}: "
        f"{len(COMMERCIAL_PACKAGE_IDS)} commercial."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
