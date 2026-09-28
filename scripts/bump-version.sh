#!/usr/bin/env bash
#
# Bumps the app version from a single source of truth: the repo-root VERSION file.
#
# Usage: scripts/bump-version.sh <new-version>
#   e.g. scripts/bump-version.sh 0.2.0
#
# It writes VERSION and then propagates the value to the files that cannot read
# VERSION directly at build time:
#   - src/frontend/aircane-web/package.json  ("version")
#   - desktop/package.json                   ("version")
#
# It does NOT need to touch:
#   - desktop/src-tauri/tauri.conf.json  → its "version" points at ../../VERSION
#   - src/backend/Aircane.Api/Aircane.Api.csproj → <Version> reads VERSION at build
#
# A version-bump PR should therefore change VERSION plus the two package.json
# files (all done by this script).
set -euo pipefail

if [ $# -ne 1 ]; then
  echo "Usage: $0 <new-version>   (e.g. $0 0.2.0)" >&2
  exit 1
fi

NEW_VERSION="$1"

# Validate a simple semver-ish MAJOR.MINOR.PATCH shape.
if ! echo "$NEW_VERSION" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z.-]+)?$'; then
  echo "ERROR: '$NEW_VERSION' is not a valid version (expected MAJOR.MINOR.PATCH)." >&2
  exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

echo "$NEW_VERSION" > "$REPO_ROOT/VERSION"
echo "VERSION -> $NEW_VERSION"

# Update a package.json "version" field in place without a JSON tool dependency.
update_package_json() {
  local file="$1"
  if [ -f "$file" ]; then
    # Replace only the top-level "version": "x.y.z" line.
    sed -i.bak -E "s/(\"version\"[[:space:]]*:[[:space:]]*\")[^\"]*(\")/\1$NEW_VERSION\2/" "$file"
    rm -f "$file.bak"
    echo "updated $file"
  fi
}

update_package_json "$REPO_ROOT/src/frontend/aircane-web/package.json"
update_package_json "$REPO_ROOT/desktop/package.json"

echo "Done. Review the diff, then commit VERSION and the package.json changes."
