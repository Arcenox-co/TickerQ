#!/usr/bin/env python3
"""Restore, run and optionally NativeAOT-publish consumers of the staged NuGet artifacts."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
import shlex
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile


REQUIRED = {
    "TickerQ", "TickerQ.Utilities", "TickerQ.Dashboard", "TickerQ.EntityFrameworkCore",
    "TickerQ.MongoDB", "TickerQ.Caching.StackExchangeRedis", "TickerQ.Integrations",
    "TickerQ.Instrumentation.OpenTelemetry",
}


def inspect_feed(directory: Path, version: str) -> None:
    found: set[str] = set()
    for path in sorted(directory.glob("*.nupkg")):
        with zipfile.ZipFile(path) as archive:
            specs = [name for name in archive.namelist() if name.endswith(".nuspec")]
            if len(specs) != 1:
                raise ValueError(f"{path.name}: expected one nuspec")
            metadata = ET.fromstring(archive.read(specs[0])).find("{*}metadata")
            if metadata is None:
                raise ValueError(f"{path.name}: missing nuspec metadata")
            package_id = metadata.findtext("{*}id", "")
            package_version = metadata.findtext("{*}version", "")
            if package_id not in REQUIRED:
                continue
            if package_version != version:
                raise ValueError(f"{package_id}: staged version {package_version!r} != {version!r}")
            if package_id in found:
                raise ValueError(f"Duplicate staged package: {package_id}")
            found.add(package_id)
            if package_id == "TickerQ" and "analyzers/dotnet/cs/TickerQ.SourceGenerator.dll" not in archive.namelist():
                raise ValueError("TickerQ must embed its source generator under analyzers/dotnet/cs")
    if missing := REQUIRED - found:
        raise ValueError("Missing staged packages: " + ", ".join(sorted(missing)))


def write_nuget_config(path: Path, feed: Path) -> None:
    root = ET.Element("configuration")
    sources = ET.SubElement(root, "packageSources")
    ET.SubElement(sources, "clear")
    ET.SubElement(sources, "add", key="staged", value=str(feed))
    ET.SubElement(sources, "add", key="third-party", value="https://api.nuget.org/v3/index.json")
    mapping = ET.SubElement(root, "packageSourceMapping")
    ET.SubElement(mapping, "clear")
    staged = ET.SubElement(mapping, "packageSource", key="staged")
    ET.SubElement(staged, "package", pattern="TickerQ")
    ET.SubElement(staged, "package", pattern="TickerQ.*")
    public = ET.SubElement(mapping, "packageSource", key="third-party")
    ET.SubElement(public, "package", pattern="*")
    ET.indent(root)
    ET.ElementTree(root).write(path, encoding="utf-8", xml_declaration=True)


def audit_assets(project: Path, version: str, cache: Path) -> None:
    assets = json.loads((project.parent / "obj/project.assets.json").read_text())
    libraries = assets["libraries"]
    if any(library.get("type") == "project" for library in libraries.values()):
        raise ValueError(f"{project.name}: project references leaked into an installed-package consumer")
    for name, library in libraries.items():
        package_id, resolved = name.rsplit("/", 1)
        if package_id == "TickerQ" or package_id.startswith("TickerQ."):
            if resolved != version or library.get("type") != "package":
                raise ValueError(f"{project.name}: expected {package_id}/{version} as a package; found {name}")
    folders = {Path(folder).resolve() for folder in assets["packageFolders"]}
    if folders != {cache.resolve()}:
        raise ValueError(f"{project.name}: restore used an unexpected package cache: {folders}")
    if not any(name.startswith("TickerQ/") for name in libraries):
        raise ValueError(f"{project.name}: the installed Core package was not resolved")


def run(command: list[str], directory: Path, environment: dict[str, str], *, timeout: int = 1200,
        marker: str | None = None) -> None:
    print("+ " + shlex.join(command), flush=True)
    if marker is None:
        subprocess.run(command, cwd=directory, env=environment, check=True, timeout=timeout)
        return
    completed = subprocess.run(command, cwd=directory, env=environment, check=True,
                               timeout=timeout, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    print(completed.stdout, end="", flush=True)
    if marker not in completed.stdout:
        raise ValueError(f"Consumer did not report {marker}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--packages", type=Path, required=True, help="Directory containing the staged .nupkg files")
    parser.add_argument("--version", required=True, help="Exact TickerQ package version")
    parser.add_argument("--framework", required=True, help="Consumer target framework, e.g. net10.0")
    parser.add_argument("--rid", help="NativeAOT runtime identifier for this runner, e.g. linux-x64")
    parser.add_argument("--aot", action="store_true",
                        help="Also publish and execute the core and dashboard NativeAOT consumers")
    parser.add_argument("--license", type=Path,
                        help="External .tqlicense used to run the licensed scheduler consumer")
    parser.add_argument("--redis-connection",
                        help="Connection string for an isolated Redis used by the Redis NativeAOT consumer")
    parser.add_argument("--ef-sqlite", type=Path,
                        help="Path for an isolated SQLite database used by the managed EF Core consumer")
    parser.add_argument("--mongo-connection",
                        help="Connection string for an isolated MongoDB replica set used by the MongoDB consumer")
    parser.add_argument("--mongo-database",
                        help="Isolated database name used by the MongoDB consumer")
    args = parser.parse_args()
    if not args.packages.is_dir():
        parser.error(f"Package directory does not exist: {args.packages}")
    if not re.fullmatch(r"net(?:8|9|10|11)\.0", args.framework):
        parser.error("--framework must be net8.0, net9.0, net10.0 or net11.0")
    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.-]+)?", args.version):
        parser.error("--version must be an exact three-part NuGet version (optionally prerelease)")
    if args.aot and not args.rid:
        parser.error("--rid is required with --aot and must match the current runner")
    if args.rid and not re.fullmatch(r"[A-Za-z0-9.-]+", args.rid):
        parser.error("Invalid runtime identifier")
    if args.license is not None and not args.license.is_file():
        parser.error("--license must point to an existing external certificate")
    if args.redis_connection is not None and args.license is None:
        parser.error("--redis-connection requires --license")
    if args.ef_sqlite is not None and args.license is None:
        parser.error("--ef-sqlite requires --license")
    if (args.mongo_connection is None) != (args.mongo_database is None):
        parser.error("--mongo-connection and --mongo-database must be supplied together")
    if args.mongo_connection is not None and args.license is None:
        parser.error("--mongo-connection requires --license")

    workspace: Path | None = None
    succeeded = False
    try:
        feed = args.packages.resolve()
        inspect_feed(feed, args.version)
        fixtures = Path(__file__).resolve().parents[1] / "tests/PackageSmoke"
        workspace = Path(tempfile.mkdtemp(prefix="tickerq-package-consumers-"))
        print(f"Isolated consumer workspace: {workspace}", flush=True)
        # Stop directory-property traversal even if a machine has custom files above its temp directory.
        (workspace / "Directory.Build.props").write_text("<Project />\n")
        (workspace / "Directory.Build.targets").write_text("<Project />\n")
        (workspace / "Directory.Packages.props").write_text("<Project />\n")
        config = workspace / "NuGet.Config"
        write_nuget_config(config, feed)
        cache = workspace / "packages"
        environment = dict(os.environ)
        environment.update({"NUGET_PACKAGES": str(cache), "DOTNET_CLI_HOME": str(workspace / "cli"),
                            "NUGET_HTTP_CACHE_PATH": str(workspace / "http-cache"),
                            "DOTNET_NOLOGO": "1", "DOTNET_CLI_TELEMETRY_OPTOUT": "1"})
        if args.license is not None:
            environment["TICKERQ_AOT_LICENSE_PATH"] = str(args.license.resolve())
        if args.redis_connection is not None:
            environment["TICKERQ_AOT_REDIS_CONNECTION"] = args.redis_connection
        if args.ef_sqlite is not None:
            ef_database = args.ef_sqlite.resolve()
            ef_database.parent.mkdir(parents=True, exist_ok=True)
            environment["TICKERQ_EF_SMOKE_DATABASE"] = str(ef_database)
        if args.mongo_connection is not None:
            environment["TICKERQ_MONGO_SMOKE_CONNECTION"] = args.mongo_connection
            environment["TICKERQ_MONGO_SMOKE_DATABASE"] = args.mongo_database
        properties = [f"-p:SmokeVersion={args.version}", f"-p:SmokeFramework={args.framework}"]

        managed_fixtures = ["Core", "DashboardAot", "Optional", "ObservabilityAot"]
        if args.license is not None:
            managed_fixtures.append("SchedulerAot")
        if args.redis_connection is not None:
            managed_fixtures.append("RedisAot")
        if args.ef_sqlite is not None:
            managed_fixtures.append("EfProvider")
        if args.mongo_connection is not None:
            managed_fixtures.append("MongoProvider")
        for fixture in managed_fixtures:
            destination = workspace / fixture
            shutil.copytree(fixtures / fixture, destination, ignore=shutil.ignore_patterns("bin", "obj"))
            project = destination / f"PackageSmoke.{fixture}.csproj"
            if ET.parse(project).find(".//ProjectReference") is not None:
                raise ValueError(f"{project.name} must not contain ProjectReference")
            run(["dotnet", "restore", str(project), "--configfile", str(config), "--no-cache", *properties], workspace, environment)
            audit_assets(project, args.version, cache)
            run(["dotnet", "build", str(project), "--no-restore", "-c", "Release", *properties], workspace, environment)
            assembly = destination / "bin/Release" / args.framework / f"PackageSmoke.{fixture}.dll"
            run(["dotnet", str(assembly)], workspace, environment, timeout=120,
                marker=f"PACKAGE_SMOKE_{fixture.upper()}_OK")

        if args.aot:
            aot_properties = [*properties, "-p:PublishAot=true", "-p:SelfContained=true", "-p:StripSymbols=true"]
            native_fixtures = ["Core", "DashboardAot", "ObservabilityAot"]
            if args.license is not None:
                native_fixtures.append("SchedulerAot")
            if args.redis_connection is not None:
                native_fixtures.append("RedisAot")
            for fixture in native_fixtures:
                project = workspace / fixture / f"PackageSmoke.{fixture}.csproj"
                run(["dotnet", "restore", str(project), "--configfile", str(config), "--no-cache", "-r", args.rid,
                     *aot_properties], workspace, environment)
                audit_assets(project, args.version, cache)
                native_output = workspace / f"native-{fixture.lower()}"
                run(["dotnet", "publish", str(project), "--no-restore", "-c", "Release", "-r", args.rid,
                     "-o", str(native_output), *aot_properties], workspace, environment, timeout=1800)
                name = f"PackageSmoke.{fixture}"
                executable = native_output / (f"{name}.exe" if args.rid.startswith("win-") else name)
                if not executable.is_file() or (native_output / f"{name}.dll").exists():
                    raise ValueError(f"NativeAOT did not produce a standalone {fixture} executable")
                run([str(executable)], workspace, environment, timeout=120,
                    marker=f"PACKAGE_SMOKE_{fixture.upper()}_OK")

        succeeded = True
        print(f"Installed-package consumers passed for {args.version}/{args.framework}" + (f" including NativeAOT {args.rid}." if args.aot else "."))
        return 0
    except (OSError, ValueError, zipfile.BadZipFile, ET.ParseError, subprocess.SubprocessError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        if isinstance(error, subprocess.CalledProcessError) and error.stdout:
            print(error.stdout, file=sys.stderr)
        if workspace is not None:
            print(f"Failed consumer workspace retained at {workspace}", file=sys.stderr)
        return 1
    finally:
        if succeeded and workspace is not None:
            shutil.rmtree(workspace)


if __name__ == "__main__":
    raise SystemExit(main())
