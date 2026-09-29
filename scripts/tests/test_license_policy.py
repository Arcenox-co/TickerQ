from __future__ import annotations

import importlib.util
import json
import tempfile
import unittest
import zipfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
VALIDATOR_PATH = ROOT / "scripts" / "validate-package-licenses.py"


def load_validator():
    spec = importlib.util.spec_from_file_location("validate_package_licenses", VALIDATOR_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot load {VALIDATOR_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class LicensePolicyTests(unittest.TestCase):
    def test_every_nuget_package_is_commercial(self) -> None:
        validator = load_validator()

        self.assertIn("TickerQ.MongoDB", validator.COMMERCIAL_PACKAGE_IDS)
        self.assertEqual(validator.COMMERCIAL_PACKAGE_IDS, validator.EXPECTED_PACKAGE_IDS)
        self.assertFalse(hasattr(validator, "OSS_PACKAGE_IDS"))

    def test_current_repository_uses_commercial_license_and_scoped_notices(self) -> None:
        commercial = ROOT / "LICENSE.md"
        licensing = ROOT / "LICENSING.md"
        notices = ROOT / "THIRD-PARTY-NOTICES.md"

        self.assertTrue(commercial.read_text(encoding="utf-8").startswith("TICKERQ\nSOFTWARE LICENSE AGREEMENT"))
        self.assertTrue(licensing.is_file())
        self.assertTrue(notices.is_file())
        self.assertFalse((ROOT / "licenses" / "OPEN-SOURCE.md").exists())

    def test_node_package_designates_commercial_terms(self) -> None:
        package_dir = ROOT / "hub" / "sdks" / "node"
        package = json.loads((package_dir / "package.json").read_text(encoding="utf-8"))

        self.assertEqual("SEE LICENSE IN LICENSE.md", package["license"])
        self.assertIn("LICENSE.md", package["files"])
        self.assertIn("THIRD-PARTY-NOTICES.md", package["files"])
        self.assertNotIn("LICENSE", package["files"])
        self.assertFalse((package_dir / "LICENSE").exists())
        self.assertEqual(
            (ROOT / "LICENSE.md").read_bytes(),
            (package_dir / "LICENSE.md").read_bytes(),
        )
        self.assertEqual(
            (ROOT / "THIRD-PARTY-NOTICES.md").read_bytes(),
            (package_dir / "THIRD-PARTY-NOTICES.md").read_bytes(),
        )

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
            )

        self.assertTrue(any("requireLicenseAcceptance" in error for error in errors), errors)
        self.assertTrue(any("stale OSS license file" in error for error in errors), errors)


if __name__ == "__main__":
    unittest.main()
