#!/usr/bin/env bash
# Verify install.sh's fresh-vs-existing install-location detection without
# downloading or touching a real install. Exercises the real --dry-run code path
# against synthetic HOME fixtures: a fresh install must resolve to the Collattice
# location, and an install already present under the earlier name must be detected
# and kept in place so a fresh install never orphans an operator's real database.
set -euo pipefail

INSTALL_SH="${1:-install.sh}"
base="$(mktemp -d)"
trap 'rm -rf "$base"' EXIT
fail=0

field() { sed -n "s/^$1: //p"; }

check() { # label actual expected
    if [ "$2" = "$3" ]; then
        echo "ok   [$1]: $2"
    else
        echo "FAIL [$1]: expected '$3', got '$2'" >&2
        fail=1
    fi
}

# Fresh -- clean HOME
h="$base/fresh"; mkdir -p "$h"
out="$(HOME="$h" bash "$INSTALL_SH" --dry-run)"
check "fresh/kind" "$(printf '%s\n' "$out" | field install-kind)" "fresh"
check "fresh/dir"  "$(printf '%s\n' "$out" | field install-dir)"  "$h/.collattice"
check "fresh/db"   "$(printf '%s\n' "$out" | field db-path)"      "$h/.collattice/data/collattice.db"

# Existing -- old dir carries an appsettings.json marker
h="$base/exist-appsettings"; mkdir -p "$h/.collaboard"; echo '{}' > "$h/.collaboard/appsettings.json"
out="$(HOME="$h" bash "$INSTALL_SH" --dry-run)"
check "exist-appsettings/kind" "$(printf '%s\n' "$out" | field install-kind)" "existing"
check "exist-appsettings/dir"  "$(printf '%s\n' "$out" | field install-dir)"  "$h/.collaboard"
check "exist-appsettings/db"   "$(printf '%s\n' "$out" | field db-path)"      "$h/.collaboard/data/collaboard.db"

# Existing -- old dir carries a data/collaboard.db marker
h="$base/exist-data"; mkdir -p "$h/.collaboard/data"; echo db > "$h/.collaboard/data/collaboard.db"
out="$(HOME="$h" bash "$INSTALL_SH" --dry-run)"
check "exist-data/kind" "$(printf '%s\n' "$out" | field install-kind)" "existing"
check "exist-data/dir"  "$(printf '%s\n' "$out" | field install-dir)"  "$h/.collaboard"

# Empty old dir (no markers) -> fresh
h="$base/emptyold"; mkdir -p "$h/.collaboard"
out="$(HOME="$h" bash "$INSTALL_SH" --dry-run)"
check "emptyold/kind" "$(printf '%s\n' "$out" | field install-kind)" "fresh"
check "emptyold/dir"  "$(printf '%s\n' "$out" | field install-dir)"  "$h/.collattice"

if [ "$fail" -ne 0 ]; then
    echo "install.sh detection: FAILURES" >&2
    exit 1
fi
echo "install.sh detection: all scenarios passed"
