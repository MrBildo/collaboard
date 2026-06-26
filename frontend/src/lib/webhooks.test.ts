import { describe, test, expect } from 'vitest';

import {
  WEBHOOK_EVENT_TYPES,
  WEBHOOK_WILDCARD,
  buildEventsPayload,
  buildWebhookCreateInput,
  buildWebhookUpdatePatch,
  classifyWebhookHealth,
  formatSuccessRate,
  isBlockedDelivery,
  isWildcard,
  successRate,
  type WebhookFormState,
} from './webhooks';
import type { WebhookDelivery, WebhookSubscription } from '@/types';

function makeSubscription(overrides: Partial<WebhookSubscription> = {}): WebhookSubscription {
  return {
    id: 'sub-1',
    name: 'test hook',
    url: 'https://example.com/hook',
    enabled: true,
    events: ['card.created', 'card.moved'],
    signed: false,
    successCount: 10,
    failureCount: 0,
    lastDeliveryStatus: 'Succeeded',
    lastDeliveryAtUtc: '2026-06-26T12:00:00Z',
    ...overrides,
  };
}

function makeDelivery(overrides: Partial<WebhookDelivery> = {}): WebhookDelivery {
  return {
    id: 'att-1',
    subscriptionId: 'sub-1',
    eventId: 'evt-1',
    eventType: 'card.moved',
    boardId: 'board-1',
    attempt: 1,
    status: 'Succeeded',
    httpStatusCode: 200,
    error: null,
    attemptedAtUtc: '2026-06-26T12:00:00Z',
    ...overrides,
  };
}

function makeFormState(overrides: Partial<WebhookFormState> = {}): WebhookFormState {
  return {
    url: 'https://example.com/hook',
    name: 'test hook',
    enabled: true,
    sendAll: false,
    selected: ['card.created'],
    secret: '',
    clearSecret: false,
    ...overrides,
  };
}

describe('event catalog', () => {
  test('exposes exactly the two M1 live events as selectable', () => {
    expect(WEBHOOK_EVENT_TYPES).toEqual(['card.created', 'card.moved']);
  });

  test('isWildcard detects the wildcard entry', () => {
    expect(isWildcard(['*'])).toBe(true);
    expect(isWildcard(['card.created'])).toBe(false);
    expect(isWildcard([])).toBe(false);
  });

  test('buildEventsPayload collapses to the wildcard when sendAll is on', () => {
    expect(buildEventsPayload(true, ['card.created'])).toEqual([WEBHOOK_WILDCARD]);
  });

  test('buildEventsPayload returns the selection when sendAll is off', () => {
    expect(buildEventsPayload(false, ['card.created', 'card.moved'])).toEqual([
      'card.created',
      'card.moved',
    ]);
  });
});

describe('isBlockedDelivery', () => {
  test('flags a Failed delivery with null status code and a blocked-address error', () => {
    const delivery = makeDelivery({
      status: 'Failed',
      httpStatusCode: null,
      error: "Webhook host 'n8n.internal' resolves to a blocked address (10.0.0.5).",
    });
    expect(isBlockedDelivery(delivery)).toBe(true);
  });

  test('flags the private-or-blocked registration phrasing', () => {
    const delivery = makeDelivery({
      status: 'Failed',
      httpStatusCode: null,
      error: "Webhook host 'x' resolves to a private or otherwise blocked address.",
    });
    expect(isBlockedDelivery(delivery)).toBe(true);
  });

  test('does NOT flag an ordinary HTTP failure (has a status code)', () => {
    const delivery = makeDelivery({
      status: 'Failed',
      httpStatusCode: 502,
      error: '502 Bad Gateway',
    });
    expect(isBlockedDelivery(delivery)).toBe(false);
  });

  test('does NOT flag a connection timeout (no blocked-address marker)', () => {
    const delivery = makeDelivery({
      status: 'Failed',
      httpStatusCode: null,
      error: 'The operation was canceled.',
    });
    expect(isBlockedDelivery(delivery)).toBe(false);
  });

  test('does NOT flag a succeeded delivery or undefined', () => {
    expect(isBlockedDelivery(makeDelivery({ status: 'Succeeded' }))).toBe(false);
    expect(isBlockedDelivery(undefined)).toBe(false);
  });
});

describe('classifyWebhookHealth', () => {
  test('disabled wins over everything', () => {
    const sub = makeSubscription({ enabled: false, lastDeliveryStatus: 'Failed' });
    expect(classifyWebhookHealth(sub, undefined)).toBe('disabled');
  });

  test('blocked when the last attempt is an SSRF block', () => {
    const sub = makeSubscription({ lastDeliveryStatus: 'Failed' });
    const blocked = makeDelivery({
      status: 'Failed',
      httpStatusCode: null,
      error: 'resolves to a blocked address (127.0.0.1).',
    });
    expect(classifyWebhookHealth(sub, blocked)).toBe('blocked');
  });

  test('idle when there has never been a delivery', () => {
    const sub = makeSubscription({ lastDeliveryStatus: null, lastDeliveryAtUtc: null });
    expect(classifyWebhookHealth(sub, undefined)).toBe('idle');
  });

  test('failing on a non-blocked failed last delivery', () => {
    const sub = makeSubscription({ lastDeliveryStatus: 'Failed' });
    const failed = makeDelivery({ status: 'Failed', httpStatusCode: 500, error: '500' });
    expect(classifyWebhookHealth(sub, failed)).toBe('failing');
  });

  test('ok on a succeeded last delivery', () => {
    expect(classifyWebhookHealth(makeSubscription(), makeDelivery())).toBe('ok');
  });
});

