#!/usr/bin/env bash
# Assert that the sourcemap "audit" build is the same bundle as the one that
# ships.
#
# THIRD-PARTY-NOTICES.md derives its browser-bundle inventory from the sourcemaps
# of a second Vite build, because the shipped build emits none. That is only
# sound while the two builds compile the same modules. Nothing enforced it: a
# future change to Vite config, a mode-conditional plugin, or a `--sourcemap`
# side effect could quietly make them diverge, and the notices file would go on
# describing a bundle with confidence -- just not the bundle anyone receives.
# A precise description of the wrong artifact is worse than a vague one, because
# nothing about it looks wrong.
#
# The comparison is content, not file name, and it normalizes two things first.
#
#   1. Enabling sourcemaps appends a `sourceMappingURL` trailer to each asset,
#      which changes the bytes and therefore the content hash Vite writes into
#      the file name.
#
#   2. Emitted chunk names contain a content hash, and the entry chunk embeds the
#      names of the chunks it lazily imports -- which in turn embed the entry's
#      name. Rollup settles that circular dependency by iterating to a fixed
#      point, and the fixed point is NOT stable run to run: two consecutive
#      builds of an unchanged tree can differ in every chunk that participates,
#      identical in size and in code, differing only in the hash tokens they
#      quote at each other. Measured on this repo, not assumed.
#
#      A gate that compared raw bytes would therefore go red at random on a
#      perfectly good pull request -- and a check that cries wolf is one people
#      learn to re-run until it passes, which is worse than not having it. So
#      every emitted asset name is rewritten to its hash-free identity inside the
#      content before hashing. What survives normalization is the code, which is
#      the thing the inventory is a claim about.
#
# Usage:
#   verify-bundle-equivalence.sh <shipped-dist-dir> <audit-dist-dir>

set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "usage: $0 <shipped-dist-dir> <audit-dist-dir>" >&2
  exit 2
fi

SHIPPED="$1"
AUDIT="$2"

for path in "${SHIPPED}" "${AUDIT}"; do
  [[ -d "${path}" ]] || { echo "verify-bundle-equivalence: '${path}' is not a directory." >&2; exit 2; }
done

WORK="$(mktemp -d)"
trap 'rm -rf "${WORK}"' EXIT

# Vite names emitted assets `<name>-<contenthash>.<ext>`. Drop the hash so the
# same logical chunk pairs up across two builds whose bytes differ only by the
# trailer. A name that does not carry a hash is left alone; two files collapsing
# onto one identity is caught below rather than silently resolved.
asset_identity() {
  local file="$1"
  echo "${file}" | sed -E 's/-[A-Za-z0-9_-]{8}\.(js|css)$/.\1/'
}

# A sed program that rewrites every asset name emitted by one build into its
# hash-free identity, so cross-chunk references stop carrying a hash that means
# nothing to the comparison. Built per build, because each has its own names.
build_normalizer() {
  local root="$1" program="$2" file base

  : > "${program}"

  while IFS= read -r file; do
    base="$(basename "${file}")"

    # `.` is the only regex metacharacter Vite puts in an emitted asset name.
    printf 's|%s|%s|g\n' \
      "${base//./\\.}" \
      "$(asset_identity "${base}")" \
      >> "${program}"
  done < <(
    find "${root}" -type f \( -name '*.js' -o -name '*.css' \) \
      | LC_ALL=C sort
  )
}

# name<TAB>hash, one row per emitted JavaScript or stylesheet asset. The content
# is stripped of the sourcemap trailer and normalized before hashing.
index_assets() {
  local root="$1" out="$2" program="$3" identity hash file
  : > "${out}"

  while IFS= read -r file; do
    identity="$(asset_identity "$(basename "${file}")")"
    hash="$(
      sed -e 's|/\*# sourceMappingURL=[^*]*\*/||g' \
          -e '/^\/\/# sourceMappingURL=/d' \
          "${file}" \
        | sed -f "${program}" \
        | sha256sum \
        | cut -c1-64
    )"
    printf '%s\t%s\n' "${identity}" "${hash}" >> "${out}"
  done < <(
    find "${root}" -type f \( -name '*.js' -o -name '*.css' \) \
      | LC_ALL=C sort
  )

  LC_ALL=C sort -o "${out}" "${out}"
}

build_normalizer "${SHIPPED}" "${WORK}/shipped.sed"
build_normalizer "${AUDIT}" "${WORK}/audit.sed"

index_assets "${SHIPPED}" "${WORK}/shipped" "${WORK}/shipped.sed"
index_assets "${AUDIT}" "${WORK}/audit" "${WORK}/audit.sed"

if [[ ! -s "${WORK}/shipped" || ! -s "${WORK}/audit" ]]; then
  echo "BUNDLE EQUIVALENCE: found no .js or .css assets in one of the builds" >&2
  echo "('${SHIPPED}': $(wc -l < "${WORK}/shipped") assets, '${AUDIT}': $(wc -l < "${WORK}/audit") assets)." >&2
  echo "A comparison over an empty set would pass while proving nothing." >&2
  exit 1
fi

for side in shipped audit; do
  # `uniq -d` reads its input to the end, so this stays clear of the early-exit
  # consumer that `| grep -q .` would introduce under `set -o pipefail`.
  collisions="$(cut -f1 "${WORK}/${side}" | LC_ALL=C uniq -d)"

  if [[ -n "${collisions}" ]]; then
    echo "BUNDLE EQUIVALENCE: two assets in the ${side} build reduce to the same identity" >&2
    echo "once the content hash is stripped, so they cannot be paired reliably:" >&2
    echo "${collisions}" >&2
    exit 1
  fi
done

if diff -u "${WORK}/shipped" "${WORK}/audit" > "${WORK}/delta"; then
  echo "Bundle equivalence verified: the sourcemap build the notices inventory is derived from"
  echo "is byte-identical to the shipped build across all $(wc -l < "${WORK}/shipped") emitted assets."
  exit 0
fi

echo "BUNDLE EQUIVALENCE: the sourcemap build and the shipped build are not the same bundle." >&2
echo "THIRD-PARTY-NOTICES.md derives its browser-bundle inventory from the sourcemap build," >&2
echo "so any difference here means the published inventory describes something other than" >&2
echo "what recipients receive. '-' is the shipped build, '+' is the audit build." >&2
echo >&2
cat "${WORK}/delta" >&2
exit 1
