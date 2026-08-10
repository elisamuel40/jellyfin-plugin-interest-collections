#!/usr/bin/env python3
"""Adds a released version to manifest.json, the file Jellyfin reads to offer the plugin.

The manifest is a list of plugins; each carries a list of versions, newest first. Jellyfin
verifies the download against the MD5 checksum recorded here, so the checksum must be the one
computed from the exact zip attached to the release.
"""

import argparse
import json
import pathlib
import sys
from datetime import datetime, timezone

GUID = "5f9a1c74-3d0e-4c1b-9f2a-7b6d8e0a4c31"
TARGET_ABI = "10.11.0.0"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", required=True, help="four-part version, e.g. 0.1.0.0")
    parser.add_argument("--checksum", required=True, help="MD5 of the release zip")
    parser.add_argument("--repo", required=True, help="owner/name of the GitHub repository")
    parser.add_argument("--tag", required=True, help="release tag, e.g. v0.1.0")
    parser.add_argument(
        "--manifest",
        default=str(pathlib.Path(__file__).resolve().parent.parent / "manifest.json"),
    )
    args = parser.parse_args()

    manifest_path = pathlib.Path(args.manifest)
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))

    plugin = next((entry for entry in manifest if entry.get("guid") == GUID), None)
    if plugin is None:
        print(f"No plugin with guid {GUID} in {manifest_path}", file=sys.stderr)
        return 1

    versions = plugin.setdefault("versions", [])
    short_version = args.tag.lstrip("v")

    entry = {
        "version": args.version,
        "changelog": f"See the release notes at https://github.com/{args.repo}/releases/tag/{args.tag}",
        "targetAbi": TARGET_ABI,
        "sourceUrl": (
            f"https://github.com/{args.repo}/releases/download/{args.tag}/"
            f"interest-collections-{short_version}.zip"
        ),
        "checksum": args.checksum,
        "timestamp": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    }

    # Republishing the same version replaces it rather than adding a duplicate.
    versions[:] = [existing for existing in versions if existing.get("version") != args.version]
    versions.insert(0, entry)

    manifest_path.write_text(json.dumps(manifest, indent=4) + "\n", encoding="utf-8")
    print(f"Recorded {args.version} in {manifest_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
