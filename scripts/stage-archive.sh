#!/usr/bin/env bash
# Shared archive STAGING — the single source of truth for what gets stripped
# from, and added to, a Collattice release archive before packaging. Paired with
# verify-archive.sh: stage produces the archive, verify asserts the contract.
# Both the release publish workflow and the PR-CI contract check call this, so
# the exclusion list (the other half of the contract) cannot drift either. (#282)
#
# Usage:
#   stage-archive.sh <publish-dir> <stage-dir> <bin-name> <rid>
#     <publish-dir>  the `dotnet publish` output directory
#     <stage-dir>    where the staged (stripped + augmented) tree is built
#     <bin-name>     apphost binary name (Collabot.Collattice.Api.exe | Collabot.Collattice.Api)
#     <rid>          runtime identifier (win-x64, linux-x64, ...) — drives chmod
#
# Excluded (and why):
#   *.pdb                              debug symbols — not shipped
#   *.xml                              XML doc files (Api/ServiceDefaults) — not shipped
#   appsettings.Development.json       dev-only config — never ships to operators
#   *.staticwebassets.endpoints.json   MapStaticAssets manifest. Collattice serves
#                                      wwwroot via UseStaticFiles + MapFallbackToFile,
#                                      NOT MapStaticAssets, so this manifest is unused
#                                      at runtime. This is the exact SDK-drift file the
#                                      archive contract exists to catch (#221 cite).
#
# The strip uses recursive `find ... -delete` so it shares one matcher with the
# recursive `find .` leaked-file check in verify-archive.sh — the two halves of
# the exclusion contract must agree on what they match.
#
# One deliberate exception: verify-archive.sh also rejects *.map, and this script
# does NOT strip it. Sourcemaps are not publish output we always discard; the CI
# contract job generates them on purpose and removes them from the build output
# itself (extract-bundle-sourcemaps.sh) before anything is copied or staged.
# Stripping them here would silently repair a removal that failed, which is
# exactly the failure the archive check exists to make loud. Rationale in full at
# check (3) in verify-archive.sh.

set -euo pipefail

if [[ $# -ne 4 ]]; then
  echo "usage: $0 <publish-dir> <stage-dir> <bin-name> <rid>" >&2
  exit 2
fi

PUB="$1"
STAGE="$2"
BIN="$3"
RID="$4"

if [[ ! -d "${PUB}" ]]; then
  echo "stage-archive: publish dir '${PUB}' is not a directory." >&2
  exit 2
fi

rm -rf "${STAGE}"
mkdir -p "${STAGE}"

# Start from the full publish output, then strip the excluded set.
cp -R "${PUB}/." "${STAGE}/"

# Recursive strip — mirrors the recursive `find .` matcher in verify-archive.sh
# so both halves of the exclusion contract agree.
find "${STAGE}" -type f \
  \( -name '*.pdb' \
  -o -name '*.xml' \
  -o -name 'appsettings.Development.json' \
  -o -name '*.staticwebassets.endpoints.json' \) \
  -delete

# Bundle INSTALL.md alongside the binary (operator-facing setup guide). The path
# is resolved relative to the repo root, which is the working directory the
# callers (publish.yml, ci.yml) invoke this script from.
cp INSTALL.md "${STAGE}/"

# Bundle the third-party attribution file. The archive redistributes the .NET
# runtime, the NuGet packages the server was built against, and a browser bundle
# compiled from npm packages; a number of those are Apache-2.0 or BSD-licensed and
# ask that recipients of the redistributed binaries also receive the license.
# Attribution that lives only in the source repository never reaches an operator
# who downloads a release, so the file has to travel inside the archive itself --
# verify-archive.sh requires it. (Counts deliberately omitted: THIRD-PARTY-NOTICES.md
# is generated and drift-checked, and a hand-written total in a comment here would
# be the one part of that claim nothing verifies.)
cp THIRD-PARTY-NOTICES.md "${STAGE}/"

# Unix archives must carry the execute bit on the apphost binary.
if [[ "${RID}" != win-x64 ]]; then
  chmod +x "${STAGE}/${BIN}"
fi

echo "Staged top-level contents:"
ls -la "${STAGE}"
