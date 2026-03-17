---
name: release
description: Wait for CI to pass on main, create a GitHub Release, monitor the publish workflow, and report when artifacts are ready.
user_invocable: true
---

# /release

Create a tagged GitHub Release and monitor the full pipeline through to published artifacts.

## Usage

```
/release              — auto-increment patch, confirm with user
/release v0.2.0       — use explicit version
/release v0.2.0 "Release notes here"
```

## Procedure

### 1. Pre-flight checks

- Confirm current branch is `main` and up to date with `origin/main`
- If not on main or not up to date, warn the user and stop

### 2. Resolve version

- If the user provided an explicit tag, use it
- Otherwise, auto-detect the next version:
  1. Get the latest release tag: `gh release view --json tagName -q '.tagName'`
  2. If no releases exist, default to `v0.1.0`
  3. Parse the semver (strip leading `v`), increment the **patch** number (e.g. `0.1.2` -> `v0.1.3`)
  4. Ask the user to confirm: "Next version: **v0.1.3** (patch bump from v0.1.2). Enter to confirm, or type a different version:"
  5. Wait for user confirmation before proceeding

### 3. Wait for CI on main

- Run `gh run list --branch main --workflow ci.yml --limit 1 --json status,conclusion` to get the latest CI run
- If `in_progress`: sleep 30s and re-check, repeating until complete
- If `completed` + `success`: proceed
- If `completed` + `failure`: report the failure and stop — do NOT create a release on a red build

### 4. Create the release

- Run: `gh release create <tag> --target main --title "<tag>" --notes "<notes>"`
- If no notes were provided by the user, generate brief notes from `gh log` since the last tag:
  ```
  gh api repos/{owner}/{repo}/compare/{prev_tag}...main --jq '.commits[].commit.message'
  ```
  Format as a bulleted changelog grouped by conventional commit prefix (feat/fix/docs/chore).

### 5. Monitor the publish workflow

- The publish workflow triggers automatically on the `release` event
- Poll with: `gh run list --workflow publish.yml --limit 1 --json status,conclusion`
- Check immediately, then every 30s until complete
- If `success`: proceed to step 5
- If `failure`: report the failure, link to the run, and stop

### 6. Report

Tell the user:
- Release URL
- That all 5 platform artifacts are attached
- The install/upgrade command: `irm https://raw.githubusercontent.com/MrBildo/collaboard/main/install.ps1 | iex`
