#!/usr/bin/env bash
# Derives the bundled-third-party-component inventory that THIRD-PARTY-NOTICES.md
# publishes, from the two artifacts that actually determine what a release archive
# contains:
#
#   * the server's `dotnet publish` dependency manifest (Collaboard.Api.deps.json)
#   * the browser bundle's sourcemaps (which npm packages contributed code to the
#     JavaScript Vite emitted into wwwroot)
#
# Both are GROUND TRUTH about what ships, not declarations of intent. That
# distinction is the whole point: frontend/package.json declares 32 runtime
# dependencies whose transitive closure is 355 packages, but only 191 of them
# survive tree-shaking into the bundle, and at least one declared dependency
# (@fontsource-variable/geist) is never imported and never ships. A notices file
# built from the declaration would claim to describe what recipients receive
# while describing something else.
#
# Usage:
#   generate-third-party-notices.sh <deps-json> <frontend-dir> <sourcemap-dir>
#       Emit the inventory block to stdout.
#
#   generate-third-party-notices.sh --check <notices-file> \
#       <deps-json> <frontend-dir> <sourcemap-dir>
#       Regenerate and diff against the block delimited by the BEGIN/END markers
#       inside <notices-file>. Exit 1 with the delta when they disagree.
#
# The --check mode is what keeps the file honest. The defect this whole surface
# exists to fix was a dependency arriving with nobody noticing; a static notices
# file with no drift gate reproduces that defect on the next dependency change.
#
# This script never guesses a license. A component whose license cannot be read
# from its own manifest, and which has no entry in the override table below,
# fails the run loudly rather than shipping an inferred answer.

set -euo pipefail

BEGIN_MARKER='<!-- BEGIN GENERATED INVENTORY -->'
END_MARKER='<!-- END GENERATED INVENTORY -->'

CHECK_FILE=""
if [[ "${1:-}" == "--check" ]]; then
  CHECK_FILE="${2:-}"
  shift 2
  if [[ -z "${CHECK_FILE}" ]]; then
    echo "generate-third-party-notices: --check requires a notices file path." >&2
    exit 2
  fi
fi

if [[ $# -ne 3 ]]; then
  echo "usage: $0 [--check <notices-file>] <deps-json> <frontend-dir> <sourcemap-dir>" >&2
  exit 2
fi

DEPS_JSON="$1"
FRONTEND_DIR="$2"
SOURCEMAP_DIR="$3"

[[ -f "${DEPS_JSON}" ]] || { echo "generate-third-party-notices: '${DEPS_JSON}' is not a file." >&2; exit 2; }
for path in "${FRONTEND_DIR}" "${SOURCEMAP_DIR}" ; do
  [[ -d "${path}" ]] || { echo "generate-third-party-notices: '${path}' is not a directory." >&2; exit 2; }
done

command -v jq >/dev/null 2>&1 || { echo "generate-third-party-notices: jq is required." >&2; exit 2; }

NUGET_ROOT="${NUGET_PACKAGES:-${HOME}/.nuget/packages}"

# Components whose own manifest does not state a copyright holder, resolved from
# a sibling artifact in the same distribution rather than from memory. Each entry
# records where the value was read from, so the resolution is auditable and a
# future reviewer can re-check it against the same source.
#
#   @radix-ui/*  -- the seven internal helper packages ship no LICENSE file of
#                   their own. Read from the sibling packages in the same
#                   published set that do carry one (@radix-ui/react-slot/LICENSE
#                   and radix-ui/LICENSE), which are identical.
override_copyright() {
  case "$1" in
    @radix-ui/number|@radix-ui/react-compose-refs|@radix-ui/react-context|\
@radix-ui/react-direction|@radix-ui/react-use-layout-effect|\
@radix-ui/react-use-previous|@radix-ui/react-use-size)
      echo "Copyright (c) 2022 WorkOS" ;;
    *) echo "" ;;
  esac
}

# Components whose package manifest omits a license field, resolved from the
# license file the package does ship. Same auditability rule as above.
#
#   khroma -- package.json carries no "license" key; the package ships a
#             `license` file whose body is the MIT text.
override_license() {
  case "$1" in
    khroma) echo "MIT" ;;
    *) echo "" ;;
  esac
}

# Extract the first copyright line from a license file. Leading comment or list
# punctuation is tolerated -- axios, for one, writes its line as "# Copyright ...".
first_copyright_line() {
  local file="$1"
  [[ -f "${file}" ]] || return 0
  sed -n 's/^[[:space:]*#>-]*\(Copyright[^\r]*\)/\1/p' "${file}" \
    | head -n 1 \
    | sed 's/[[:space:]]*$//'
}

