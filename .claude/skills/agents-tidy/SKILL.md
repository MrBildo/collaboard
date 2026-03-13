---
name: agents-tidy
description: Scan and maintain .agents/ directory structure, enforce conventions, extract roadmap items
user_invocable: true
---

# /agents-tidy

Scan `.agents/` for structural violations, stale content, and roadmap extraction opportunities.

## Execution Sequence

### 1. Structure Check
Verify these directories exist under `.agents/`:
- `roadmap/` (must contain `INDEX.md`)
- `specs/` (must contain `TEMPLATE.md`)
- `kb/`
- `research/`
- `temp/`
- `archive/specs/`
- `archive/milestones/`
- `archive/postmortems/`

Report any missing directories.

### 2. Loose File Check
Flag files that violate placement rules:
- `.agents/` root: only `WORKFLOW.md` allowed
- `research/` root: no loose files (must be in topic folders)
- `archive/` root: no loose files (must be in category folders)

### 3. Completed Spec Check
For each spec in `specs/`:
- Check if status is "Complete"
- Check if associated branch has been merged to main
- If both: recommend moving to `archive/specs/`

### 4. Roadmap Extraction
Scan specs and postmortems for items that should be in `roadmap/INDEX.md`:
- Future work items mentioned in completed specs
- Action items from postmortems
- Report: new items (not yet in roadmap) vs stale items (in roadmap but done)

### 5. Temp Cleanup
Read each file in `temp/`:
- Flag content that should be captured elsewhere before deletion
- Identify files older than 7 days

### 6. KB Health Check
For each knowledge base folder in `kb/`:
- Verify `META.md` and `INDEX.md` exist
- Report missing navigation files

## Rules

- **Never delete anything** — moves and extracts only
- **Never move without confirmation** — present plan, wait for user approval
- **Extraction is copy, not cut** — sources keep content, roadmap entries link back with `**Source:**`
- **One item, one entry** — update duplicates, don't re-add

## Output Format

```
## .agents/ Health Report

### Structure: [✅ OK | ⚠️ Issues Found]
<findings>

### Loose Files: [✅ Clean | ⚠️ Violations]
<findings>

### Specs: [n active, n ready to archive]
<findings>

### Roadmap: [n new extractions, n stale]
<findings>

### Temp: [n files, n flagged for cleanup]
<findings>

### KBs: [✅ Healthy | ⚠️ Missing nav files]
<findings>

---
Which actions should I take?
```
