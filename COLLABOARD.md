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
| **Archived** | Cleared periodically |

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

## Workflow

1. New items → **Triage** with a type label (`Bug`, `Feature`, etc.)
2. Size (S/M/L/XL), prioritize → **Backlog**
3. User approves for work → **Ready** (agents should only pick up cards from Ready)
4. Pick up → **In Progress**, create a feature branch
5. PR open → **Review**, awaiting user review
6. PR merged → **Done**
7. Periodically sweep Done → **Archived**
8. Cards needing a spec get a comment linking to `.agents/specs/`

## Session Protocol

- **Start of session:** Call `get_cards` with the Collaboard board slug to see current ready, in-progress items, and recent activity
- **Check for changes:** Use `get_cards` with the `since` parameter to see cards with recent activity (new/edited comments, new attachments, card updates)
- **During work:** Move cards between lanes, add comments with PR links, and label cards as you work
- **Board slug:** `collaboard`
