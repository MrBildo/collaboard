import { describe, test, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { AxiosError, AxiosHeaders } from 'axios';
import type { ReactNode } from 'react';

import { WebhookFormDialog } from './WebhookFormDialog';
import type { WebhookEventGroup, WebhookSubscription } from '@/types';

vi.mock('@/lib/api', () => ({
  fetchWebhookEventCatalog: vi.fn(),
  createWebhookSubscription: vi.fn(),
  updateWebhookSubscription: vi.fn(),
}));

import { fetchWebhookEventCatalog, createWebhookSubscription } from '@/lib/api';

const mockCatalog = vi.mocked(fetchWebhookEventCatalog);
const mockCreate = vi.mocked(createWebhookSubscription);

// A persisted subscription the create mutation resolves with — its exact shape
// is irrelevant to these tests (they assert on the request, not the response).
function makeSubscription(events: string[]): WebhookSubscription {
  return {
    id: 'sub-1',
    name: null,
    url: 'https://example.com/hook',
    enabled: true,
    events,
    signed: false,
    successCount: 0,
    failureCount: 0,
    lastDeliveryStatus: null,
    lastDeliveryAtUtc: null,
  };
}

// A trimmed catalog with a non-card family — the point is that the
// picker renders whatever the server returns, not a hardcoded card-only list.
const CATALOG: WebhookEventGroup[] = [
  {
    family: 'card',
    label: 'Cards',
    events: [
      { type: 'card.created', label: 'card.created', description: 'A card is created.' },
      { type: 'card.moved', label: 'card.moved', description: 'A card moves to a different lane.' },
    ],
  },
  {
    family: 'comment',
    label: 'Comments',
    events: [
      {
        type: 'comment.created',
        label: 'comment.created',
        description: 'A comment is added to a card.',
      },
    ],
  },
];

function renderDialog() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
  }
  return render(<WebhookFormDialog open onOpenChange={() => {}} />, { wrapper: Wrapper });
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe('WebhookFormDialog event picker', () => {
  test('renders every family and event the server returns', async () => {
    mockCatalog.mockResolvedValue(CATALOG);
    renderDialog();

    // Group headings come from the fetched catalog.
    expect(await screen.findByText('Cards')).toBeInTheDocument();
    expect(screen.getByText('Comments')).toBeInTheDocument();

    // A non-card event the old hardcoded list could never have shown.
    expect(screen.getByText('comment.created')).toBeInTheDocument();
    expect(screen.getByText('A comment is added to a card.')).toBeInTheDocument();
  });

  test('shows a loading state while the catalog is fetching', () => {
    // A never-settling fetch keeps the query in its loading state.
    mockCatalog.mockReturnValue(new Promise<WebhookEventGroup[]>(() => {}));
    renderDialog();

    expect(screen.getByText('Loading events…')).toBeInTheDocument();
  });

  test('surfaces the catalog failure inline and keeps the wildcard usable', async () => {
    mockCatalog.mockRejectedValue(new Error('500'));
    renderDialog();

    // No silent failure — the error surfaces inline in the form. The catalog
    // hook retries once (a ~1s backoff) before settling, so wait past it.
    expect(
      await screen.findByText(/Couldn't load the event catalog/, undefined, { timeout: 4000 }),
    ).toBeInTheDocument();
    // "Send all events" is catalog-independent, so a failed catalog never blocks
    // creating a wildcard subscription.
    expect(screen.getByText('Send all events')).toBeInTheDocument();
  });
});

// The selection the picker shows MUST be the selection the form submits. This is
// the contract these tests lock: a build where the checkbox/count reflected a tick
// the POST body did not carry shipped because no test drove tick -> submit. These
// tests close that gap at the component seam — they exercise the real click, not
// the buildEventsPayload helper in isolation, so a future desync between what the
// picker displays and what the request sends fails here.
describe('WebhookFormDialog submit payload', () => {
  test('a ticked event reaches the create request body', async () => {
    mockCatalog.mockResolvedValue(CATALOG);
    mockCreate.mockResolvedValue(makeSubscription(['card.created']));
    const user = userEvent.setup();
    renderDialog();

    await user.type(await screen.findByLabelText('Payload URL'), 'https://example.com/hook');
    await user.click(screen.getByRole('checkbox', { name: /card\.created/i }));
    await user.click(screen.getByRole('button', { name: /create webhook/i }));

    await waitFor(() => {
      expect(mockCreate).toHaveBeenCalledWith({
        url: 'https://example.com/hook',
        events: ['card.created'],
        enabled: true,
      });
    });
  });

  test('"Send all events" submits the wildcard rather than the per-event set', async () => {
    mockCatalog.mockResolvedValue(CATALOG);
    mockCreate.mockResolvedValue(makeSubscription(['*']));
    const user = userEvent.setup();
    renderDialog();

    await user.type(await screen.findByLabelText('Payload URL'), 'https://example.com/hook');
    await user.click(screen.getByRole('switch', { name: /send all events/i }));
    await user.click(screen.getByRole('button', { name: /create webhook/i }));

    await waitFor(() => {
      expect(mockCreate).toHaveBeenCalledWith({
        url: 'https://example.com/hook',
        events: ['*'],
        enabled: true,
      });
    });
  });
});

// The signed/unsigned indicator must track what the operator is typing, not just
// the persisted state — otherwise the create flow shows "Unsigned" the whole time
// a secret is being entered. And it must agree with the submit path: a
// whitespace-only secret reads as Unsigned AND is not sent as a secret (an
// invisible HMAC key in a masked field is a fat-finger, not a credential).
describe('WebhookFormDialog secret indicator', () => {
  test('typing a secret flips the indicator to Signed in the create flow', async () => {
    mockCatalog.mockResolvedValue(CATALOG);
    const user = userEvent.setup();
    renderDialog();

    // A fresh create dialog starts Unsigned.
    expect(await screen.findByText('Unsigned')).toBeInTheDocument();

    await user.type(screen.getByLabelText(/signing secret/i), 'my-secret');

    expect(screen.getByText('Signed')).toBeInTheDocument();
    expect(screen.queryByText('Unsigned')).not.toBeInTheDocument();
  });

  test('a whitespace-only secret stays Unsigned and is not submitted as a secret', async () => {
    mockCatalog.mockResolvedValue(CATALOG);
    mockCreate.mockResolvedValue(makeSubscription(['card.created']));
    const user = userEvent.setup();
    renderDialog();

    await user.type(await screen.findByLabelText('Payload URL'), 'https://example.com/hook');
    await user.type(screen.getByLabelText(/signing secret/i), '   ');

    // The indicator agrees with the submit gate: whitespace is not a secret.
    expect(screen.getByText('Unsigned')).toBeInTheDocument();
    expect(screen.queryByText('Signed')).not.toBeInTheDocument();

    await user.click(screen.getByRole('checkbox', { name: /card\.created/i }));
    await user.click(screen.getByRole('button', { name: /create webhook/i }));

    // No secret key in the request body — the whitespace was dropped, not signed with.
    await waitFor(() => {
      expect(mockCreate).toHaveBeenCalledWith({
        url: 'https://example.com/hook',
        events: ['card.created'],
        enabled: true,
      });
    });
  });
});

// When a create is rejected, the operator's attention is on this dialog, so the
// failure surfaces inline here. The content that surfaces must be the server's
// actionable message — not axios's generic "Request failed with status code
// 400", which is what buried the real SSRF diagnosis. The API returns the
// message as a bare JSON string on its validation 400s (Results.BadRequest(msg)),
// so axios parses error.response.data to that string.
describe('WebhookFormDialog error surfacing', () => {
  const SSRF_MESSAGE =
    "Webhook host 'collaboard.collabot.dev' resolves to a private or otherwise blocked " +
    'address (192.168.50.135); set Webhooks:AllowPrivateNetworkTargets to allow it.';

  function badRequest(body: unknown): AxiosError {
    const response = {
      data: body,
      status: 400,
      statusText: 'Bad Request',
      headers: {},
      config: { headers: new AxiosHeaders() },
    };
    return new AxiosError(
      'Request failed with status code 400',
      'ERR_BAD_REQUEST',
      undefined,
      undefined,
      // The AxiosResponse cast mirrors the shape axios builds; the test only
      // reads .data / .status off it.
      response as AxiosError['response'],
    );
  }

  test("renders the server's 400 message inline, not the generic axios string", async () => {
    mockCatalog.mockResolvedValue(CATALOG);
    mockCreate.mockRejectedValue(badRequest(SSRF_MESSAGE));
    const user = userEvent.setup();
    renderDialog();

    await user.type(
      await screen.findByLabelText('Payload URL'),
      'https://collaboard.collabot.dev/hook',
    );
    await user.click(screen.getByRole('checkbox', { name: /card\.created/i }));
    await user.click(screen.getByRole('button', { name: /create webhook/i }));

    // The actionable server message reaches the inline error...
    expect(
      await screen.findByText(/resolves to a private or otherwise blocked address/),
    ).toBeInTheDocument();
    // ...and the generic axios status-code string never reaches the operator.
    expect(screen.queryByText('Request failed with status code 400')).not.toBeInTheDocument();
  });
});
