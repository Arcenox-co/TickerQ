"""Apply required .NET branch overrides without silently skipping missing properties."""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET


def read_rules(path: Path) -> list[tuple[str, str, str]]:
    rules = []
    for raw in path.read_text().splitlines():
        line = raw.split('#', 1)[0].strip()
        if not line:
            continue
        parts = tuple(part.strip() for part in line.split('|'))
        if len(parts) != 3 or parts[2] not in {'version-major', 'framework', 'version-range', 'preserve'}:
            raise ValueError(f'Invalid override rule: {line}')
        rules.append(parts)
    if not rules:
        raise ValueError('No version override rules were found')
    return rules


def read_property(root: Path, file: str, element: str) -> tuple[Path, str, str]:
    path = (root / file).resolve()
    if not path.is_relative_to(root.resolve()):
        raise ValueError(f'Override path escapes the repository: {file}')
    text = path.read_text(encoding='utf-8-sig')
    matches = list(ET.fromstring(text).iter(element))
    if len(matches) != 1 or not matches[0].text:
        raise ValueError(f'{file}: expected exactly one nonempty <{element}> property')
    return path, text, matches[0].text


def snapshot(root: Path, rules: list[tuple[str, str, str]]) -> dict[str, str]:
    return {f'{file}|{element}': read_property(root, file, element)[2]
            for file, element, action in rules if action == 'preserve'}


def apply(root: Path, rules: list[tuple[str, str, str]], version: int, preserved: dict[str, str]) -> list[str]:
    if version not in (8, 9, 10):
        raise ValueError(f'Unsupported .NET target: {version}')
    pending: dict[Path, str] = {}
    details = []
    for file, element, action in rules:
        path, original, value = read_property(root, file, element)
        text = pending.get(path, original)
        if action == 'version-major':
            if not re.fullmatch(r'\d+\.\d+\.\d+(?:[-+][A-Za-z0-9.+-]+)?', value):
                raise ValueError(f'{file}: invalid version {value!r}')
            new = re.sub(r'^\d+', str(version), value)
        elif action == 'framework':
            new = f'net{version}.0'
        elif action == 'version-range':
            new = f'[{version}.0.0,{version + 1}.0.0)'
        else:
            new = preserved[f'{file}|{element}']
        pattern = rf'(<{re.escape(element)}>){re.escape(value)}(</{re.escape(element)}>)'
        text, count = re.subn(pattern, lambda m: m[1] + new + m[2], text)
        if count != 1:
            raise ValueError(f'{file}: could not safely replace <{element}>')
        pending[path] = text
        details.append(f'{file}|{element}|{value} -> {new}')
    # Validate all rules before writing any files.
    for path, text in pending.items():
        path.write_text(text, encoding='utf-8')
    return details


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path.cwd())
    parser.add_argument('--rules', type=Path, required=True)
    parser.add_argument('--preserved', type=Path, required=True)
    parser.add_argument('--snapshot', action='store_true')
    parser.add_argument('--version', type=int)
    args = parser.parse_args()
    try:
        rules = read_rules(args.rules)
        if args.snapshot:
            args.preserved.write_text(json.dumps(snapshot(args.root, rules)))
        else:
            for detail in apply(args.root, rules, args.version, json.loads(args.preserved.read_text())):
                print(detail)
    except (ValueError, KeyError, OSError, ET.ParseError) as exc:
        parser.exit(1, f'Version sync failed: {exc}\n')
