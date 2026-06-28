import type {
  CreateWebhookInput,
  UpdateWebhookPatch,
  WebhookDelivery,
  WebhookSubscription,
} from '@/types';
import { arraysEqual } from '@/lib/utils';

// Webhook presentation + classification logic. Pure functions over the
// API contract — kept out of the components so the load-bearing rules (what
// counts as "blocked", the secret set/keep/clear payload) are unit-tested in
// isolation. The server owns all display naming: the selectable event catalog
// (labels, descriptions, family grouping) is fetched from
// `GET /webhooks/event-types`, not hardcoded here — so the picker can
// never again drift from what the backend emits and accepts.

// --- Event catalog --------------------------------------------------------
// The catalog itself is a fetched API contract — its schema/types live in
// `@/lib/schemas` + `@/types` (`WebhookEventGroup`), fetched via
// `useWebhookEventCatalog`. The wildcard sentinel stays here because it's a
// frontend-only concern: "send all events" is a form toggle that collapses to
// `["*"]`, independent of the catalog.

export const WEBHOOK_WILDCARD = '*';

export function isWildcard(events: readonly string[]): boolean {
  return events.includes(WEBHOOK_WILDCARD);
}

// The events array the API expects, from the form's "send all events" toggle +
// the per-event selection. The wildcard wins — it collapses to ["*"], which the
// server reads as "every event type, now and future".
export function buildEventsPayload(sendAll: boolean, selected: readonly string[]): string[] {
  if (sendAll) return [WEBHOOK_WILDCARD];
  return [...selected];
}

// --- Delivery health classification --------------------------------------

export type WebhookHealth = 'ok' | 'failing' | 'blocked' | 'disabled' | 'idle';

// A delivery whose target resolved to a private/internal address is recorded as
// Failed with a null httpStatusCode (the request never left the host) and a
// blocked-address error. We match the server's own phrasing so the "delivery
// blocked — private target" state is identifiable and self-explaining rather
// than lumped in with ordinary connection failures.
const BLOCKED_ERROR_PATTERN =
  /blocked address|private (?:or|network|\/)|AllowPrivateNetworkTargets/i;

export function isBlockedDelivery(delivery: WebhookDelivery | undefined): boolean {
  if (!delivery) return false;
  if (delivery.status !== 'Failed') return false;
  if (delivery.httpStatusCode !== null) return false;
  return BLOCKED_ERROR_PATTERN.test(delivery.error ?? '');
}

// The single per-row health verdict, in priority order: a disabled subscription
// is paused (no delivery regardless of history); a blocked one needs the SSRF
// remediation; a never-delivered one is idle; otherwise the last delivery's
// status decides. `lastAttempt` is the subscription's most-recent delivery from
// the deliveries log (joined client-side), used only to refine Failed → blocked.
export function classifyWebhookHealth(
  subscription: WebhookSubscription,
  lastAttempt: WebhookDelivery | undefined,
): WebhookHealth {
  if (!subscription.enabled) return 'disabled';
  if (isBlockedDelivery(lastAttempt)) return 'blocked';
  if (subscription.lastDeliveryStatus === null) return 'idle';
  if (subscription.lastDeliveryStatus === 'Failed') return 'failing';
  return 'ok';
}

// Success ratio over the subscription's full delivery history (the metrics are
// computed server-side over the whole log, not the recent window). Null when
// there have been no deliveries — there is no rate to show yet.
export function successRate(metrics: {
  successCount: number;
  failureCount: number;
}): number | null {
  const total = metrics.successCount + metrics.failureCount;
  if (total === 0) return null;
  return metrics.successCount / total;
}

export function formatSuccessRate(rate: number | null): string {
  if (rate === null) return '—';
  // One decimal, but show a clean "100%" / "0%" rather than "100.0%".
  const pct = rate * 100;
  const rounded = Math.round(pct * 10) / 10;
  return Number.isInteger(rounded) ? `${rounded}%` : `${rounded.toFixed(1)}%`;
}

// --- Form payload builders ------------------------------------------------
// Extracted from the form (load-bearing correctness: a wrong patch could leak,
// keep, or silently clear a secret). `secret` is the NEW value the operator
// typed (empty = none typed); `clearSecret` is the edit-only "go unsigned".

export type WebhookFormState = {
  url: string;
  name: string;
  enabled: boolean;
  sendAll: boolean;
  selected: string[];
  secret: string;
  clearSecret: boolean;
};

// Whether the form state will result in a signed subscription. The single
// notion of "a secret is being set", shared by the signed/unsigned indicator and
// the submit builders so the two can never disagree: a whitespace-only secret is
// treated as no secret (an invisible HMAC key in a masked field is a fat-finger,
// not a credential), and clearSecret always wins.
export function willBeSigned(state: WebhookFormState, currentlySigned: boolean): boolean {
  if (state.clearSecret) return false;
  if (state.secret.trim().length > 0) return true;
  return currentlySigned;
}

export function buildWebhookCreateInput(state: WebhookFormState): CreateWebhookInput {
  const input: CreateWebhookInput = {
    url: state.url.trim(),
    events: buildEventsPayload(state.sendAll, state.selected),
    enabled: state.enabled,
  };
  const name = state.name.trim();
  if (name) input.name = name;
  // Trim the secret value, not just the gate: trailing whitespace pasted with a
  // credential is invisible in a masked field and would silently break HMAC
  // verification on the receiver. A whitespace-only secret trims to empty → unset.
  const secret = state.secret.trim();
  if (secret) input.secret = secret;
  return input;
}

// A minimal diff against the current subscription — only changed fields are
// sent (an empty patch means "no change", and the caller skips the request).
// The secret follows set / keep / clear: clear wins; else a typed value
// replaces; else the secret key is omitted entirely (kept unchanged).
export function buildWebhookUpdatePatch(
  subscription: WebhookSubscription,
  state: WebhookFormState,
): UpdateWebhookPatch {
  const patch: UpdateWebhookPatch = {};
  const url = state.url.trim();
  const name = state.name.trim();
  const events = buildEventsPayload(state.sendAll, state.selected);

  if (url !== subscription.url) patch.url = url;
  if ((name || null) !== (subscription.name ?? null)) patch.name = name;
  if (state.enabled !== subscription.enabled) patch.enabled = state.enabled;
  if (!arraysEqual(events, subscription.events)) patch.events = events;

  // Same trim rationale as create: the value is trimmed, and a whitespace-only
  // secret trims to empty → the key is omitted (kept unchanged), not replaced.
  const secret = state.secret.trim();
  if (state.clearSecret) {
    patch.clearSecret = true;
  } else if (secret) {
    patch.secret = secret;
  }

  return patch;
}
