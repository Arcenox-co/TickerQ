#!/usr/bin/env python3
"""Fail closed unless the public licensing portal matches the artifact agreement."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import urllib.request
from pathlib import Path
from typing import Any


DEFAULT_METADATA_URL = "https://license.tickerq.net/api/commercial-terms/current"
DEFAULT_PDF_URL = "https://license.tickerq.net/api/commercial-terms/current/view"


def validate(
    metadata: dict[str, Any],
    pdf_bytes: bytes,
    agreement_bytes: bytes,
    expected_version: str,
    expected_pdf_sha256: str,
) -> list[str]:
    errors: list[str] = []

    if metadata.get("version") != expected_version:
        errors.append(
            f"portal agreement version is {metadata.get('version')!r}; expected {expected_version!r}"
        )

    content = metadata.get("content")
    if not isinstance(content, str):
        errors.append("portal metadata does not contain textual agreement content")
    elif content.encode("utf-8") != agreement_bytes:
        errors.append("portal agreement text does not byte-match LICENSE.md")

    declared_hash = metadata.get("sha256")
    actual_hash = hashlib.sha256(pdf_bytes).hexdigest()
    expected_hash = expected_pdf_sha256.strip().lower()
    if re.fullmatch(r"[0-9a-f]{64}", expected_hash) is None:
        errors.append("source-controlled Version 1.0 PDF SHA-256 is not published")
    elif not isinstance(declared_hash, str) or declared_hash.lower() != expected_hash:
        errors.append(
            f"portal metadata declares PDF SHA-256 {declared_hash!r}; expected {expected_hash}"
        )
    elif actual_hash != expected_hash:
        errors.append(
            f"portal PDF SHA-256 is {actual_hash}; expected {expected_hash}"
        )

    return errors


def fetch(url: str) -> bytes:
    request = urllib.request.Request(url, headers={"User-Agent": "TickerQ-release-policy/1.0"})
    with urllib.request.urlopen(request, timeout=30) as response:
        return response.read()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--agreement", type=Path, required=True)
    parser.add_argument("--expected-version", required=True)
    parser.add_argument("--expected-pdf-sha256-file", type=Path, required=True)
    parser.add_argument("--metadata-url", default=DEFAULT_METADATA_URL)
    parser.add_argument("--pdf-url", default=DEFAULT_PDF_URL)
    args = parser.parse_args()

    try:
        metadata = json.loads(fetch(args.metadata_url))
        pdf_bytes = fetch(args.pdf_url)
        agreement_bytes = args.agreement.read_bytes()
        expected_pdf_sha256 = args.expected_pdf_sha256_file.read_text(encoding="utf-8")
    except (OSError, ValueError, json.JSONDecodeError) as error:
        print(f"Public agreement validation failed: {error}", file=sys.stderr)
        return 1

    errors = validate(
        metadata,
        pdf_bytes,
        agreement_bytes,
        args.expected_version,
        expected_pdf_sha256,
    )
    if errors:
        for error in errors:
            print(f"Public agreement validation failed: {error}", file=sys.stderr)
        return 1

    print(
        f"Validated public agreement {args.expected_version}: "
        f"PDF SHA-256 {hashlib.sha256(pdf_bytes).hexdigest()}."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
