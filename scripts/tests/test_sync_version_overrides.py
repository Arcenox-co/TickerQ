import tempfile
import unittest
from pathlib import Path
from scripts.sync_version_overrides import apply, read_rules, snapshot

ROOT = Path(__file__).resolve().parents[2]

class VersionSyncTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.file = self.root / 'src/Directory.Build.props'
        self.file.parent.mkdir()
        self.source = (ROOT / 'src/Directory.Build.props').read_text(encoding='utf-8-sig')
        self.file.write_text(self.source)
        self.rules = read_rules(ROOT / '.sync-overrides')

    def test_net8_and_net9_preserve_suffix_minor_and_target_abstractions(self):
        for major in (8, 9):
            with self.subTest(major=major):
                self.file.write_text(self.source.replace('[10.0.0,11.0.0)', '[8.0.0,11.0.0)'))
                saved = snapshot(self.root, self.rules)
                self.file.write_text(self.source)
                apply(self.root, self.rules, major, saved)
                result = self.file.read_text()
                self.assertIn(f'<VersionPrefix>{major}.5.0</VersionPrefix>', result)
                self.assertIn('<VersionSuffix>beta</VersionSuffix>', result)
                self.assertIn(f'<TargetFramework>net{major}.0</TargetFramework>', result)
                self.assertIn(f'<DotNetVersion>[{major}.0.0,{major+1}.0.0)</DotNetVersion>', result)
                self.assertIn('<DotNetAbstractionsVersion>[8.0.0,11.0.0)</DotNetAbstractionsVersion>', result)

    def test_missing_property_fails_before_any_file_changes(self):
        saved = snapshot(self.root, self.rules)
        before = self.file.read_text()
        bad = self.rules + [('src/Directory.Build.props', 'Version', 'version-major')]
        with self.assertRaisesRegex(ValueError, 'Version'):
            apply(self.root, bad, 8, saved)
        self.assertEqual(before, self.file.read_text())

    def test_missing_preserved_value_fails_closed(self):
        with self.assertRaises(KeyError):
            apply(self.root, self.rules, 8, {})
        self.assertEqual(self.source, self.file.read_text())

    def test_duplicate_property_and_path_escape_are_rejected(self):
        self.file.write_text(self.source.replace('</Project>', '<PropertyGroup><VersionPrefix>1.0.0</VersionPrefix></PropertyGroup></Project>'))
        with self.assertRaises(ValueError):
            apply(self.root, self.rules, 8, {})
        with self.assertRaises(ValueError):
            apply(self.root, [('../elsewhere', 'Version', 'version-major')], 8, {})

if __name__ == '__main__':
    unittest.main()
