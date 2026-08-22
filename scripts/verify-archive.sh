#!/usr/bin/env bash
# Shared archive-contract verification — the single source of truth for what a
# shipped Collattice release archive must (and must not) contain. Both the
# release publish workflow (.github/workflows/publish.yml) and the PR-CI
# contract check (.github/workflows/ci.yml) call this script, so the contract
# they enforce CANNOT drift from each other. (#282)
#
# The blind spot this closes: publish.yml runs only on `release: published`, so
# its verification first executed at tag-push time. #280 (win-x64 web.config
# tripping the drift check) and #281 (gh upload repo-context) both shipped
# through green PR CI and failed only at release. Extracting the contract here
# lets a PR exercise it.
#
# Usage:
#   verify-archive.sh <verify-dir> <bin-name> <app-base-name>
#     <verify-dir>      directory the archive was extracted into
#     <bin-name>        apphost binary name (Collabot.Collattice.Api.exe | Collabot.Collattice.Api)
#     <app-base-name>   assembly base name (Collabot.Collattice.Api) -> <base>.deps.json etc.
#
# Asserts:
#   (1) flat layout      — no wrapping "<artifact>/" directory at the root
#   (2) required present  — binary, appsettings.json, deps.json, runtimeconfig,
#                           INSTALL.md, THIRD-PARTY-NOTICES.md, wwwroot/index.html
#   (3) no excluded leak  — *.pdb, *.xml, appsettings.Development.json,
#                           *.staticwebassets.endpoints.json, *.map (recursive)
#   (4) no sourcemap ref  — no shipped .js/.css still points at a sourcemap
#                           (a sourceMappingURL comment), even with no .map present
#   (5) no top-level drift — every top-level entry matches a known library class
#                           or the short named set of non-library artifacts
#
# Exit 0 on a clean contract; exit 1 with diagnostics on any violation.

set -euo pipefail