fail_unresolved() {
  echo "generate-third-party-notices: cannot resolve ${1} for '${2}'." >&2
  echo "Read it from the component's own distribution and add an entry to the" >&2
  echo "override table in this script, recording where the value came from." >&2
  exit 1
}

emit_inventory() {
  # ---------------------------------------------------------------- .NET runtime
  # A self-contained publish carries the entire .NET runtime and the ASP.NET Core
  # shared framework as loose assemblies -- roughly 313 managed libraries and 15
  # native ones per archive. deps.json records them as two runtimepack entries
  # rather than as packages, so they are read from there and asserted to be the
  # Microsoft-published runtime packs; an unrecognized runtimepack fails the run
  # instead of being folded silently into the Microsoft line.
  echo "### .NET runtime and shared framework"
  echo
  echo "Redistributed in full by the self-contained publish."
  echo

  local rp_count=0
  while IFS=$'\t' read -r id version; do
    case "${id}" in
      runtimepack.Microsoft.NETCore.App.Runtime*)
        echo "- \`Microsoft.NETCore.App.Runtime\` ${version} — MIT — Copyright (c) .NET Foundation and Contributors" ;;
      runtimepack.Microsoft.AspNetCore.App.Runtime*)
        echo "- \`Microsoft.AspNetCore.App.Runtime\` ${version} — MIT — Copyright (c) .NET Foundation and Contributors" ;;
      *)
        fail_unresolved "runtime pack identity" "${id}" ;;
    esac
    rp_count=$((rp_count + 1))
  done < <(
    jq -r '.libraries | to_entries[]
           | select(.value.type == "runtimepack")
           | (.key | split("/")) as $p
           | "\($p[0])\t\($p[1])"' "${DEPS_JSON}" \
      | sed 's/\.\(win\|linux\|osx\)-[a-z0-9]*\t/\t/' \
      | sort -u
  )
  if [[ "${rp_count}" -eq 0 ]]; then
    fail_unresolved "runtime packs (none found)" "${DEPS_JSON}"
  fi

  # ------------------------------------------------------------- NuGet packages
  echo
  echo "### NuGet packages (server)"
  echo
  echo "Shipped as managed assemblies alongside the executable."
  echo

  while IFS=$'\t' read -r id version; do
    local lower nuspec license copyright
    lower="$(echo "${id}" | tr '[:upper:]' '[:lower:]')"
    nuspec="${NUGET_ROOT}/${lower}/${version}/${lower}.nuspec"
    [[ -f "${nuspec}" ]] || fail_unresolved "nuspec (${nuspec})" "${id}"

    license="$(sed -n 's/.*<license type="expression">\([^<]*\)<\/license>.*/\1/p' "${nuspec}" | head -n 1)"
    if [[ -z "${license}" ]]; then
      license="$(sed -n 's/.*<licenseUrl>\([^<]*\)<\/licenseUrl>.*/\1/p' "${nuspec}" | head -n 1)"
    fi
    [[ -n "${license}" ]] || license="$(override_license "${id}")"
    [[ -n "${license}" ]] || fail_unresolved "license" "${id}"

    # Not every package states a copyright holder. Apache-2.0 and the MIT family
    # both ask that notices PRESENT in the work be retained, so where a package
    # states none there is none to reproduce -- but silently emitting a blank
    # would read as an oversight. Fall back to the manifest's declared authors,
    # labelled as authorship rather than as a copyright claim we invented.
    copyright="$(sed -n 's/.*<copyright>\([^<]*\)<\/copyright>.*/\1/p' "${nuspec}" | head -n 1)"
    [[ -n "${copyright}" ]] || copyright="$(override_copyright "${id}")"
    if [[ -z "${copyright}" ]]; then
      local authors
      authors="$(sed -n 's/.*<authors>\([^<]*\)<\/authors>.*/\1/p' "${nuspec}" | head -n 1)"
      [[ -n "${authors}" ]] || fail_unresolved "copyright holder or authors" "${id}"
      copyright="states no copyright notice; authored by ${authors}"
    fi

    echo "- \`${id}\` ${version} — ${license} — ${copyright}"
  done < <(
    jq -r '.libraries | to_entries[]
           | select(.value.type == "package")
           | (.key | split("/")) as $p
           | "\($p[0])\t\($p[1])"' "${DEPS_JSON}" \
      | LC_ALL=C sort -f
  )

  # --------------------------------------------------------- native third-party
  # Native libraries that arrive from a package rather than from the runtime pack.
  # The runtime pack's own native libraries are covered by the .NET runtime entry
  # above; this section is for everything else, and today that is exactly one
  # library whose file name is the only thing that varies across RIDs.
  echo
  echo "### Native libraries (server)"
  echo

  local native_rows
  native_rows="$(
    jq -r '.targets | to_entries[-1].value | to_entries[]
           | select(.key | startswith("runtimepack") | not)
           | select(.value.native != null)
           | .key as $k | .value.native | keys[]
           | "\($k)\t\(.)"' "${DEPS_JSON}"
  )"
  if [[ -z "${native_rows}" ]]; then
    echo "_None beyond the .NET runtime's own native libraries._"
  else
    while IFS=$'\t' read -r lib asset; do
      # The file name is platform-specific (libe_sqlite3.so / .dylib /
      # e_sqlite3.dll) while the component is not. Normalize to the platform-
      # neutral library name so this inventory is identical for every RID --
      # one archive's notices file would otherwise disagree with another's, and
      # the drift check would fail purely on which platform CI happened to build.
      local libname
      libname="$(basename "${asset}")"
      libname="${libname%.*}"
      libname="${libname#lib}"

      case "${lib}" in
        SQLitePCLRaw.lib.e_sqlite3/*)
          echo "- \`${libname}\` — a compiled build of **SQLite**, which its authors have released"
          echo "  into the **public domain**: no copyright is claimed and no license conditions"
          echo "  attach. Shipped as \`lib${libname}.so\` on Linux, \`lib${libname}.dylib\` on macOS"
          echo "  and \`${libname}.dll\` on Windows. The build is produced and distributed by"
          echo "  \`SQLitePCLRaw.lib.e_sqlite3\`, listed under Apache-2.0 above." ;;
        *)
          fail_unresolved "provenance of native asset '${asset}'" "${lib}" ;;
      esac
    done <<< "${native_rows}"
  fi

  # ----------------------------------------------------- npm packages in bundle
  # Derived from the sourcemaps of the JavaScript Vite emitted, so the list is
  # what the browser actually receives rather than what package.json declares.
  echo
  echo "### npm packages (browser bundle)"
  echo
  echo "Compiled into the JavaScript served from \`wwwroot\`."
  echo

  # A package can appear at more than one version in one bundle: npm nests a
  # second copy under a dependent when versions conflict, and Vite bundles
  # whichever copy the import resolved to. Resolving the manifest by name alone
  # would read the hoisted top-level copy and report a version that is not the
  # one shipped -- `parse5`, for instance, is bundled at both 8.0.0 (hoisted) and
  # 7.3.0 (nested under hast-util-raw). So the package DIRECTORY is carried
  # through from the sourcemap path, and each distinct directory is listed.
  #
  # Sourcemap sources are paths relative to the emitted chunk, always of the form
  # `../../node_modules/...`; stripping the leading `../` segments yields a path
  # relative to the frontend directory. The package directory then runs up to and
  # including the segment(s) after the LAST `node_modules/` -- two segments for a
  # scoped name, one otherwise.
  local pkg_list
  pkg_list="$(
    find "${SOURCEMAP_DIR}" -name '*.map' -type f -print0 \
      | xargs -0 -r jq -r '.sources[]?' \
      | sed 's|^\(\.\./\)*||' \
      | awk '
          {
            marker = "node_modules/"
            pos = 0; last = 0
            while ((p = index(substr($0, pos + 1), marker)) > 0) {
              pos = pos + p
              last = pos
            }
            if (last == 0) next

            rest = substr($0, last + length(marker))
            split(rest, seg, "/")
            name = (seg[1] ~ /^@/) ? seg[1] "/" seg[2] : seg[1]
            if (name == "" || (seg[1] ~ /^@/ && seg[2] == "")) next

            print name "\t" substr($0, 1, last - 1) marker name
          }' \
      | LC_ALL=C sort -u -t$'\t' -k1,1 -k2,2
  )"
  [[ -n "${pkg_list}" ]] || fail_unresolved "any bundled npm package (no sourcemaps?)" "${SOURCEMAP_DIR}"

  while IFS=$'\t' read -r name reldir; do
    [[ -n "${name}" ]] || continue
    local dir manifest license copyright licfile
    dir="${FRONTEND_DIR}/${reldir}"
    manifest="${dir}/package.json"
    [[ -f "${manifest}" ]] || fail_unresolved "package manifest" "${name}"

    license="$(jq -r 'if (.license | type) == "string" then .license
                      elif .license.type then .license.type
                      elif .licenses then ([.licenses[].type] | join(" OR "))
                      else "" end' "${manifest}")"
    [[ -n "${license}" && "${license}" != "null" ]] || license="$(override_license "${name}")"
    [[ -n "${license}" ]] || fail_unresolved "license" "${name}"

    licfile="$(find "${dir}" -maxdepth 1 -type f \
      \( -iname 'LICENSE' -o -iname 'LICENSE.*' -o -iname 'LICENCE' -o -iname 'LICENCE.*' -o -iname 'COPYING' -o -iname 'COPYING.*' \) \
      | LC_ALL=C sort | head -n 1)"
    copyright="$(first_copyright_line "${licfile}")"
    [[ -n "${copyright}" ]] || copyright="$(override_copyright "${name}")"
    [[ -n "${copyright}" ]] || fail_unresolved "copyright holder" "${name}"

    local version
    version="$(jq -r '.version' "${manifest}")"
    echo "- \`${name}\` ${version} — ${license} — ${copyright}"
  done <<< "${pkg_list}"

  # ---------------------------------------------------------------- CSS toolchain
  # Sourcemaps cannot see these: they contribute generated CSS to the stylesheet
  # rather than JavaScript modules to the bundle, so no module path names them.
  # They are read from styles.css's own @import/@plugin directives, which are the
  # complete set of stylesheet inputs.
  echo
  echo "### CSS toolchain (browser bundle)"
  echo
  echo "Emit generated CSS into the shipped stylesheet."
  echo

  while read -r name; do
    [[ -n "${name}" ]] || continue
    local manifest license copyright licfile
    manifest="${FRONTEND_DIR}/node_modules/${name}/package.json"
    [[ -f "${manifest}" ]] || fail_unresolved "package manifest" "${name}"
    license="$(jq -r 'if (.license | type) == "string" then .license else .license.type // "" end' "${manifest}")"
    [[ -n "${license}" && "${license}" != "null" ]] || fail_unresolved "license" "${name}"
    licfile="$(find "${FRONTEND_DIR}/node_modules/${name}" -maxdepth 1 -type f \
      \( -iname 'LICENSE' -o -iname 'LICENSE.*' \) | LC_ALL=C sort | head -n 1)"
    copyright="$(first_copyright_line "${licfile}")"
    [[ -n "${copyright}" ]] || fail_unresolved "copyright holder" "${name}"
    echo "- \`${name}\` $(jq -r '.version' "${manifest}") — ${license} — ${copyright}"
  done < <(
    sed -n "s/^@import '\([^']*\)'.*/\1/p; s/^@plugin '\([^']*\)'.*/\1/p" \
      "${FRONTEND_DIR}/src/styles.css" | LC_ALL=C sort -u
  )
}

