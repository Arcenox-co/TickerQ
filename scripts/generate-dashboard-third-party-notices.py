#!/usr/bin/env python3
"""Generate or verify notices for dependencies bundled into TickerQ.Dashboard."""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from pathlib import Path

REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
LEGAL_FILE_PREFIXES = ("license", "licence", "copying", "notice")
CSS_IMPORT = re.compile(r"@import\s+[\"']([^\"']+)[\"']")
FALLBACK_LICENSES = {
    (
        "@microsoft/signalr",
        "10.0.0",
        "sha512-0BRqz/uCx3JdrOqiqgFhih/+hfTERaUfCZXFB52uMaZJrKaPRzHzMuqVsJC/V3pt7NozcNXGspjKiQEK+X7P2w==",
    ): "licenses/third-party/dotnet-foundation-mit.txt",
    (
        "@radix-ui/number",
        "1.1.1",
        "sha512-MkKCwxlXTgz6CFoJx3pCwn07GKp36+aZyu/u2Ln2VrA5DcdyCZkASEDBTd8x5whTQQL5CiYf4prXKLcgQdv29g==",
    ): "licenses/third-party/radix-primitives-mit.txt",
    (
        "@radix-ui/react-compose-refs",
        "1.1.2",
        "sha512-z4eqJvfiNnFMHIIvXP3CY57y2WJs5g2v3X0zm9mEJkrkNv4rDxu+sg9Jh8EkXyeqBkB7SOcboo9dMVqhyrACIg==",
    ): "licenses/third-party/radix-primitives-mit.txt",
    (
        "@radix-ui/react-context",
        "1.1.2",
        "sha512-jCi/QKUM2r1Ju5a3J64TH2A5SpKAgh0LpknyqdQ4m6DCV0xJ2HG1xARRwNGPQfi1SLdLWZ1OJz6F4OMBBNiGJA==",
    ): "licenses/third-party/radix-primitives-mit.txt",
    (
        "@radix-ui/react-direction",
        "1.1.1",
        "sha512-1UEWRX6jnOA2y4H5WczZ44gOOjTEmlqv1uNW4GAJEO5+bauCBhv8snY65Iw5/VOS/ghKN9gr2KjnLKxrsvoMVw==",
    ): "licenses/third-party/radix-primitives-mit.txt",
    (
        "@radix-ui/react-id",
        "1.1.1",
        "sha512-kGkGegYIdQsOb4XjsfM97rXsiHaBwco+hFI66oO4s9LU+PLAC5oJ7khdOVFxkhsmlbpUqDAvXw11CluXP+jkHg==",
    ): "licenses/third-party/radix-primitives-mit.txt",
    (
        "@radix-ui/react-use-callback-ref",
        "1.1.1",
        "sha512-FkBMwD+qbGQeMu1cOHnuGB6x4yzPjho8ap5WtbEJ26umhgqVXbhekKUQO+hZEL1vU92a3wHwdp0HAcqAUF5iDg==",
    ): "licenses/third-party/radix-primitives-mit.txt",
    (
        "@radix-ui/react-use-escape-keydown",
        "1.1.1",
        "sha512-Il0+boE7w/XebUHyBjroE+DbByORGR9KKmITzbR7MyQ4akpORYP/ZmbhAr0DG7RmmBqoOnZdy2QlvajJ2QA59g==",
    ): "licenses/third-party/radix-primitives-mit.txt",
    (
        "@radix-ui/react-use-layout-effect",
        "1.1.1",
        "sha512-RbJRS4UWQFkzHTTwVymMTUv8EqYhOp8dOOviLj2ugtTiXRaRQS7GLGxZTLL1jWhMeoSCf5zmcZkqTl9IiYfXcQ==",
    ): "licenses/third-party/radix-primitives-mit.txt",
    (
        "@radix-ui/react-use-previous",
        "1.1.1",
        "sha512-2dHfToCj/pzca2Ck724OZ5L0EVrr3eHRNsG/b3xQJLA2hZpVCS99bLAX+hm1IHXDEnzU6by5z/5MIY794/a8NQ==",
    ): "licenses/third-party/radix-primitives-mit.txt",
    (
        "@radix-ui/react-use-size",
        "1.1.1",
        "sha512-ewrXRDTAqAXlkl6t/fkXWNAhFX9I+CkKlw6zjEwk86RSPKwZr3xpBRso655aqYafwtnbpHLj6toFzmd6xdVptQ==",
    ): "licenses/third-party/radix-primitives-mit.txt",
    (
        "react-remove-scroll-bar",
        "2.3.8",
        "sha512-9r+yi9+mgU33AKcj6IbT9oRCO78WriSj6t/cF8DWBZJ9aOGPOTEDvdUDz1FwKim7QXWwmHqtdHnRJfhAxEG46Q==",
    ): "licenses/third-party/react-remove-scroll-bar-mit.txt",
}


def package_directory(source_map: Path, source: str) -> Path | None:
    if "node_modules/" not in source:
        return None
    # Collapse source-map '..' segments before resolving filesystem aliases such
    # as macOS /tmp -> /private/tmp. Resolving the symlink first can create a
    # non-existent /private/private/tmp path.
    source_path = Path(os.path.abspath(source_map.parent / source))
    if not source_path.exists() and "Users/" in source:
        source_path = Path("/" + source[source.index("Users/") :])
    parts = source_path.parts
    indexes = [index for index, part in enumerate(parts) if part == "node_modules"]
    if not indexes:
        return None
    index = indexes[-1]
    length = 2 if parts[index + 1].startswith("@") else 1
    return Path(*parts[: index + 1 + length])


