# Integrating with Webhooks

Collaboard can POST a structured event to a URL you choose whenever a card is
**created** or **moved**. That turns board activity into a signal an external
system can act on — a workflow automation tool, a small script, an AI agent —
without polling the API or holding a connection open.

This guide is the practical walkthrough: turn it on, point it somewhere, confirm a
delivery arrived, and read the delivery log when something looks wrong. It also
covers the one rule you should read **before** you point a webhook at anything that
creates cards — the recursion guard.

For the exact field-by-field contract (the envelope, both payload shapes, the
headers, the signing scheme), see the [API Reference](api-reference.md#webhooks).
For the host settings, see [Host Configuration](../README.md#webhooks).

---

## Read this first: the recursion guard

If your automation reacts to a `card.created` event by **creating a card** — a very
common shape, e.g. "a card landed in Inbox, so create a follow-up card" — you have
built a loop. The card your automation creates fires its own `card.created`, which
triggers your automation, which creates another card, and so on. With agents in the
mix this is a *when*, not an *if*.

Every event carries an `actor` telling you **who** caused it, and `actor.role` is the
field that breaks the loop. The four possible role values are:

- `Administrator`
- `HumanUser`
- `AgentUser`
- `AgentAdministrator`

**Filter on a human-role allowlist, not an agent-role denylist.** Forward an event
to your automation only when the actor is a human:

```js
const HUMAN_ROLES = ["Administrator", "HumanUser"];

if (!HUMAN_ROLES.includes(event.actor.role)) {
  return; // an agent caused this — ignore it, don't let the loop run
}
// ...your automation here
```

Why an allowlist and not `if (event.actor.role !== "AgentUser") { ... }`? Because the
agent side has **two** roles today (`AgentUser` and `AgentAdministrator`), and the set
can grow. A denylist that names `AgentUser` silently lets an `AgentAdministrator`-created
card through — and the day a new agent role is added, every denylist that didn't know
about it springs a leak. Allowlisting the two human roles excludes every present and
future agent role by default. The safe set is the one that doesn't change when new
roles appear.

This is the single most important line in your consumer. Get it in place before you
wire the create side of anything.

---

## Turn it on

Webhooks are off until you configure an endpoint. Set `Webhooks:Endpoint` to the URL
that should receive the POSTs — either as an environment variable (note the double
underscore) or in `appsettings.json`:

```bash
export Webhooks__Endpoint="https://automation.example.com/collaboard-hook"
```

```jsonc
// appsettings.json
{
  "Webhooks": {
    "Endpoint": "https://automation.example.com/collaboard-hook"
  }
}
```

Restart the API. On boot it logs the state it resolved, so you can confirm the config
took effect before you have any consumer working:

- `Webhooks enabled → automation.example.com (signed: false).` — live (the log shows the host only, never the full URL or the secret).
- `Webhooks dark (no Webhooks:Endpoint configured).` — no endpoint set.
- `Webhooks disabled (Webhooks:Enabled = false).` — endpoint kept, delivery paused.

If you can't read the logs, an admin can query the same answer at any time:

```
GET /api/v1/webhooks/status
→ { "enabled": true, "endpointConfigured": true, "signed": false }
```

This returns booleans only — never the secret or the URL — so it's safe to expose to
a setup tool.

---

## Test your endpoint

There is no "send me a test event" button in v1. To confirm your endpoint actually
receives a payload, do the thing that produces one:

1. Open a **scratch board** (not a live one — see the warning below).
2. Create a card on it, or move a card between lanes.
3. Within a moment your endpoint should receive a POST. If it doesn't, an admin can
   check `GET /api/v1/webhooks/deliveries` (see [When it breaks](#when-it-breaks)) to
   see whether Collaboard tried to deliver at all.

> **Use a scratch board for testing, not a live one.** A webhook fires for *every*
> matching card event on the board, so creating or moving cards on a real board to
> "test" the wiring will trigger your real automation against real cards. Make a
> throwaway board, test there, then point the automation at the boards you mean.

---

## What you receive

A `card.created` POST body looks like this (the full contract is in the
[API Reference](api-reference.md#the-envelope)):

```json
{
  "event": "card.created",
  "eventId": "01J9ZQK8H6F4N3M2P7R5T8V0XW",
  "occurredAt": "2026-06-18T16:42:25.770Z",
  "version": "1",
  "boardId": "f6fa6794-4bed-44d0-9656-de8080791302",
  "boardSlug": "collaboard",
  "actor": { "userId": "52df8c11-...", "name": "Bill Wheelock", "role": "Administrator" },
  "data": {
    "card": { "number": 321, "name": "Investigate flaky test", "laneId": "b7c8...", "position": 0, "...": "..." },
    "laneName": "Inbox"
  }
}
```

A few things worth knowing so you don't misread a payload:

- **Filter on names, not GUIDs.** `boardSlug` and the lane names (`data.laneName`, and
  `data.from`/`data.to` on a move) are right there in the body — you can route on
  `boardSlug === "research" && data.to.laneName === "Ready"` without a single lookup.

- **The card is state *at the moment of the event*, not current state.** A
  `card.created` event always shows `commentCount: 0`, `attachmentCount: 0`, and
  `latestComment: null` — the card was just born. That's correct, not a missing value.
  Don't treat those zeros as a bug or a sign the payload is incomplete.

- **A draft card does not fire until it's finalized.** When someone composes a card
  interactively, Collaboard holds a temporary draft until they commit it. The
  `card.created` event fires on finalize, not while the draft is being typed — so an
  in-progress compose that gets cancelled never produces an event. You only ever see
  real, committed cards.

- **Ignore fields you don't recognize.** Within `version: "1"`, new fields may be
  *added* to the payload over time. Your consumer must tolerate that — parse leniently
  and ignore unknown fields. A strict-schema deserializer configured to reject any key
  it wasn't told about will break the first time Collaboard adds a field, even though
  nothing about the contract you depend on changed. (If a change is ever *breaking*,
  the `version` value bumps — so you can branch on it.)

- **Sort on `occurredAt`, don't trust arrival order.** Delivery is best-effort and
  not strictly ordered, so two events can arrive out of the order they happened. If
  ordering matters to your automation, sort on `occurredAt` (the server-side
  timestamp of when the fact happened) rather than the order POSTs land.

- **Deduplicate on `eventId`.** Delivery is at-least-once: a retry after a flaky
  response can deliver the same event twice. The `eventId` (a ULID) is stable across
  retries of one event, so keep a short memory of recently-seen ids and drop repeats.

---

## Verifying the signature (optional)

If you set a `Webhooks:Secret`, every delivery is signed so your endpoint can confirm
it really came from Collaboard. The signature rides in a header:

```
X-Collaboard-Signature: sha256=<hex-lowercase-digest>
```

The digest is HMAC-SHA256 over the **exact raw bytes of the request body**, keyed by
the shared secret. To verify, compute the same HMAC over the body **as you received
it** — before parsing it as JSON or re-serializing — and compare against the header
value with a constant-time comparison:

```js
import { createHmac, timingSafeEqual } from "node:crypto";

function isValid(rawBody, header, secret) {
  const expected = "sha256=" + createHmac("sha256", secret).update(rawBody).digest("hex");
  const a = Buffer.from(header);
  const b = Buffer.from(expected);
  return a.length === b.length && timingSafeEqual(a, b);
}
```

The one trap to avoid: sign the bytes you *received*, not a re-serialized copy of the
parsed object. Re-serializing can reorder keys or change whitespace, and the HMAC
won't match even though the payload is genuine.

---

## When it breaks

Delivery is **asynchronous and never blocks the board.** When a card is created or
moved, the operator's action returns immediately; the webhook is delivered in the
background. So a slow or dead endpoint never makes the board feel slow or fails a
card operation — but it also means a failed delivery is quiet unless you go look.

Two places to look:

**The delivery log.** Every attempt — success or failure — is recorded. An admin can
read it:

```
GET /api/v1/webhooks/deliveries?boardId={id}
```

Each row carries the event id and type, the attempt number, the status
(`Succeeded` / `Failed`), the HTTP status code (when there was a response), and a
truncated error (for timeouts, TLS/DNS failures, connection refused, or a non-2xx
body). A webhook that's configured but silently failing shows up here as a run of
`Failed` rows — which is exactly the thing a fire-and-forget integration otherwise
hides.

**The retry behavior.** A failed delivery is retried up to `Webhooks:MaxAttempts`
times (default 3): the first try is immediate, then a short backoff before each
retry (roughly the `Webhooks:RetryBackoffBase` wait, growing for later retries, with
a little jitter). After the final attempt fails, Collaboard records the failure and
logs it at a level the operator can see — so a permanently dead endpoint is loud, not
a black hole.

> **One honest caveat: the delivery log is not a complete record across a restart.**
> The delivery queue lives in memory. If the API restarts while an event is waiting
> to be delivered, that event is simply dropped — and because it never reached the
> attempt stage, it leaves **no `Failed` row**. It's gone with no trace in the log. So
> don't treat the delivery log as a guaranteed ledger of everything that ever
> happened; treat it as the record of everything Collaboard *attempted to deliver*.
> In practice a dropped-across-restart event is rare and recoverable while you're at a
> small scale (the card it was about is sitting right there on the board), so this is
> an acceptable v1 trade-off — but it's worth knowing before you build something that
> assumes the log is exhaustive.

---

## A worked example (n8n)

[n8n](https://n8n.io/) is a self-hostable workflow automation tool, and a webhook is
the natural way to drive an n8n workflow from board activity. It's a concrete
illustration — nothing here is n8n-specific; any tool that can receive an HTTP POST
works the same way.

1. In n8n, add a **Webhook** trigger node. It gives you a URL.
2. Set that URL as `Webhooks:Endpoint` in Collaboard and restart. Confirm the boot log
   says `Webhooks enabled → ...`.
3. Add an **IF** node right after the trigger that drops non-human events — the
   [recursion guard](#read-this-first-the-recursion-guard). Forward only when
   `actor.role` is in `["Administrator", "HumanUser"]`.
4. Branch on what you care about — e.g. `event === "card.moved"` and
   `data.to.laneName === "Ready"` — and wire the rest of your workflow off that.
5. Test against a scratch board (create or move a card), watch the execution land in
   n8n, then point it at the boards you actually mean.

That's the whole shape: Collaboard emits the fact, your tool decides what to do with
it, and the recursion guard keeps an automation that creates cards from chasing its
own tail.
