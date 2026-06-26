import { describe, test, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { WebhooksTab } from './WebhooksTab';
import type { WebhookDelivery, WebhookSubscription } from '@/types';

vi.mock('@/lib/api', () => ({
  fetchWebhookSubscriptions: vi.fn(),
  fetchWebhookDeliveries: vi.fn(),
  fetchWebhookStatus: vi.fn(),
  createWebhookSubscription: vi.fn(),
  updateWebhookSubscription: vi.fn(),
  deleteWebhookSubscription: vi.fn(),
  testWebhookSubscription: vi.fn(),
}));

import { fetchWebhookSubscriptions, fetchWebhookDeliveries, fetchWebhookStatus } from '@/lib/api';

const mockSubs = vi.mocked(fetchWebhookSubscriptions);
const mockDeliveries = vi.mocked(fetchWebhookDeliveries);
const mockStatus = vi.mocked(fetchWebhookStatus);

function makeSub(overrides: Partial<WebhookSubscription> = {}): WebhookSubscription {
  return {
    id: 'sub-1',
    name: 'Zapier prod',
    url: 'https://hooks.zapier.com/catch/1/abc',
    enabled: true,
    events: ['card.created', 'card.moved'],
    signed: true,
    successCount: 1428,
    failureCount: 3,
    lastDeliveryStatus: 'Succeeded',
    lastDeliveryAtUtc: '2026-06-26T11:58:00Z',
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
    attemptedAtUtc: '2026-06-26T11:58:00Z',
    ...overrides,
  };
}

function renderTab() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
  }
  return render(<WebhooksTab />, { wrapper: Wrapper });
}

beforeEach(() => {
  vi.clearAllMocks();
  mockDeliveries.mockResolvedValue({ items: [], totalCount: 0, offset: 0, limit: 200 });
  mockStatus.mockResolvedValue({
    enabled: true,
    allowPrivateNetworkTargets: false,
    subscriptionCount: 0,
    enabledSubscriptionCount: 0,
  });
});

describe('WebhooksTab', () => {
  test('shows the empty state when there are no subscriptions', async () => {
    mockSubs.mockResolvedValue([]);
    renderTab();
    expect(await screen.findByText('No webhooks yet')).toBeInTheDocument();
    expect(screen.getByText('Add your first webhook')).toBeInTheDocument();
  });

  test('renders a subscription row with name, url and signed state', async () => {
    mockSubs.mockResolvedValue([makeSub()]);
    renderTab();
    expect(await screen.findByText('Zapier prod')).toBeInTheDocument();
    expect(screen.getByText('https://hooks.zapier.com/catch/1/abc')).toBeInTheDocument();
    expect(screen.getByText('Signed')).toBeInTheDocument();
    // The success/failure counts surface in the reliability column.
    expect(screen.getByText('1,428')).toBeInTheDocument();
  });

  test('shows the wildcard as "all" and an unsigned badge', async () => {
    mockSubs.mockResolvedValue([
      makeSub({ id: 'sub-2', name: 'Analytics', events: ['*'], signed: false }),
    ]);
    renderTab();
    await screen.findByText('Analytics');
    expect(screen.getByText('Unsigned')).toBeInTheDocument();
    expect(screen.getByText('* all')).toBeInTheDocument();
  });

  test('surfaces the SSRF "Blocked" state when the last attempt is a private-target block', async () => {
    mockSubs.mockResolvedValue([
      makeSub({ id: 'sub-3', name: 'n8n prod', lastDeliveryStatus: 'Failed' }),
    ]);
    mockDeliveries.mockResolvedValue({
      items: [
        makeDelivery({
          subscriptionId: 'sub-3',
          status: 'Failed',
          httpStatusCode: null,
          error: "Webhook host 'n8n.internal' resolves to a blocked address (10.0.0.5).",
        }),
      ],
      totalCount: 1,
      offset: 0,
      limit: 200,
    });
    renderTab();
    await screen.findByText('n8n prod');
    expect(screen.getByText('Blocked')).toBeInTheDocument();
    expect(screen.getByText('Private target')).toBeInTheDocument();
  });

  test('reflects the global "private targets blocked" posture', async () => {
    mockSubs.mockResolvedValue([makeSub()]);
    renderTab();
    await screen.findByText('Zapier prod');
    expect(screen.getByText('Private targets blocked')).toBeInTheDocument();
  });

  test('warns when delivery is globally disabled', async () => {
    mockSubs.mockResolvedValue([makeSub()]);
    mockStatus.mockResolvedValue({
      enabled: false,
      allowPrivateNetworkTargets: false,
      subscriptionCount: 1,
      enabledSubscriptionCount: 1,
    });
    renderTab();
    await screen.findByText('Zapier prod');
    expect(screen.getByText('Delivery globally disabled')).toBeInTheDocument();
  });

  test('never renders a secret value (only the signed/unsigned state)', async () => {
    // The contract is secret-free; assert the rendered DOM carries no secret-like
    // field. The signed boolean is the only secret-derived surface.
    mockSubs.mockResolvedValue([makeSub({ signed: true })]);
    const { container } = renderTab();
    await screen.findByText('Zapier prod');
    expect(container.textContent).not.toMatch(/secret/i);
  });
});