def package_name(specifier: str) -> str:
    parts = specifier.split("/")
    return "/".join(parts[:2]) if specifier.startswith("@") else parts[0]


def bundled_package_directories(
    bundle_dir: Path, node_modules: Path, source_dir: Path
) -> set[Path]:
    package_directories: set[Path] = set()
    source_maps = sorted(bundle_dir.rglob("*.map"))
    if not source_maps:
        raise ValueError(f"no source maps found under {bundle_dir}")

    for source_map in source_maps:
        data = json.loads(source_map.read_text(encoding="utf-8"))
        for source in data.get("sources", []):
            directory = package_directory(source_map, source)
            if directory is not None:
                package_directories.add(directory)

    for css_file in sorted(source_dir.rglob("*.css")):
        for specifier in CSS_IMPORT.findall(css_file.read_text(encoding="utf-8")):
            if not specifier.startswith((".", "/")):
                package_directories.add(node_modules / package_name(specifier))
    return package_directories


def license_files(
    package_name: str, version: str, integrity: str, package_dir: Path
) -> list[tuple[str, str]]:
    files = sorted(
        path
        for path in package_dir.iterdir()
        if path.is_file() and path.name.casefold().startswith(LEGAL_FILE_PREFIXES)
    )
    if files:
        return [(path.name, path.read_text(encoding="utf-8").strip()) for path in files]

    fallback = FALLBACK_LICENSES.get((package_name, version, integrity))
    if fallback is None:
        raise ValueError(f"{package_name}: no local license/notice file and no reviewed fallback")
    path = REPOSITORY_ROOT / fallback
    return [(f"reviewed upstream notice ({fallback})", path.read_text(encoding="utf-8").strip())]


def generate(bundle_dir: Path, node_modules: Path, source_dir: Path, lockfile: Path) -> str:
    lock_packages = json.loads(lockfile.read_text(encoding="utf-8")).get("packages", {})
    packages: dict[tuple[str, str], tuple[str, str, list[tuple[str, str]]]] = {}
    for package_dir in bundled_package_directories(bundle_dir, node_modules, source_dir):
        metadata_path = package_dir / "package.json"
        if not metadata_path.is_file():
            raise ValueError(f"missing package metadata: {metadata_path}")
        metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
        name = metadata.get("name")
        version = metadata.get("version")
        license_expression = metadata.get("license")
        if not all(isinstance(value, str) and value for value in (name, version, license_expression)):
            raise ValueError(f"incomplete name/version/license metadata: {metadata_path}")
        lock_key = package_dir.relative_to(node_modules.parent).as_posix()
        locked = lock_packages.get(lock_key)
        if not isinstance(locked, dict):
            raise ValueError(f"{name}@{version}: missing lockfile entry {lock_key}")
        locked_version = locked.get("version")
        locked_license = locked.get("license")
        resolved = locked.get("resolved")
        integrity = locked.get("integrity")
        if (version, license_expression) != (locked_version, locked_license):
            raise ValueError(
                f"{name}: installed metadata {(version, license_expression)!r} does not match "
                f"lockfile {(locked_version, locked_license)!r}"
            )
        if not all(isinstance(value, str) and value for value in (resolved, integrity)):
            raise ValueError(f"{name}@{version}: lockfile lacks resolved URL or integrity")
        key = (name, version)
        notices = license_files(name, version, integrity, package_dir)
        value = (license_expression, integrity, notices)
        existing = packages.get(key)
        if existing is not None and existing != value:
            raise ValueError(f"conflicting license data for {name}@{version}")
        packages[key] = value

    lines = [
        "# TickerQ Dashboard bundled third-party notices",
        "",
        "This file covers third-party frontend code and fonts embedded in the `TickerQ.Dashboard` distribution.",
        "It is generated deterministically from the production Vite bundle sourcemap and exact npm lockfile metadata.",
        "The notices below do not license TickerQ itself; TickerQ's terms are in `LICENSE.md` and `THIRD-PARTY-NOTICES.md`.",
        "",
        "## Bundled packages",
        "",
    ]
    for (name, version), (license_expression, integrity, _) in sorted(packages.items()):
        lines.append(f"- `{name}@{version}` — `{license_expression}` — `{integrity}`")

    lines.extend(["", "## License and notice texts", ""])
    for (name, version), (license_expression, _, notices) in sorted(packages.items()):
        lines.extend([f"### {name}@{version} ({license_expression})", ""])
        for source, text in notices:
            lines.extend(
                [
                    f"Notice source: `{source}`",
                    "",
                    f"----- BEGIN {name}@{version}: {source} -----",
                    text,
                    f"----- END {name}@{version}: {source} -----",
                    "",
                ]
            )
    return "\n".join(lines).rstrip() + "\n"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--bundle-dir", required=True, type=Path)
    parser.add_argument("--node-modules", required=True, type=Path)
    parser.add_argument("--source-dir", required=True, type=Path)
    parser.add_argument("--lockfile", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()

    try:
        content = generate(
            Path(os.path.abspath(args.bundle_dir)),
            args.node_modules.resolve(),
            args.source_dir.resolve(),
            args.lockfile.resolve(),
        )
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1

    if args.check:
        if not args.output.is_file() or args.output.read_text(encoding="utf-8") != content:
            print(f"ERROR: generated dashboard notices differ from {args.output}", file=sys.stderr)
            return 1
        print(f"Validated generated dashboard notices: {args.output}")
        return 0

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(content, encoding="utf-8")
    print(f"Generated dashboard notices: {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
