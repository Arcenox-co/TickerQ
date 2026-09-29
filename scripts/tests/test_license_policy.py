from __future__ import annotations

import importlib.util
import hashlib
import io
import json
import sys
import tarfile
import tempfile
import unittest
from unittest import mock
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
VALIDATOR_PATH = ROOT / "scripts" / "validate-package-licenses.py"
NODE_VALIDATOR_PATH = ROOT / "scripts" / "validate-node-package.py"
DASHBOARD_NOTICE_GENERATOR_PATH = ROOT / "scripts" / "generate-dashboard-third-party-notices.py"
PUBLIC_TERMS_VALIDATOR_PATH = ROOT / "scripts" / "validate-public-terms.py"
NUGET_COMPARER_PATH = ROOT / "scripts" / "compare-nuget-packages.py"


def load_validator():
    spec = importlib.util.spec_from_file_location("validate_package_licenses", VALIDATOR_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot load {VALIDATOR_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def load_node_validator():
    spec = importlib.util.spec_from_file_location("validate_node_package", NODE_VALIDATOR_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot load {NODE_VALIDATOR_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def load_dashboard_notice_generator():
    spec = importlib.util.spec_from_file_location(
        "generate_dashboard_third_party_notices", DASHBOARD_NOTICE_GENERATOR_PATH
    )
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot load {DASHBOARD_NOTICE_GENERATOR_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def load_public_terms_validator():
    spec = importlib.util.spec_from_file_location(
        "validate_public_terms", PUBLIC_TERMS_VALIDATOR_PATH
    )
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot load {PUBLIC_TERMS_VALIDATOR_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def load_nuget_comparer():
    spec = importlib.util.spec_from_file_location("compare_nuget_packages", NUGET_COMPARER_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot load {NUGET_COMPARER_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def add_tar_bytes(archive: tarfile.TarFile, name: str, content: bytes) -> None:
    info = tarfile.TarInfo(name)
    info.size = len(content)
    archive.addfile(info, io.BytesIO(content))


class LicensePolicyTests(unittest.TestCase):
    def test_nuget_registry_comparison_ignores_only_repository_signature(self) -> None:
        comparer = load_nuget_comparer()
        with tempfile.TemporaryDirectory() as directory:
            local = Path(directory) / "local.nupkg"
            registry = Path(directory) / "registry.nupkg"
            for path, payload in ((local, b"assembly"), (registry, b"assembly")):
                with zipfile.ZipFile(path, "w") as archive:
                    archive.writestr("Package.nuspec", b"metadata")
                    archive.writestr("lib/net10.0/Package.dll", payload)
                    if path == registry:
                        archive.writestr(".signature.p7s", b"nuget.org signature")

            self.assertEqual([], comparer.compare(local, registry))

            with zipfile.ZipFile(registry, "w") as archive:
                archive.writestr("Package.nuspec", b"metadata")
                archive.writestr("lib/net10.0/Package.dll", b"different")
                archive.writestr(".signature.p7s", b"nuget.org signature")

            self.assertTrue(
                any("differs" in error for error in comparer.compare(local, registry))
            )

    def test_publication_workflow_uses_authenticated_producer_artifacts(self) -> None:
        build_workflow = (ROOT / ".github" / "workflows" / "build.yml").read_text()
        publish_workflow = (ROOT / ".github" / "workflows" / "publish.yml").read_text()

        self.assertIn("name: npm-package", build_workflow)
        self.assertIn("npm ci --include=dev", build_workflow)
        self.assertIn("workflow_run:", publish_workflow)
        self.assertNotIn("workflow_dispatch:", publish_workflow)
        self.assertIn("github.event.workflow_run.id", publish_workflow)
        self.assertIn(".head_repository.full_name", publish_workflow)
        self.assertIn("environment: production-publish", publish_workflow)
        self.assertIn("--name npm-package", publish_workflow)
        self.assertIn("compare-nuget-packages.py", publish_workflow)
        self.assertIn("unsafe or invalid NuGet version", publish_workflow)
        self.assertIn("npm_archive = npm_archive_input.resolve()", publish_workflow)
        self.assertIn("--registry https://registry.npmjs.org", publish_workflow)
        self.assertIn("continue-on-error: true", build_workflow)
        self.assertIn("path: ./nupkgs/*.nupkg", build_workflow)
        self.assertNotIn("*.snupkg", build_workflow)
        self.assertNotIn("symbol_package=", publish_workflow)
        self.assertNotIn("--version '${{ steps.versions.outputs", publish_workflow)
        self.assertNotIn("python3 release-source/scripts/", publish_workflow)
        self.assertNotIn("--skip-duplicate", publish_workflow)
        self.assertNotIn("all_exist", build_workflow)
        self.assertNotIn("artifact_id:", publish_workflow)

    def test_package_ownership_metadata_has_one_authoritative_source(self) -> None:
        root_props = ET.parse(ROOT / "Directory.Build.props").getroot()
        source_props = ET.parse(ROOT / "src" / "Directory.Build.props").getroot()

        self.assertEqual("Arcenox LLC", root_props.findtext(".//Authors"))
        self.assertEqual(
            "Copyright 2025-present Arcenox LLC and contributors",
            root_props.findtext(".//Copyright"),
        )
        self.assertEqual("false", source_props.findtext(".//IncludeSymbols"))
        self.assertIsNone(source_props.find(".//Authors"))
        self.assertIsNone(source_props.find(".//Copyright"))

    def test_every_nuget_package_is_commercial(self) -> None:
        validator = load_validator()

        self.assertIn("TickerQ.MongoDB", validator.COMMERCIAL_PACKAGE_IDS)
        self.assertEqual(validator.COMMERCIAL_PACKAGE_IDS, validator.EXPECTED_PACKAGE_IDS)
        self.assertEqual("Arcenox,Alex Brown", validator.PACKAGE_AUTHORS["TickerQ.MongoDB"])
        self.assertFalse(hasattr(validator, "OSS_PACKAGE_IDS"))

    def test_current_repository_uses_commercial_license_and_scoped_notices(self) -> None:
        commercial = ROOT / "LICENSE.md"
        licensing = ROOT / "LICENSING.md"
        notices = ROOT / "THIRD-PARTY-NOTICES.md"
        dashboard_notices = ROOT / "src" / "TickerQ.Dashboard" / "DASHBOARD-THIRD-PARTY-NOTICES.md"

        self.assertTrue(commercial.read_text(encoding="utf-8").startswith("TICKERQ\nSOFTWARE LICENSE AGREEMENT"))
        license_text = commercial.read_text(encoding="utf-8")
        self.assertIn("Agreement version Version 1.1", license_text)
        self.assertIn("TickerQ.MongoDB MongoDB persistence provider", license_text)
        self.assertIn("@tickerq/sdk TickerQ Hub JavaScript/TypeScript client", license_text)
        self.assertIn("first immutable version 1.0.0 artifact", license_text)
        self.assertTrue(licensing.is_file())
        self.assertIn("Publication gate", licensing.read_text(encoding="utf-8"))
        self.assertTrue(notices.is_file())
        notice_text = notices.read_text(encoding="utf-8")
        for contributor in (
            "Alex Brown",
            "Ingmar Olmaru",
            "Steve Wojciechowski",
            "Rafael Câmara",
            "Daniel Nordström",
            "Christoph Seelbach",
            "Dawit",
            "Cédric Hulin",
            "jods",
            "Valiantsin Leushyts",
            "PuFGGs",
        ):
            self.assertIn(contributor, notice_text)
        self.assertIn("`@tickerq/sdk` package contains no directly attributed", notice_text)
        self.assertTrue(dashboard_notices.is_file())
        self.assertIn(
            "DASHBOARD-THIRD-PARTY-NOTICES.md",
            (ROOT / "src" / "TickerQ.Dashboard" / "TickerQ.Dashboard.csproj").read_text(),
        )
        self.assertFalse((ROOT / "licenses" / "OPEN-SOURCE.md").exists())
        self.assertEqual(
            "PENDING_PUBLICATION",
            (ROOT / "licenses" / "public-master-v1.1.sha256").read_text().strip(),
        )

    def test_node_package_designates_commercial_terms(self) -> None:
        package_dir = ROOT / "hub" / "sdks" / "node"
        package = json.loads((package_dir / "package.json").read_text(encoding="utf-8"))

        self.assertEqual("SEE LICENSE IN LICENSE.md", package["license"])
        self.assertEqual("hub/sdks/node", package["repository"]["directory"])
        self.assertIn("LICENSE.md", package["files"])
        self.assertIn("THIRD-PARTY-NOTICES.md", package["files"])
        self.assertNotIn("LICENSE", package["files"])
        self.assertFalse((package_dir / "LICENSE").exists())
        self.assertTrue((package_dir / "package-lock.json").is_file())
        self.assertIn("verify:public-terms", package["scripts"]["prepublishOnly"])
        self.assertEqual(
            (ROOT / "LICENSE.md").read_bytes(),
            (package_dir / "LICENSE.md").read_bytes(),
        )
        self.assertEqual(
            (ROOT / "THIRD-PARTY-NOTICES.md").read_bytes(),
            (package_dir / "THIRD-PARTY-NOTICES.md").read_bytes(),
        )

    def test_node_artifact_validator_rejects_stale_license_file(self) -> None:
        validator = load_node_validator()
        metadata = {
            "name": "@tickerq/sdk",
            "version": "1.0.0",
            "license": "SEE LICENSE IN LICENSE.md",
            "repository": {
                "type": "git",
                "url": "https://github.com/Arcenox-co/TickerQ",
                "directory": "hub/sdks/node",
            },
            "publishConfig": {"registry": "https://example.invalid/custom/"},
        }
        with tempfile.TemporaryDirectory() as directory:
            package = Path(directory) / "tickerq-sdk-1.0.0.tgz"
            with tarfile.open(package, "w:gz") as archive:
                add_tar_bytes(archive, "package/package.json", json.dumps(metadata).encode())
                add_tar_bytes(archive, "package/LICENSE.md", b"commercial")
                add_tar_bytes(archive, "package/THIRD-PARTY-NOTICES.md", b"notices")
                add_tar_bytes(archive, "package/LICENSE", b"stale")

            errors = validator.validate(package, "1.0.0", b"commercial", b"notices")

        self.assertTrue(any("stale license file" in error for error in errors), errors)
        self.assertTrue(any("package/dist/index.js" in error for error in errors), errors)
        self.assertTrue(any("publishConfig is forbidden" in error for error in errors), errors)

    def test_node_artifact_validator_rejects_duplicate_paths(self) -> None:
        validator = load_node_validator()
        with tempfile.TemporaryDirectory() as directory:
            package = Path(directory) / "tickerq-sdk-1.0.0.tgz"
            with tarfile.open(package, "w:gz") as archive:
                add_tar_bytes(archive, "package/LICENSE.md", b"first")
                add_tar_bytes(archive, "package/LICENSE.md", b"second")

            errors = validator.validate(package, "1.0.0", b"commercial", b"notices")

        self.assertTrue(any("duplicate paths" in error for error in errors), errors)

    def test_node_artifact_validator_rejects_case_colliding_paths(self) -> None:
        validator = load_node_validator()
        metadata = {
            "name": "@tickerq/sdk",
            "version": "1.0.0",
            "license": "SEE LICENSE IN LICENSE.md",
            "repository": {
                "url": "https://github.com/Arcenox-co/TickerQ",
                "directory": "hub/sdks/node",
            },
        }
        with tempfile.TemporaryDirectory() as directory:
            package = Path(directory) / "tickerq-sdk-1.0.0.tgz"
            with tarfile.open(package, "w:gz") as archive:
                add_tar_bytes(archive, "package/package.json", json.dumps(metadata).encode())
                add_tar_bytes(archive, "package/LICENSE.md", b"commercial")
                add_tar_bytes(archive, "package/license.md", b"ambiguous")
                add_tar_bytes(archive, "package/THIRD-PARTY-NOTICES.md", b"notices")

            errors = validator.validate(package, "1.0.0", b"commercial", b"notices")

        self.assertTrue(
            any("case-insensitive path collisions" in error for error in errors), errors
        )

    def test_node_artifact_validator_rejects_missing_declared_entrypoint(self) -> None:
        validator = load_node_validator()
        metadata = {
            "name": "@tickerq/sdk",
            "version": "1.0.0",
            "main": "dist/missing.js",
            "module": "dist/index.js",
            "types": "dist/index.d.ts",
            "typings": "dist/missing-legacy.d.ts",
            "bin": {"tickerq": "dist/missing-cli.js"},
            "exports": {".": {"default": "./dist/also-missing.js"}},
            "license": "SEE LICENSE IN LICENSE.md",
            "repository": {
                "url": "https://github.com/Arcenox-co/TickerQ",
                "directory": "hub/sdks/node",
            },
        }
        with tempfile.TemporaryDirectory() as directory:
            package = Path(directory) / "tickerq-sdk-1.0.0.tgz"
            with tarfile.open(package, "w:gz") as archive:
                add_tar_bytes(archive, "package/package.json", json.dumps(metadata).encode())
                add_tar_bytes(archive, "package/README.md", b"readme")
                add_tar_bytes(archive, "package/dist/index.js", b"module.exports = {}")
                add_tar_bytes(archive, "package/dist/index.d.ts", b"export {}")
                add_tar_bytes(archive, "package/LICENSE.md", b"commercial")
                add_tar_bytes(archive, "package/THIRD-PARTY-NOTICES.md", b"notices")

            errors = validator.validate(package, "1.0.0", b"commercial", b"notices")

        self.assertTrue(
            any("package/dist/missing.js" in error for error in errors), errors
        )
        self.assertTrue(
            any("package/dist/also-missing.js" in error for error in errors), errors
        )
        self.assertTrue(
            any("package/dist/missing-legacy.d.ts" in error for error in errors), errors
        )
        self.assertTrue(
            any("package/dist/missing-cli.js" in error for error in errors), errors
        )

    def test_node_artifact_validator_rejects_precommercial_version(self) -> None:
        validator = load_node_validator()
        metadata = {
            "name": "@tickerq/sdk",
            "version": "0.9.0",
            "license": "SEE LICENSE IN LICENSE.md",
            "repository": {
                "url": "https://github.com/Arcenox-co/TickerQ",
                "directory": "hub/sdks/node",
            },
        }
        with tempfile.TemporaryDirectory() as directory:
            package = Path(directory) / "tickerq-sdk-0.9.0.tgz"
            with tarfile.open(package, "w:gz") as archive:
                add_tar_bytes(archive, "package/package.json", json.dumps(metadata).encode())
                add_tar_bytes(archive, "package/README.md", b"readme")
                add_tar_bytes(archive, "package/dist/index.js", b"module.exports = {}")
                add_tar_bytes(archive, "package/dist/index.d.ts", b"export {}")
                add_tar_bytes(archive, "package/LICENSE.md", b"commercial")
                add_tar_bytes(archive, "package/THIRD-PARTY-NOTICES.md", b"notices")

            errors = validator.validate(package, "0.9.0", b"commercial", b"notices")

        self.assertTrue(any("precedes commercial" in error for error in errors), errors)

    def test_dashboard_notice_paths_normalize_before_resolving_aliases(self) -> None:
        generator = load_dashboard_notice_generator()
        source_map = Path("/tmp/output/assets/index.js.map")
        source = "../../../../private/tmp/project/node_modules/example/index.js"

        self.assertEqual(
            Path("/private/tmp/project/node_modules/example"),
            generator.package_directory(source_map, source),
        )

    def test_dashboard_notice_cli_preserves_lexical_bundle_path(self) -> None:
        generator = load_dashboard_notice_generator()
        bundle = Path("/tmp/tickerq-alias/bundle")
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "notices.md"
            argv = [
                str(DASHBOARD_NOTICE_GENERATOR_PATH),
                "--bundle-dir",
                str(bundle),
                "--node-modules",
                str(ROOT / "src/TickerQ.Dashboard/wwwroot/node_modules"),
                "--source-dir",
                str(ROOT / "src/TickerQ.Dashboard/wwwroot/src"),
                "--lockfile",
                str(ROOT / "src/TickerQ.Dashboard/wwwroot/package-lock.json"),
                "--output",
                str(output),
            ]
            with mock.patch.object(sys, "argv", argv), mock.patch.object(
                generator, "generate", return_value="notices\n"
            ) as generate:
                self.assertEqual(0, generator.main())

        self.assertEqual(bundle, generate.call_args.args[0])

    def test_dashboard_notice_fallback_requires_exact_package_integrity(self) -> None:
        generator = load_dashboard_notice_generator()
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(ValueError, "no local license/notice file"):
                generator.license_files(
                    "@microsoft/signalr",
                    "10.0.0",
                    "sha512-not-the-reviewed-package",
                    Path(directory),
                )

    def test_public_terms_validator_fails_closed_on_portal_drift(self) -> None:
        validator = load_public_terms_validator()
        agreement = b"agreement-v1.1"
        pdf = b"pdf-v1.1"
        expected_hash = hashlib.sha256(pdf).hexdigest()
        metadata = {
            "version": "public-master-v1.1",
            "content": agreement.decode(),
            "sha256": expected_hash,
        }

        self.assertEqual(
            [],
            validator.validate(
                metadata, pdf, agreement, "public-master-v1.1", expected_hash
            ),
        )
        self.assertTrue(
            any("expected" in error for error in validator.validate(
                {**metadata, "version": "public-master-v1.0"},
                pdf,
                agreement,
                "public-master-v1.1",
                expected_hash,
            ))
        )
        self.assertTrue(
            any("byte-match" in error for error in validator.validate(
                {**metadata, "content": "different"},
                pdf,
                agreement,
                "public-master-v1.1",
                expected_hash,
            ))
        )
        self.assertTrue(
            any("SHA-256" in error for error in validator.validate(
                metadata,
                b"different-pdf",
                agreement,
                "public-master-v1.1",
                expected_hash,
            ))
        )
        self.assertTrue(
            any("not published" in error for error in validator.validate(
                metadata,
                pdf,
                agreement,
                "public-master-v1.1",
                "PENDING_PUBLICATION",
            ))
        )

    def test_validator_rejects_duplicate_zip_members(self) -> None:
        validator = load_validator()
        with tempfile.TemporaryDirectory() as directory:
            package = Path(directory) / "duplicate.nupkg"
            with zipfile.ZipFile(package, "w") as archive:
                archive.writestr("TickerQ.nuspec", b"first")
                archive.writestr("TickerQ.nuspec", b"second")

            with self.assertRaisesRegex(ValueError, "duplicate paths"):
                validator._read_package(package)

    def test_validator_rejects_case_colliding_zip_members(self) -> None:
        validator = load_validator()
        with tempfile.TemporaryDirectory() as directory:
            package = Path(directory) / "collision.nupkg"
            with zipfile.ZipFile(package, "w") as archive:
                archive.writestr("TickerQ.nuspec", b"first")
                archive.writestr("tickerq.NUSPEC", b"second")

            with self.assertRaisesRegex(ValueError, "case-insensitive path collisions"):
                validator._read_package(package)

    def test_validator_rejects_stale_oss_file_and_missing_acceptance(self) -> None:
        validator = load_validator()
        nuspec = """<?xml version="1.0"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>TickerQ</id>
    <version>10.5.0-beta</version>
    <license type="file">LICENSE.md</license>
    <repository type="git" url="https://github.com/arcenox-co/TickerQ" />
  </metadata>
</package>
"""
        with tempfile.TemporaryDirectory() as directory:
            package = Path(directory) / "TickerQ.10.5.0-beta.nupkg"
            with zipfile.ZipFile(package, "w") as archive:
                archive.writestr("TickerQ.nuspec", nuspec)
                archive.writestr("LICENSE.md", b"commercial")
                archive.writestr("THIRD-PARTY-NOTICES.md", b"notices")
                archive.writestr("OPEN-SOURCE.md", b"stale")

            errors = validator.validate(
                Path(directory),
                "10.5.0-beta",
                b"commercial",
                b"notices",
                b"dashboard notices",
            )

        self.assertTrue(any("requireLicenseAcceptance" in error for error in errors), errors)
        self.assertTrue(any("stale OSS license file" in error for error in errors), errors)

    def test_validator_rejects_mixed_internal_dependency_versions(self) -> None:
        validator = load_validator()
        nuspec = """<?xml version="1.0"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>TickerQ</id>
    <version>10.5.0-beta</version>
    <license type="file">LICENSE.md</license>
    <requireLicenseAcceptance>true</requireLicenseAcceptance>
    <repository type="git" url="https://github.com/arcenox-co/TickerQ" />
    <dependencies>
      <group targetFramework="net10.0">
        <dependency id="tickerq.utilities" version="9.9.9" />
      </group>
    </dependencies>
  </metadata>
</package>
"""
        with tempfile.TemporaryDirectory() as directory:
            package = Path(directory) / "TickerQ.10.5.0-beta.nupkg"
            with zipfile.ZipFile(package, "w") as archive:
                archive.writestr("TickerQ.nuspec", nuspec)
                archive.writestr("LICENSE.md", b"commercial")
                archive.writestr("THIRD-PARTY-NOTICES.md", b"notices")

            errors = validator.validate(
                Path(directory),
                "10.5.0-beta",
                b"commercial",
                b"notices",
                b"dashboard notices",
            )

        self.assertTrue(
            any("tickerq.utilities dependency version '9.9.9'" in error for error in errors),
            errors,
        )

    def test_validator_rejects_unexpected_artifact_entries(self) -> None:
        validator = load_validator()
        with tempfile.TemporaryDirectory() as directory:
            (Path(directory) / "unexpected.snupkg").write_bytes(b"unexpected")
            errors = validator.validate(
                Path(directory),
                "10.5.0-beta",
                b"commercial",
                b"notices",
                b"dashboard notices",
            )

        self.assertTrue(
            any("unexpected entries: unexpected.snupkg" in error for error in errors), errors
        )

    def test_validator_rejects_unrecognized_commercial_version_family(self) -> None:
        validator = load_validator()

        with tempfile.TemporaryDirectory() as directory:
            errors = validator.validate(
                Path(directory), "1.5.0", b"commercial", b"notices", b"dashboard notices"
            )

        self.assertTrue(any("commercial version family" in error for error in errors), errors)


if __name__ == "__main__":
    unittest.main()