describe('successRate / formatSuccessRate', () => {
  test('null when there are no deliveries', () => {
    expect(successRate({ successCount: 0, failureCount: 0 })).toBeNull();
    expect(formatSuccessRate(null)).toBe('—');
  });

  test('computes the ratio', () => {
    expect(successRate({ successCount: 99, failureCount: 1 })).toBeCloseTo(0.99);
  });

  test('formats a clean integer percentage', () => {
    expect(formatSuccessRate(1)).toBe('100%');
    expect(formatSuccessRate(0)).toBe('0%');
  });

  test('formats one decimal when not whole', () => {
    expect(formatSuccessRate(0.927)).toBe('92.7%');
  });
});

describe('buildWebhookCreateInput', () => {
  test('builds the create payload, trimming url and name', () => {
    const input = buildWebhookCreateInput(
      makeFormState({ url: '  https://x.test/h  ', name: '  prod  ', selected: ['card.created'] }),
    );
    expect(input).toEqual({
      url: 'https://x.test/h',
      name: 'prod',
      events: ['card.created'],
      enabled: true,
    });
  });

  test('omits an empty name and an empty secret', () => {
    const input = buildWebhookCreateInput(makeFormState({ name: '   ', secret: '' }));
    expect(input.name).toBeUndefined();
    expect(input.secret).toBeUndefined();
  });

  test('includes a typed secret', () => {
    const input = buildWebhookCreateInput(makeFormState({ secret: 's3cr3t' }));
    expect(input.secret).toBe('s3cr3t');
  });

  test('collapses to the wildcard when sendAll is on', () => {
    const input = buildWebhookCreateInput(
      makeFormState({ sendAll: true, selected: ['card.created'] }),
    );
    expect(input.events).toEqual(['*']);
  });
});

describe('buildWebhookUpdatePatch — secret set / keep / clear', () => {
  const signed = makeSubscription({
    signed: true,
    secret: undefined,
  } as Partial<WebhookSubscription>);

  test('omitting the secret keeps it (no secret/clearSecret keys)', () => {
    const patch = buildWebhookUpdatePatch(
      signed,
      makeFormState({ secret: '', clearSecret: false }),
    );
    expect('secret' in patch).toBe(false);
    expect('clearSecret' in patch).toBe(false);
  });

  test('a typed secret replaces', () => {
    const patch = buildWebhookUpdatePatch(signed, makeFormState({ secret: 'new-secret' }));
    expect(patch.secret).toBe('new-secret');
    expect(patch.clearSecret).toBeUndefined();
  });

  test('clearSecret wins over a typed secret', () => {
    const patch = buildWebhookUpdatePatch(
      signed,
      makeFormState({ secret: 'ignored', clearSecret: true }),
    );
    expect(patch.clearSecret).toBe(true);
    expect(patch.secret).toBeUndefined();
  });
});

describe('buildWebhookUpdatePatch — minimal diff', () => {
  test('an unchanged form produces an empty patch', () => {
    const sub = makeSubscription({ events: ['card.created'] });
    const patch = buildWebhookUpdatePatch(
      sub,
      makeFormState({
        url: sub.url,
        name: sub.name ?? '',
        enabled: sub.enabled,
        selected: ['card.created'],
      }),
    );
    expect(patch).toEqual({});
  });

  test('only changed fields appear', () => {
    const sub = makeSubscription({ enabled: true, events: ['card.created'] });
    const patch = buildWebhookUpdatePatch(
      sub,
      makeFormState({
        url: sub.url,
        name: sub.name ?? '',
        enabled: false,
        selected: ['card.created'],
      }),
    );
    expect(patch).toEqual({ enabled: false });
  });

  test('event-set change is order-insensitive', () => {
    const sub = makeSubscription({ events: ['card.created', 'card.moved'] });
    const patch = buildWebhookUpdatePatch(
      sub,
      makeFormState({
        url: sub.url,
        name: sub.name ?? '',
        selected: ['card.moved', 'card.created'],
      }),
    );
    expect('events' in patch).toBe(false);
  });

  test('switching to the wildcard is a change', () => {
    const sub = makeSubscription({ events: ['card.created'] });
    const patch = buildWebhookUpdatePatch(
      sub,
      makeFormState({ url: sub.url, name: sub.name ?? '', sendAll: true }),
    );
    expect(patch.events).toEqual(['*']);
  });

  test('clearing the name sends an empty string', () => {
    const sub = makeSubscription({ name: 'old name', events: ['card.created'] });
    const patch = buildWebhookUpdatePatch(
      sub,
      makeFormState({ url: sub.url, name: '', selected: ['card.created'] }),
    );
    expect(patch.name).toBe('');
  });
});
