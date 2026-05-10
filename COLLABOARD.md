# Collaboard Board

Collaboard development is tracked on its own production instance — Collaboard runs on Collabhost, the board the team uses to track Collaboard work IS a Collaboard board. Self-hosted dogfooding.

| Field | Value |
|---|---|
| Slug | `collaboard` |
| Board UUID | `f6fa6794-4bed-44d0-9656-de8080791302` |
| Auth key | `~/.agents/bots/<bot>/.env` → `COLLABOARD_AUTH_KEY` (per-bot, gitignored) |

See the `collaboard` skill for the full auth contract and MCP usage. The conventions below are project-specific overrides on top of that skill.

## Lanes

Collaboard uses the org-default lane set defined in the `collaboard` skill, with no project-specific deviations today.

| Lane | Purpose |
|---|---|
| **Backlog** | Considered for the next quarter or two; not the immediate next pickup. Long-running `Blocked` cards parked here are acceptable — Backlog is a fine parking lot for hard problems. |
| **Triage** | New items, awaiting disposition. Cards genuinely needing discussion before sizing live here. Sized + labeled cards with no surfaceable triggering question get promoted out by the coordinator without per-card ask. |
| **Ready** | Sized, scoped, approved — picked up next session (or right now). Curated by the operator; coordinator proposes promotions from Backlog. |
| **In Progress** | Actively being worked on. |
| **Review** | PR open, awaiting operator review. |
| **Done** | Merged to main, awaiting archive sweep. |
| **Archived** | Closed. Archived cards are **frozen** — no edits, comments, labels, or attachment changes (400 response). Only `restore_card` and delete are allowed. |

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

| Size | Ordinal | Effort + Risk |
|------|---------|---------------|
| S | 0 | Trivial — single surface, no ambiguity |
| M | 1 | Moderate — one or two surfaces, straightforward |
| L | 2 | Significant — multiple surfaces or non-trivial logic |
| XL | 3 | Complex — cross-cutting, high risk, or unknown scope |

Sizes represent effort and risk, not urgency. The **scope of work** — which surfaces are touched (backend, frontend, MCP, tests) — is the primary triage signal for sizing. During triage, evaluate the scope and set the size accordingly; don't ask the user to reconsider.

## Workflow

1. New items → **Triage** with a type label (`Bug`, `Feature`, etc.).
2. Size (S/M/L/XL), prioritize → **Backlog**.
3. Operator approves for work → **Ready**. Agents pick up cards only from Ready.
4. Pick up → **In Progress**, comment with the plan, create a feature branch.
5. PR open → **Review**, awaiting operator review.
6. PR merged → **Done**.
7. Coordinator sweeps Done → **Archived** as part of session-close hygiene. No external trigger; coordinator's judgment, executed when the lane has accumulated enough to be worth a sweep.
8. Cards needing a spec get a comment linking to `.agents/specs/`.

## Card Conventions

### Titles
Action-oriented for features (e.g., "Add archive endpoint for cards"). Bug-report style for bugs. Keep under 80 characters.

### Descriptions
Include goal, background (if needed), and specific deliverables. Reference specs by name (`.agents/specs/<file>.md`).

### Comments
Session journals — write for a reader with no prior context. Include what was done, what changed, what's next, and PR links when relevant.

### When to defer card disposition

When a board walk with the operator is imminent, individual card-state questions (move, archive, verify-and-close) wait for the walk rather than getting fragmented into per-card rulings. The walk is the right surface for batch dispositions.

## Session Workflow

When the operator signals board work:

1. **Pull live state** — `get_lanes` + `get_cards` for recent activity. The board is ground truth; HANDOFF/TODO can drift across sessions and must be reconciled against the board, not the other way around.
2. **Brief the operator** — short summary of board state (what's ready, what's in progress, what's blocking, what's stale). Tables over prose.
3. **Wait for direction** — don't auto-start work or grab cards.

During a session:

- Move cards as state changes (Triage → Backlog → Ready → In Progress → Review → Done).
- Comment on cards as work progresses — write for a reader with no prior context.
- Create new cards when gaps or ideas surface — put them in Triage with minimal ceremony.

### Cross-project externally-gated cards

When a Collaboard card is gated on a sibling project's deliverable (e.g., something Collabhost ships first), keep the card in Backlog and add an explicit gate-and-trigger comment naming what's blocking and what unblocks it. Do **not** apply the `Blocked` label — that label is reserved for in-Triage gating; Backlog is the right home for externally-gated cards by design.

### Card addressing

Use `cardNumber` + `boardSlug` (e.g., card #15 on `collaboard`). When referencing cards from another project's board (e.g., Collabhost #220), always name the board — card numbers are board-scoped and easy to garble silently.

## Archive

- Use `archive_card` to archive (not `move_card` — the archive lane refuses `move_card` with a 400).
- Archived cards are frozen: no edits, comments, labels, or attachment mutations (400).
- Only `restore_card` (requires target `laneId`) and delete are allowed.
- `get_cards` excludes archived by default; pass `includeArchived: true` to include.
- Card responses include `isArchived` (bool).

## MCP Tips

- **Pass `labelIds` at creation.** Use the `labelIds` param on `create_card` instead of a separate `add_label_to_card` call. One call instead of two.
- **Schema-load before first use of an unfamiliar tool.** Don't infer parameter names from sibling-tool conventions — the wrapper hides parameter mismatches as opaque `An error occurred invoking '<tool>'.` messages. Card #202 (Ready) addresses the wrapper-side; the caller-side discipline is to load the schema first.
