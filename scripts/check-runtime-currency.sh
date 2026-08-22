#!/usr/bin/env bash
# Checks the .NET runtime a self-contained publish is about to bundle against the
# current .NET servicing release, and fails if we would ship one that is behind.
#
# Why this exists. A self-contained Collattice archive carries the entire .NET
# runtime inside it -- hundreds of libraries, the largest single body of code in
# every archive. So when a runtime security patch ships, an operator does not get
# it from their own machine; they get whatever we packaged. We own that patch
# window for them. Nothing else in the pipeline looks at the bundled runtime for
# currency: the dependency scanners read the packages the project REFERENCES, not
# the runtime pack the publish BUNDLES, so the runtime sits outside every other
# check we run.
#
# The build PINS the runtime (RuntimeFrameworkVersion in Collabot.Collattice.Api.csproj)
# rather than floating to whatever the build SDK ships, so a rebuild from a given
# source revision reproduces the same runtime and a servicing update is a
# deliberate, reviewed change. That makes staying current a choice the team makes
# rather than one the SDK makes for it -- which is why this check exists. It reads
# the runtime the build ACTUALLY produced -- from deps.json, which is ground truth
# about what ships, not the csproj's declaration of intent -- and compares it to
# Microsoft's published latest-runtime for that channel, so the pin cannot fall
# behind a servicing (often security) release without the gate going red.
#
# It is meant to run on a real representative publish in pull-request CI, so a
# green PR proves the whole check -- fetch, parse, compare -- rather than deferring
# it to a release the way a publish-only step would.
#
# Usage:
#   check-runtime-currency.sh <deps-json>
#     <deps-json>  a self-contained publish's dependency manifest
#                  (Collabot.Collattice.Api.deps.json). The bundled runtime pack version is
#                  read from here.
#
# Exit 0 when the bundled runtime is current (equal to, or ahead of, the published
# servicing release). Exit 1 when it is behind, OR when currency could not be
# determined -- the metadata was unreachable or in an unexpected shape. A check
# that cannot reach its source has not verified currency, and treating that as a
# pass would reproduce the exact "nobody checked" defect this exists to prevent.
# The two failures carry different messages so the behind case is never confused
# with the cannot-tell case.

set -euo pipefail

# The release-metadata host is overridable so this can be exercised against a
# local fixture offline; the default is Microsoft's published feed. The channel
# ("10.0") is derived from the bundled runtime version below, so this follows the
# project onto the next major without an edit here.
METADATA_BASE="${DOTNET_RELEASE_METADATA_BASE:-https://builds.dotnet.microsoft.com/dotnet/release-metadata}"

if [[ $# -ne 1 ]]; then
  echo "usage: $0 <deps-json>" >&2
  exit 2
fi

DEPS_JSON="$1"

[[ -f "${DEPS_JSON}" ]] || { echo "check-runtime-currency: '${DEPS_JSON}' is not a file." >&2; exit 2; }
command -v jq >/dev/null 2>&1 || { echo "check-runtime-currency: jq is required." >&2; exit 2; }
command -v curl >/dev/null 2>&1 || { echo "check-runtime-currency: curl is required." >&2; exit 2; }

# The bundled runtime version comes from the base-runtime pack. A self-contained
# publish records two runtime packs -- Microsoft.NETCore.App.Runtime.<rid> and
# Microsoft.AspNetCore.App.Runtime.<rid> -- at the same servicing version, which
# is the version Microsoft's latest-runtime field tracks. Read the NETCore.App one
# specifically: it is the base runtime whose CVEs the servicing release fixes, and
# reading one avoids depending on the two always agreeing.
#
# jq emits nothing (not an error) if no such pack is present, so the emptiness
# guard below is load-bearing: a shape change in deps.json would otherwise leave
# BUNDLED empty and every comparison meaningless.
BUNDLED="$(
  jq -r '.libraries | to_entries[]
         | select(.value.type == "runtimepack")
         | .key
         | select(startswith("runtimepack.Microsoft.NETCore.App.Runtime"))
         | split("/")[1]' "${DEPS_JSON}"
)"

if [[ -z "${BUNDLED}" ]]; then
  echo "check-runtime-currency: no Microsoft.NETCore.App.Runtime runtimepack found in" >&2
  echo "'${DEPS_JSON}'. Either this is not a self-contained publish manifest, or the" >&2
  echo "manifest's runtimepack shape has changed. Currency cannot be determined." >&2
  exit 1
fi

# More than one distinct base-runtime version in one manifest would make "the
# bundled runtime" ambiguous. That should be impossible for a single-RID publish;
# assert it rather than silently comparing whichever line sorted first.
if [[ "$(printf '%s\n' "${BUNDLED}" | LC_ALL=C sort -u | wc -l)" -ne 1 ]]; then
  echo "check-runtime-currency: '${DEPS_JSON}' records more than one distinct" >&2
  echo "Microsoft.NETCore.App.Runtime version:" >&2
  printf '%s\n' "${BUNDLED}" >&2
  exit 1
fi
BUNDLED="$(printf '%s\n' "${BUNDLED}" | LC_ALL=C sort -u)"

if [[ ! "${BUNDLED}" =~ ^[0-9]+\.[0-9]+\.[0-9]+ ]]; then
  echo "check-runtime-currency: bundled runtime version '${BUNDLED}' is not a" >&2
  echo "recognizable X.Y.Z version. Currency cannot be determined." >&2
  exit 1
fi

# The servicing channel is the major.minor of what we bundle (10.0.11 -> 10.0).
CHANNEL="$(printf '%s\n' "${BUNDLED}" | cut -d. -f1,2)"
METADATA_URL="${METADATA_BASE}/${CHANNEL}/releases.json"

# Fetch with retries -- a transient blip on the feed should not fail the gate, but
# a genuine outage must (a check that cannot reach its source has verified
# nothing). --fail turns an HTTP error into a non-zero exit rather than a body of
# error HTML that jq would then misread.
METADATA="$(
  curl --fail --silent --show-error --location \
       --retry 3 --retry-delay 2 --max-time 30 \
       "${METADATA_URL}" 2>/dev/null
)" || {
  echo "check-runtime-currency: could not fetch the .NET ${CHANNEL} release metadata from" >&2
  echo "  ${METADATA_URL}" >&2
  echo "Currency could not be determined, so this is a hard failure rather than a pass:" >&2
  echo "an unreachable source proves nothing about whether the runtime is current." >&2
  exit 1
}

