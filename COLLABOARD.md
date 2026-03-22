# Collaboard — Collaboard

Collaboard development is tracked on its own production instance.

**Board slug:** `collaboard`

## Lanes

| Lane | Purpose |
|------|---------|
| **Backlog** | Prioritized, ready to pick up |
| **Triage** | New items land here, need sizing/discussion |
| **Ready** | Sized, scoped, and approved — agents can pick these up |
| **In Progress** | Actively being worked on |
| **Review** | PR open, awaiting user review |
| **Done** | Merged to main |
| **Archived** | Shipped and closed. Archived cards are frozen — no edits, comments, labels, or attachment changes. |

## Labels

Labels are board-scoped and align with conventional commit prefixes.

### Type

| Label | Color | Meaning |
|-------|-------|---------|
| `Feature` | green | `feat:` commits |
| `Bug` | orange-red | `fix:` commits |
| `Improvement` | blue | `refactor:` / minor enhancements |
| `Chore` | gray | CI, deps, tooling |
| `Docs` | teal | Documentation |
| `Infrastructure` | dark gray | Build, deploy, CI infrastructure |
| `Investigation` | yellow | Research-driven work |
| `Discussion` | purple | Needs conversation before action |

### Status (transient)

| Label | Color | Meaning |
|-------|-------|---------|
| `Blocked` | red | Can't proceed, external dependency |

## Sizes

| Size | Ordinal |
|------|---------|
| S | 0 |
| M | 1 |
| L | 2 |
| XL | 3 |

Sizes represent effort, not urgency.

## Workflow

1. New items → **Triage** with a type label (`Bug`, `Feature`, etc.)
2. Size (S/M/L/XL), prioritize → **Backlog**
3. User approves for work → **Ready** (agents should only pick up cards from Ready)
4. Pick up → **In Progress**, comment with plan, create a feature branch
5. PR open → **Review**, awaiting user review
6. PR merged → **Done**
7. Periodically sweep Done → **Archived**
8. Cards needing a spec get a comment linking to `.agents/specs/`

## Card Conventions

### Titles
Action-oriented for features (e.g., "Add archive endpoint for cards"). Bug-report style for bugs. Keep under 80 characters.

### Descriptions
Include Goal, Background (if needed), and specific deliverables. Reference specs with wikilinks.

### Comments
Session journals — write assuming the reader has no prior context. Include what was done, what changed, and what's next.

## Session Workflow

When the user signals board work:

1. **Check for updates** — `get_cards` with `since` filter for recent activity
2. **Brief the user** — short summary of board state (what's ready, in progress, blockers)
3. **Wait for direction** — don't auto-start work or grab cards

During a session:
- Move cards as their state changes
- Comment on cards as work progresses (write for a reader with no prior context)
- Create new cards when gaps or ideas surface — put them in Triage with minimal ceremony

**Card addressing:** Use `cardNumber` + `boardSlug` (e.g., card #15 on `collaboard`)
**Auth key:** stored in `.agents.env` (gitignored). Use THIS project's key for the collaboard board.
**Board slug:** `collaboard`

## Archive

- Use `archive_card` to archive (not `move_card`)
- Archived cards are frozen: no edits, comments, labels, or attachment mutations (400)
- Only `restore_card` (requires target laneId) and delete are allowed
- `get_cards` excludes archived by default; pass `includeArchived: true` to include
- Card responses include `isArchived` (bool)