if [[ $# -ne 3 ]]; then
  echo "usage: $0 <verify-dir> <bin-name> <app-base-name>" >&2
  exit 2
fi

VERIFY_DIR="$1"
BIN="$2"
APP_BASE="$3"

if [[ ! -d "${VERIFY_DIR}" ]]; then
  echo "verify-archive: '${VERIFY_DIR}' is not a directory." >&2
  exit 2
fi

echo "Archive top-level listing:"
(cd "${VERIFY_DIR}" && ls -la)

FAILED=0

# (1) Flat-layout assertion: the apphost binary must be at the archive root, not
# nested under a wrapping "<artifact>/" directory. The flat layout is the
# contract the installer scripts (install.sh, install.ps1) and the post-publish
# install-smoke assume. We detect a wrapping directory generically: if the only
# top-level entry is a single directory AND the binary is not at the root, the
# archive wrapped.
if [[ ! -e "${VERIFY_DIR}/${BIN}" ]]; then
  echo "LAYOUT: apphost binary '${BIN}' not found at the archive root." >&2
  echo "Archives must be flat (contract items at root). Installers + install-smoke assume flat layout." >&2
  FAILED=1
fi

# (2) Required items present.
for path in \
  "${BIN}" \
  "appsettings.json" \
  "${APP_BASE}.deps.json" \
  "${APP_BASE}.runtimeconfig.json" \
  "INSTALL.md" \
  "THIRD-PARTY-NOTICES.md"
do
  if [[ ! -f "${VERIFY_DIR}/${path}" ]]; then
    echo "MISSING: ${path}" >&2
    FAILED=1
  fi
done

if [[ ! -f "${VERIFY_DIR}/wwwroot/index.html" ]]; then
  echo "MISSING: wwwroot/index.html" >&2
  FAILED=1
fi

# (3) Excluded patterns must NOT be present anywhere in the archive. This is the
# "fails on EXTRA files" half of the contract — it catches dev/debug artifacts
# and SDK-added-file drift leaking into a shipped archive. Recursive `find .`
# mirrors the recursive strip matcher in stage-archive.sh (both halves of the
# exclusion contract must agree on what they match).
#
# *.map is the deliberate exception to that mirroring, and the asymmetry is the
# point. Every other pattern here is something `dotnet publish` emits that we
# never want, so staging strips it and this check is the backstop. Sourcemaps are
# different: the CI contract job builds the frontend WITH sourcemaps on purpose
# (they are the only ground truth for which npm packages reach the bundle) and
# then moves them out of the build output before anything copies it into wwwroot.
# That removal is the load-bearing step. If staging also stripped *.map, a
# removal that silently matched nothing would be silently repaired here and the
# archive would pass — we would have traded a noisy gate for a quiet one instead
# of for a correct one. Leaving the strip out means a surviving sourcemap fails
# the build and names the real problem. (Sourcemaps expose our source layout and
# original module paths; they are not something to ship by accident.)
LEAKED=$(
  cd "${VERIFY_DIR}"
  find . \
    \( -name '*.pdb' \
    -o -name '*.xml' \
    -o -name 'appsettings.Development.json' \
    -o -name '*.staticwebassets.endpoints.json' \
    -o -name '*.map' \) \
    -type f | sort
)
if [[ -n "${LEAKED}" ]]; then
  echo "EXTRA (excluded artifact leaked into archive):" >&2
  echo "${LEAKED}" >&2
  FAILED=1
fi

# (4) No shipped file may still POINT AT a sourcemap. Check (3) rejects .map
# *files*; this rejects a lingering `sourceMappingURL` *reference* in a shipped
# .js/.css even when the map itself is absent. The two frontend build paths differ
# exactly here, which is why it is worth a cheap "should never" assertion:
#
#   * The PR-CI contract job builds with `--sourcemap hidden` (which writes the
#     .map sidecars but NO sourceMappingURL comment), then moves the sidecars out;
#     extract-bundle-sourcemaps.sh asserts no reference is left behind -- but that
#     assertion runs only on that build.
#   * The release path builds a plain `vite build` (no sourcemaps at all), so there
#     should likewise never be a reference -- and nothing re-checked it, because
#     the reference assertion lived in the CI-only extract step.
#
# verify-archive.sh runs on BOTH paths, so asserting it here backstops the release
# archive too. If a build ever emitted `--sourcemap true` (which writes the
# comment) or otherwise left a reference, a browser would 404 fetching a map that
# was correctly never shipped -- cosmetic, but exactly the unasserted asymmetry the
# archive contract exists to make loud.
#
# grep -rl exits 1 when nothing matches (the good case); a mapfile over a process
# substitution does not propagate that under `set -e`, so an empty result is clean.
declare -a MAP_REFS=()
mapfile -t MAP_REFS < <(
  grep -rl 'sourceMappingURL=' "${VERIFY_DIR}" --include='*.js' --include='*.css' \
    | LC_ALL=C sort
)
if [[ "${#MAP_REFS[@]}" -ne 0 ]]; then
  echo "SOURCEMAP REF (a shipped file points at a sourcemap that is not — and must not be — in the archive):" >&2
  printf '%s\n' "${MAP_REFS[@]}" >&2
  echo "A release build should produce no sourcemaps and no references to them. A" >&2
  echo "shipped file naming a sourceMappingURL means the build emitted one; build" >&2
  echo "with no sourcemap (release) or --sourcemap hidden (the inventory path)." >&2
  FAILED=1
fi

# (5) Top-level allow-list drift check. The exclusion list (3) only rejects
# KNOWN-bad patterns; an exclusion list passes unknown files by default. This
# check restores allow-list-grade detection: every entry at the archive top
# level must match a recognized library class or the short named set of
# non-library artifacts. A genuinely novel SDK-added file (e.g. a future
# *.staticwebassets.runtime.json) matches neither and is reported. The classes
# absorb the ~350 runtime libraries so the list stays tractable without
# enumerating individual DLL names; only the top level drifts (the runtime set
# itself is stable per-RID).
#
# Recognized top-level CLASSES (patterns):
#   *.dll      managed assemblies + Windows native runtime libs
#   *.so       Linux native runtime libs
#   *.dylib    macOS native runtime libs
# Recognized top-level NAMED entries:
#   <bin>                              apphost binary (RID-specific name)
#   createdump / createdump.exe        .NET crash-dump helper (self-contained)
#   appsettings.json                   shipped operator config
#   <base>.deps.json                   dependency manifest (runtime-load-bearing)
#   <base>.runtimeconfig.json          runtime config (runtime-load-bearing)
#   INSTALL.md                         bundled operator setup guide
#   THIRD-PARTY-NOTICES.md             attribution + license texts for every
#                                      third-party component the archive
#                                      redistributes. Required, not merely
#                                      allowed: several bundled components are
#                                      licensed on the condition that recipients
#                                      of the binaries get the license too, and
#                                      an archive without this file does not
#                                      satisfy that.
#   wwwroot                            SPA static assets (directory)
#   web.config                         Windows-only ASP.NET Core hosting config
#                                      (IIS / in-process model — emitted by the
#                                      win-x64 self-contained publish only; the
#                                      other RIDs do not produce it). #280.
UNRECOGNIZED=$(
  cd "${VERIFY_DIR}"
  for entry in *; do
    case "${entry}" in
      *.dll|*.so|*.dylib) ;;
      "${BIN}") ;;
      createdump|createdump.exe) ;;
      appsettings.json) ;;
      "${APP_BASE}.deps.json") ;;
      "${APP_BASE}.runtimeconfig.json") ;;
      INSTALL.md) ;;
      THIRD-PARTY-NOTICES.md) ;;
      wwwroot) ;;
      web.config) ;;
      *) echo "${entry}" ;;
    esac
  done | sort
)
if [[ -n "${UNRECOGNIZED}" ]]; then
  echo "DRIFT (unrecognized top-level entry — not a known library class or named artifact):" >&2
  echo "${UNRECOGNIZED}" >&2
  echo "If this is a legitimate new shipped artifact, add it to the top-level allow-list in this script." >&2
  echo "If it is SDK scratch that must not ship, add it to the exclusion list in stage-archive.sh." >&2
  FAILED=1
fi

if [[ "${FAILED}" -ne 0 ]]; then
  echo "Archive contract verification failed." >&2
  exit 1
fi

echo "Archive contract verified: flat layout, required items present, no excluded artifacts, no top-level drift."
