---
name: roadmap
description: View and manage the living backlog at .agents/roadmap/INDEX.md
user_invocable: true
---

# /roadmap

View and manage `.agents/roadmap/INDEX.md` — the living backlog for Collaboard.

## Sections

The roadmap INDEX.md has four sections:

1. **Active** — Currently in progress (linked to spec in `specs/`)
2. **Backlog** — Decided to do, not started
3. **Ideas** — Captured but not committed
4. **Decisions** — Key architectural decisions with date and rationale

## Commands

### View (default)
Read `INDEX.md` and summarize current state: active count, backlog depth, recent decisions.

### Add
Add a new item to the specified section:
- **Required:** section, name, 1-line description
- **Optional:** source link, spec link (if active)

### Update
Modify an existing item: change description, move between sections, add spec link.

### Remove
Delete an entry from the index. Source documents are never touched.

## Format Rules

- **3 lines max per entry:** name, description, source
- **Always include Source** — every item traces its origin (conversation, spec, postmortem, PR)
- **Wikilink-style:** `[[path/to/file]]` (no `.md` extension)
- **No duplicates** — update existing entries, don't re-add
- **Detail files are the exception** — most items don't need separate documents

## Example Entry

```markdown
### Card detail view
Full card editing experience with comments, attachments, and metadata.
**Source:** Session 2026-03-13 — feature backlog review
```