if [[ -z "${CHECK_FILE}" ]]; then
  emit_inventory
  exit 0
fi

# --check: compare the regenerated inventory against the one the file publishes.
[[ -f "${CHECK_FILE}" ]] || { echo "generate-third-party-notices: '${CHECK_FILE}' is not a file." >&2; exit 2; }

TMP_DIR="$(mktemp -d)"
trap 'rm -rf "${TMP_DIR}"' EXIT

emit_inventory > "${TMP_DIR}/expected"

awk -v b="${BEGIN_MARKER}" -v e="${END_MARKER}" '
  $0 == b { inside = 1; next }
  $0 == e { inside = 0; next }
  inside  { print }
' "${CHECK_FILE}" > "${TMP_DIR}/actual"

if [[ ! -s "${TMP_DIR}/actual" ]]; then
  echo "NOTICES: no generated inventory block found in '${CHECK_FILE}'." >&2
  echo "Expected content between ${BEGIN_MARKER} and ${END_MARKER}." >&2
  exit 1
fi

# Both sides are emitted with a trailing blank line boundary; compare ignoring
# leading/trailing blank lines so the marker spacing in the document is free.
strip_blank_edges() {
  sed -e '/./,$!d' "$1" | sed -e :a -e '/^\n*$/{$d;N;};/\n$/ba'
}
strip_blank_edges "${TMP_DIR}/expected" > "${TMP_DIR}/expected.trim"
strip_blank_edges "${TMP_DIR}/actual"   > "${TMP_DIR}/actual.trim"

if diff -u "${TMP_DIR}/actual.trim" "${TMP_DIR}/expected.trim" > "${TMP_DIR}/delta"; then
  echo "Third-party notices verified: the published inventory matches what the build actually bundles."
  exit 0
fi

echo "NOTICES DRIFT: the component inventory in '${CHECK_FILE}' no longer matches what the build bundles." >&2
echo "'-' lines are published but no longer bundled; '+' lines are bundled but not published." >&2
echo >&2
cat "${TMP_DIR}/delta" >&2
echo >&2
echo "Regenerate with:" >&2
echo "  scripts/generate-third-party-notices.sh <deps-json> frontend <sourcemap-dir>" >&2
echo "and replace the block between the BEGIN/END markers in ${CHECK_FILE}." >&2
echo "If a newly bundled component carries a license not already reproduced in that" >&2
echo "file, add its license text too — the inventory and the license texts are one" >&2
echo "deliverable, and a listed license with no text does not discharge the obligation." >&2
exit 1
