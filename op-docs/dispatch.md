# Dispatch Protocol

Rules and conventions for dispatching sub-agents.

## Dispatch Rules

- **Spec first:** Write specs to `.agents/specs/` before dispatching. No dispatch without a spec.
- Include ALL context the child needs — it has no memory of this session.
- **Ask, don't guess:** Include: "If you get stuck or unsure, report back rather than guessing."
- Max 3 follow-up rounds per task before escalating to user.
- Dispatch in parallel when independent, sequentially when dependent.

## Sub-Agent Conventions

When dispatching coding or evaluation sub-agents via the Agent tool:

- **Model:** Always use `model: "opus"` (Opus High)
- **Skills:** Instruct sub-agents to use skills appropriate to the task — e.g., dotnet-dev for C# tasks, typescript-dev for TypeScript. A research agent doesn't need coding skills.
- **Report format:** Every sub-agent must return a standardized report. Include this template in the prompt:

### Report Template

    ## Report: <card or task title>

    ### Summary
    <1-2 sentence verdict>

    ### Deliverable Status
    | Deliverable | Status | Notes |
    |---|---|---|
    | <item> | Done / Partial / Missing | <detail> |

    ### Verification
    - Backend build: <pass/fail/not run>
    - Backend tests: <pass/fail/not run — include count>
    - Frontend typecheck: <pass/fail/not run>
    - Frontend lint: <pass/fail/not run>
    - Frontend tests: <pass/fail/not run — include count>

    ### Files Touched
    - <path> — <created/modified/read> — <what changed>

    ### Gaps & Issues
    1. <issue description>

    ### Convention Violations
    <list or "None">

    ### Recommendation
    <next steps, move to Review, stays in Ready, etc.>

## Parallel Dispatch

**Partition by resource, not by task.** When dispatching parallel agents, group work by the files being touched — not by the card or task being worked on. Two cards that edit the same files must go to the same agent. Two cards that touch completely separate projects can go to separate agents. The rule: **if two agents could write to the same file, they must be the same agent.**

When multiple agents need the same repo simultaneously, use **git worktrees** for physically separate working directories.

```powershell
git worktree add ../<repo>-wt-<short-name> -b feature/<branch-name> <start-point>
```

Each worktree needs its own dependency install. The `.git` store is shared.
