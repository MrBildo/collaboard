# Workflow

How work moves from idea to merged code.

## 1. Pre-Flight

Before ANY task:

1. **Check the board.** `get_cards` on the project's board. Know the current state.
2. **Check for existing spec.** Scan `.agents/specs/` by slug or card number. If found, confirm with user before overwriting.
3. **Read the user's ask carefully.** Don't assume — ask when unclear.
4. **Review board conventions.** See [[op-docs/board-conventions]] for lane rules, labels, and sizing.

## 2. Gather Context

- Existing spec, board card description, user's verbal requirements
- Cross-project impact? API changes? Database changes?
- What can be reused? Existing components, endpoints, patterns?

## 3. Discuss Before Speccing

The agent is a **partner**, not a worker. Every card is a chance to surface ambiguity and align.

- Ask about: design directions, scope boundaries, data model, competing approaches
- Present your understanding back with **numbered questions**
- Let the user answer in any order
- Only write the spec **after alignment is clear**
- If the task is small enough to not need a spec, say so and skip to implementation

## 4. Write Spec

Use [[specs/TEMPLATE]]. The spec must be **self-contained** — another agent can implement from it alone.

Key rules:

- **Entry point rule.** New modules must state WHERE they get called from at the top level, not buried in deliverables. A module with no caller is dead code.
- **Multi-card work.** Create a **release dashboard card** at the START with a phased guide and agent operating rules. This is the coordination anchor.

## 5. Implement

- **Incremental integration.** Wire each module immediately after building it. Never defer integration to the end — it hides breakage.
- **Implementation journal.** Comment on cards as you work, not just after completion. Progress is visible or it didn't happen.
- **Circuit breaker.** When working unsupervised on multiple cards, verify after every 2-3 cards. Stop and report on any failure. Do not push through broken state.

## 6. Verify

- Follow the project's Definition of Done (defined in each project's CLAUDE.md).
- **Tools > Tokens.** Run the test suite, hit the endpoint, check the build. Deterministic verification beats agent assertions.
- A card is NOT done until the feature is **observable in the running system**.

## 7. Review

- PR with conventional commits (`feat:`, `fix:`, `chore:`, `docs:`, `refactor:`)
- Squash merge to main (or release branch if active)
- **Agent NEVER merges its own PR.** User reviews and merges.

## 8. Between Milestones

Run this checklist after completing a milestone, before planning the next:

1. **Structure check.** Verify `.agents/` has required directories (`specs/`, `research/`, `temp/`, `archive/specs/`, `archive/postmortems/`). Only WORKFLOW.md belongs at `.agents/` root.
2. **Loose file check.** No stray files in `research/` or `archive/` roots. Everything in its designated subfolder.
3. **Completed spec check.** Status = Complete + branch merged → move to `archive/specs/`.
4. **Roadmap extraction.** Scan specs and postmortems for "Future Work", "Next Steps", "Action Items". Extraction is **copy not cut** — sources keep their content. Roadmap entries link back with `**Source:** [[path]]`. One item, one entry — update duplicates, don't re-add.
5. **Temp cleanup.** Capture any value first, then flag files 7+ days old for removal.
6. **Card audit.** Evaluate each active card: implementation progress, spec deviations, gaps, what's missing.

**Core invariants:**

- Never delete anything. Moves and extracts only.
- Present plan before acting. Confirm with user before executing moves.

## 9. Board Stewardship

- Maintain cards **throughout** work, not just at completion. Move lanes, add comments, update descriptions.
- Speed vs safety: **5 cards done right > 9 cards half-right.**
- Card descriptions should be consolidated and implementation-ready. If a card is vague, improve it before picking it up.
