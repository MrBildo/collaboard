# Collaboard Workflow & Tips — What We've Figured Out

This is the **opt-in companion** to the factual skill reference,
[Operating Collaboard via MCP](./collaboard/SKILL.md). That document tells you *how the
machinery works* — the tools, the board model, the identifier rules. This one is
different: it's *what we've learned* running Collaboard boards for a human-plus-agent
team over many months. Suggestions, not mechanics.

**Feel free to skip this entirely.** If you already have a workflow designed
elsewhere — your own lane conventions, your own card protocol, your own agent
harness — none of this is required to operate Collaboard, and adopting someone
else's process on top of a working one is usually a step backward. Read this only
if you're starting from a blank board and want a head start, or if you're curious
how another team uses the same tools.

Everything below is a default you can take, reshape, or ignore. We'll mark the few
places where a tip is closer to "this will save you real pain" than "this is how we
happen to do it."

---

## Setup ergonomics

These two are the highest-leverage things to set up once, and they pay off every
session afterward. They're the closest this document gets to a recommendation
rather than a preference.

### Give each agent its own key, and store it where the agent can read it

Every MCP call takes an `authKey`, and that key *is the identity* — card history,
comments, and the activity record all attribute actions to whoever the key belongs
to. (See [Authentication](./collaboard/SKILL.md#authentication) in the skill reference.)

Two things follow from that:

- **One key per agent, not one shared key for all of them.** If five agents share
  one key, the board can't tell them apart — every comment and every card move
  looks like it came from the same actor, and you lose the audit trail that makes a
  shared board legible. Issue a distinct board user (and key) per agent. The same
  key works across every board that agent has access to, so it's one credential per
  agent, not one per board.

- **Store the key somewhere the agent loads automatically, and keep it out of the
  repository.** We keep each agent's key in that agent's own private config file
  (for us, a per-agent `.env` that is *not* checked in), and the agent reads it from
  there at the start of a session. The key is a secret — the same caution as any
  credential applies: never commit it, never paste it into a card comment or a PR
  description, never log it. A key that lands in a public repo is a key you have to
  rotate.

A small but real gotcha: many MCP clients do **not** automatically inject the key
into each call. Whatever your agent doesn't explicitly pass, the server doesn't
see. So "the key is in the environment" isn't enough on its own — the agent has to
actually read it and pass it as the `authKey` argument. If calls fail with an
auth error even though the key exists, that mismatch is the first thing to check.

### Cache the board's identifiers in an auto-loaded file

The skill reference's first best practice is *discover once, cache for the
session* — `get_boards`, `get_lanes`, `get_sizes`, `get_labels` all return
identifiers that are stable for the life of the board. You can take that one step
further across sessions.

A board's slug, its lane IDs, its size names, and its label IDs barely ever
change. Re-discovering them at the start of every single session is a tax: several
tool calls before any real work, every time. Instead, **write them down once in a
file your agent loads at the start of every session** — the same file where you
keep its standing instructions (a `CLAUDE.md`, an `AGENTS.md`, a system prompt, a
project memory file — whatever your harness auto-loads).

What's worth caching:

- The **board slug and GUID** — so the agent never has to call `get_boards` just to
  translate a slug. (Recall from the [Identifier Rules](./collaboard/SKILL.md#identifier-rules)
  that board-level read tools require the GUID, not the slug.)
- The **lane names and their GUIDs** — the single most-used lookup, since almost
  every card create/move names a lane.
- The **size names** (`S`/`M`/`L`/...) and **label names** — you can pass these
  tools a name instead of a GUID in most cases, but having the canonical list in
  front of the agent stops it from inventing a label that doesn't exist.

Two honest caveats so the cache doesn't bite you:

- **Lane and label GUIDs are board-specific.** A GUID you cached for one board is
  meaningless on another. If your team runs several boards, scope the cache per
  board.
- **A cache can go stale.** If someone renames a lane, adds a label, or reorders
  the board, the cached IDs drift from reality. GUIDs don't change when a lane is
  *renamed* (the ID is stable; only the name moved), but a lane that's *deleted and
  recreated* gets a new GUID. Treat the cache as a fast path, not gospel — if a
  call fails with a not-found error on an ID you cached, re-run the relevant
  `get_*` discovery call and refresh the file. A once-in-a-while refresh is far
  cheaper than re-discovering everything every session.

---

## A workflow you can borrow

Below is the lane-and-card rhythm our team settled into. It is **one example**, not
the right answer. Collaboard imposes no workflow — lanes are just named columns and
labels are just tags. If your team thinks in a different pipeline, build that
instead.

### Lanes as a pipeline

We give lanes a left-to-right meaning and let a card flow through them:

1. **Triage** — where new work lands. It hasn't been sized or scoped yet.
2. **Backlog** — sized, prioritized, but not yet approved to start.
3. **Ready** — approved and scoped. Agents pick up work *only* from here, which
   keeps an agent from grabbing something that hasn't been blessed yet.
4. **In Progress** — actively being worked.
5. **Review** — work done, waiting on a human (or another agent) to review.
6. **Done** — finished and merged.

The detail that earns its keep: **a single lane (we use "Ready") is the only place
agents start work from.** That one rule is what lets a human stay in control of a
board that agents are otherwise driving autonomously — nothing gets worked until it
reaches the lane that means "approved." If you take one idea from this section, take
that one.

You don't need all six lanes. A smaller team might collapse Triage and Backlog, or
drop Review if there's no separate review step. Shape the pipeline to your actual
process.

### Sizes are effort and risk, not urgency

We use sizes (`S`/`M`/`L`/`XL`) to mean *how much work and how much risk*, not *how
soon*. The signal we size against is **how many surfaces the work touches** and how
much ambiguity it carries — a one-file change with an obvious fix is `S`; a
cross-cutting change with unknown scope is `XL`. Urgency lives elsewhere (lane
position, a label, a comment), because effort and urgency are genuinely different
axes and conflating them makes both illegible.

### Labels for type, sparingly for status

We keep labels mostly for *kind of work* — a `Bug` / `Feature` / `Docs` / `Chore`
set that mirrors our commit-message prefixes, so a card's label and its eventual
commit agree. We use a single transient status label (`Blocked`) for the one state
the lane position can't express. The temptation is to label everything; resist it.
A label that's on 90% of cards tells you nothing. Labels earn their place when they
let you *filter* — `get_cards` takes a `labelId` filter, so a label is worth adding
when you'll later want to pull "just the bugs."

---

## Operating tips

These are smaller, board-agnostic habits that have saved us grief. Take the ones
that fit.

### Write comments for a reader with no context

Card comments are the durable memory of a board — the place where "what happened and
why" lives after the work is done. The agents on our team treat a card comment as a
short journal entry: what was done, what changed, what's next, written so a
teammate (human or agent) arriving cold can follow it without having been in the
room. This is doubly true on a shared board where an agent picking up a card may
have *no* memory of the session that last touched it. The comment is the handoff.

Comments render as Markdown (see [Markdown capabilities](./collaboard/SKILL.md#markdown-capabilities)),
so use real structure — a short summary line, a list of changes, a "next" line.
And use **real newlines**: a literal `\n` in the text renders as the characters
backslash-n, not a line break.

### Archive is the reversible "delete"

Collaboard's archive freezes a card and hides it from normal views but keeps it
forever; the MCP deliberately offers no bulk delete. We lean into that: when a card
is finished or no longer relevant, we **archive** it rather than delete it. Archived
cards stay searchable (opt them into a search) and fully restorable, so archiving
costs nothing if you were wrong, while a delete is gone. Our rule of thumb: delete
is for genuine mistakes (a card created in error); archive is for everything that
served its purpose. A periodic sweep of the Done lane into the archive keeps the
working board uncluttered without losing the record.

### Reach for the bulk and prune tools at scale

When you find yourself about to fire the same operation at a handful of cards in a
row — archiving a finished batch, moving a cluster, relabeling a group — that's a
single bulk call, not a loop. Beyond the obvious round-trip savings, a bulk call
emits one board-update event instead of N (connected boards re-render less), and it
hands back a per-card result envelope telling you exactly which cards succeeded and
which didn't. For archiving by a rule (everything older than a date, everything in a
lane), `prune_preview` lets you see the match set before `prune` commits. The full
shapes are in the [Tool Reference](./collaboard/SKILL.md#tool-reference); the habit worth
forming is *recognizing the 2-or-more-uniform-operations moment and switching tools.*

### Let the board be the source of truth for state, not your memory

On a multi-agent board, the card's current lane, comments, and labels are
authoritative — your agent's recollection from a previous turn is not. A card you
"left in Ready" may have been moved, commented on, or re-prioritized by someone else
since. Before acting on a card you remember, read its current state — and in
particular read the **latest comment** on any card you're about to brief on or ask
about. The card list shows a comment *count*, not the bodies, so a recent decision
sitting in a comment is invisible until you open the card. Acting on stale memory of
a board that other actors are also driving is the most common way an agent does the
wrong thing confidently.

---

## When multiple agents share a board

A board built for a team of agents is a shared mutable surface, and the usual
shared-state cautions apply.

- **Attribution depends on per-agent keys.** This is why the per-agent-key setup at
  the top matters: it's what makes "who did this?" answerable. With it, the activity
  record is a real audit trail; without it, it's noise.

- **Serialize agents that would touch the same thing.** Two agents editing the same
  card, or working files that overlap, will step on each other. Our rule outside the
  board generalizes cleanly: *if two agents could write to the same thing, they
  should be the same agent, or run one after the other.* Independent cards on
  disjoint surfaces parallelize fine; overlapping ones serialize. When in doubt,
  sequence — the time you'd save running in parallel is usually lost to untangling
  the collision.

- **A "pick up only from one lane" rule scales the team safely.** The single-entry-
  lane convention from the workflow section above isn't just a human-control knob —
  it's also what keeps several agents from racing to grab the same unblessed work.
  Work becomes pickable only when a human (or a coordinating agent) moves it to the
  start lane.

---

## That's it

None of this is required to use Collaboard — the [skill reference](./collaboard/SKILL.md)
is the only document you *need*. This one is a starting point you're free to adopt
in part, in whole, or not at all. If you've got a workflow that works, keep it.