LATEST="$(printf '%s' "${METADATA}" | jq -r '."latest-runtime" // empty')"

if [[ -z "${LATEST}" || ! "${LATEST}" =~ ^[0-9]+\.[0-9]+\.[0-9]+ ]]; then
  echo "check-runtime-currency: the .NET ${CHANNEL} metadata did not carry a usable" >&2
  echo "latest-runtime value (got '${LATEST}'). The feed shape may have changed;" >&2
  echo "currency cannot be determined." >&2
  exit 1
fi

# Order the two versions. mapfile consumes sort to completion, so there is no
# early-exiting consumer for pipefail to trip over (the reason the other scripts
# in this directory avoid `| head`).
declare -a ordered=()
mapfile -t ordered < <(printf '%s\n%s\n' "${BUNDLED}" "${LATEST}" | sort -V)
lowest="${ordered[0]}"

if [[ "${BUNDLED}" == "${LATEST}" ]]; then
  verdict="current"
elif [[ "${lowest}" == "${BUNDLED}" ]]; then
  verdict="behind"
else
  # Bundled sorts newer than the published latest -- the feed occasionally trails a
  # fresh release by a short window. Ahead is not stale, so it is not a failure.
  verdict="ahead"
fi

summary_line=""
case "${verdict}" in
  current)
    summary_line="Runtime currency OK: bundling .NET ${BUNDLED}, the current ${CHANNEL} servicing release." ;;
  ahead)
    summary_line="Runtime currency OK: bundling .NET ${BUNDLED}, ahead of the feed's latest (${LATEST}) -- the metadata is trailing a fresh release." ;;
  behind)
    summary_line="Runtime currency BEHIND: bundling .NET ${BUNDLED}, but ${LATEST} is the current ${CHANNEL} servicing release." ;;
esac

# Surface the verdict where the pre-tag decision can see it: the job log always,
# and the GitHub step summary when running under Actions (far more visible than a
# log line, and the whole point of the card is that this be visible rather than
# incidental).
echo "${summary_line}"
if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then
  {
    echo "### .NET runtime currency"
    echo
    echo "| channel | bundled | current servicing | verdict |"
    echo "| --- | --- | --- | --- |"
    echo "| ${CHANNEL} | ${BUNDLED} | ${LATEST} | ${verdict} |"
    echo
    echo "${summary_line}"
  } >> "${GITHUB_STEP_SUMMARY}"
fi

if [[ "${verdict}" == "behind" ]]; then
  echo >&2
  echo "check-runtime-currency: the self-contained archive would ship a .NET runtime" >&2
  echo "behind the current servicing release, which typically carries security fixes." >&2
  echo "Because the archive bundles the runtime, operators receive whatever we package" >&2
  echo "-- they cannot patch it themselves. Bump the pin to the current release with" >&2
  echo "  scripts/bump-runtime.sh ${LATEST}" >&2
  echo "which rewrites <RuntimeFrameworkVersion> in Collabot.Collattice.Api.csproj and regenerates" >&2
  echo "THIRD-PARTY-NOTICES.md together, then open that one-commit PR." >&2
  exit 1
fi

exit 0
