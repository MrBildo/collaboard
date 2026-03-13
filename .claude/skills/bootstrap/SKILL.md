---
name: bootstrap
description: Create .agents/ directory structure and local config for fresh clones
user_invocable: true
---

# /bootstrap

Creates the `.agents/` directory structure and `.claude/settings.local.json` for a fresh clone of Collaboard. Run this once after cloning.

## What It Creates

### Directory Structure
```
.agents/
├── roadmap/
│   └── INDEX.md
├── specs/
│   └── TEMPLATE.md
├── kb/
├── research/
├── temp/
├── archive/
│   ├── specs/
│   ├── milestones/
│   └── postmortems/
└── WORKFLOW.md
```

### Local Settings
`.claude/settings.local.json` — permission whitelist for this project.

## Execution Steps

1. **Check for existing `.agents/`** — if it exists, ask before overwriting
2. **Create directories** — all folders in the structure above
3. **Create WORKFLOW.md** — feature planning workflow (see template below)
4. **Create roadmap/INDEX.md** — empty living backlog with section headers
5. **Create specs/TEMPLATE.md** — spec creation template
6. **Create .claude/settings.local.json** — default permission whitelist
7. **Verify .gitignore** — confirm `.agents/` and `.claude/*` patterns exist

## WORKFLOW.md Template

The workflow file should contain:

### Pre-Flight Checklist
1. Spec exists? Check `specs/` first
2. Read the user's words carefully
3. Use active release branch if one exists, else main

### Feature Workflow
1. **Gather context** — check for existing spec, understand the ask
2. **Discuss before speccing** — ask questions about design, scope, data model. Present understanding back with numbered questions. Only write spec after alignment.
3. **Write spec** — use `[[specs/TEMPLATE]]`, self-contained, includes test plan
4. **Implement** — database/API first, then frontend
5. **Test** — run test suite, manual verification
6. **PR** — feature branch, conventional commit messages, squash merge

### Between-Milestone Cleanup
1. Run `/agents-tidy`
2. Delete test artifacts
3. Write handoff doc to `archive/milestones/`
4. Review memory
5. `git status` check

## roadmap/INDEX.md Template

```markdown
# Collaboard Roadmap

Living backlog — source of truth for what's next.

## Active

_Nothing in progress._

## Backlog

<populated from current known backlog>

## Ideas

<captured but uncommitted items>

## Decisions

<key architectural decisions with dates>
```

## specs/TEMPLATE.md Template

```markdown
---
source: <Trello card # or "Ad-hoc">
status: Draft | Ready | In Progress | Complete
created: <date>
updated: <date>
branch: feature/<slug>
---

# <Feature Name>

## Summary
1-2 paragraph description of the feature.

## API Changes
New or modified endpoints with request/response shapes.

## Database Changes
New or modified entities, indexes, migrations needed.

## Frontend Changes
New components, routes, API hooks, state changes.

## Acceptance Criteria
Specific, testable criteria (numbered list).

## Test Plan
### Prerequisites
- Environment setup, seed data needed

### Scenarios
1. **<Scenario name>** — Steps and expected results

### Edge Cases
- <edge case descriptions>
```

## settings.local.json Template

```json
{
  "permissions": {
    "allow": [
      "WebFetch(domain:github.com)",
      "WebSearch",
      "WebFetch(domain:www.nuget.org)",
      "WebFetch(domain:api.github.com)"
    ]
  }
}
```
