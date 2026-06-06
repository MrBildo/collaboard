#!/usr/bin/env bash
# Lint: every `gh release upload` invocation in a workflow file must carry
# --repo. (#281 / #282)
#
# The #281 failure: the aggregate-checksums job has no actions/checkout, so gh
# could not infer owner/repo from git context and failed with `fatal: not a git
# repository`. `--repo ${{ github.repository }}` makes the upload independent of
# implicit git context. This invariant only ever bit at release time (the
# upload steps run only on `release: published`), so a PR could not catch a
# regression. This lint is the PR-runnable cover for that class.
#
# Usage: lint-gh-upload-repo.sh <workflow-file>
#
# Mechanism: shell commands continue across lines with a trailing backslash.
# Join continued lines first, then for every joined line containing
# `gh release upload`, assert `--repo` is present in the SAME (joined)
# invocation. Fail loud, listing each offending invocation.

set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: $0 <workflow-file>" >&2
  exit 2
fi

FILE="$1"

if [[ ! -f "${FILE}" ]]; then
  echo "lint-gh-upload-repo: '${FILE}' not found." >&2
  exit 2
fi

# Join backslash-continued lines into single logical lines, then collapse runs
# of whitespace so the search sees each full invocation as one string.
JOINED=$(sed ':a;/\\$/{N;s/\\\n//;ba}' "${FILE}" | tr -s '[:blank:]' ' ')

FOUND=0
MISSING=0

while IFS= read -r line; do
  # Match only a real INVOCATION: after trimming leading whitespace, the line
  # must START with `gh release upload`. This excludes both comments (`# ...gh
  # release upload...`) and YAML scalars that merely mention the command (a
  # `- name: Lint gh release upload ...` step). We lint the command, not prose
  # about it.
  trimmed="${line#"${line%%[![:blank:]]*}"}"
  case "${trimmed}" in
    "gh release upload"*)
      FOUND=$((FOUND + 1))
      case "${trimmed}" in
        *"--repo"*) ;;
        *)
          MISSING=$((MISSING + 1))
          echo "MISSING --repo on gh release upload:" >&2
          echo "  ${trimmed}" >&2
          ;;
      esac
      ;;
  esac
done <<< "${JOINED}"

if [[ "${FOUND}" -eq 0 ]]; then
  echo "lint-gh-upload-repo: no 'gh release upload' invocation found in ${FILE}." >&2
  echo "Expected at least one (the release path uploads assets). Did the file move?" >&2
  exit 1
fi

if [[ "${MISSING}" -ne 0 ]]; then
  echo "lint-gh-upload-repo: ${MISSING} of ${FOUND} gh release upload calls lack --repo." >&2
  echo "Every gh release upload must pass --repo so it does not depend on implicit git context (#281)." >&2
  exit 1
fi

echo "lint-gh-upload-repo: ${FOUND} gh release upload call(s) verified — all carry --repo."
