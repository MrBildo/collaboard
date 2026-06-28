import { describe, test, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

import { WebhookFormDialog } from './WebhookFormDialog';
import type { WebhookEventGroup } from '@/types';

vi.mock('@/lib/api', () => ({
  fetchWebhookEventCatalog: vi.fn(),
  createWebhookSubscription: vi.fn(),
  updateWebhookSubscription: vi.fn(),
}));

import { fetchWebhookEventCatalog } from '@/lib/api';

const mockCatalog = vi.mocked(fetchWebhookEventCatalog);

// A trimmed catalog with a non-card family — the point of #336 is that the
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
