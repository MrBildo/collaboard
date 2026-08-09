import { describe, test, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { CardComments } from './CardComments';
import { fetchComments } from '@/lib/api';
import { formatDateTime } from '@/lib/utils';
import type { CardComment } from '@/types';

vi.mock('@/lib/api', () => ({
  fetchComments: vi.fn(),
  createComment: vi.fn(),
  updateComment: vi.fn(),
  deleteComment: vi.fn(),
}));

vi.mock('@/hooks/use-user-directory', () => ({
  useUserDirectory: () => ({
    getUserName: (id: string) => (id === 'user-1' ? 'Bot Cora' : 'Unknown'),
  }),
}));

vi.mock('@/hooks/use-card-links', () => ({
  useCardLinkContext: () => ({
    boardSlug: 'collaboard',
    cardNumbers: new Set<number>(),
    cardPreviews: new Map(),
  }),
}));

// MarkdownRenderer pulls in the real mermaid package for the comment body; keep
// it inert in jsdom.
vi.mock('mermaid', () => ({
  default: { initialize: vi.fn(), render: vi.fn() },
}));

const mockedFetchComments = vi.mocked(fetchComments);

const POSTED_AT = '2026-08-09T15:54:26.381Z';
const EDITED_AT = '2026-08-09T16:16:13.537Z';

function makeComment(overrides: Partial<CardComment> = {}): CardComment {
  return {
    id: 'comment-1',
    cardId: 'card-1',
    userId: 'user-1',
    contentMarkdown: 'A comment body',
    // Default: never edited — created and last-updated are identical, as the
    // backend stamps them at posting.
    createdAtUtc: POSTED_AT,
    lastUpdatedAtUtc: POSTED_AT,
    ...overrides,
  };
}

function renderComments() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <CardComments cardId="card-1" readOnly />
      </QueryClientProvider>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe('CardComments edited marker', () => {
  test('shows an "(edited)" marker when a comment has been edited', async () => {
    mockedFetchComments.mockResolvedValue([
      makeComment({ createdAtUtc: POSTED_AT, lastUpdatedAtUtc: EDITED_AT }),
    ]);

    renderComments();

    expect(await screen.findByText('(edited)')).toBeInTheDocument();
  });

  test('shows no marker for a comment that was never edited', async () => {
    mockedFetchComments.mockResolvedValue([makeComment()]);

    renderComments();

    // Wait for the comment to render, then assert the marker is absent.
    expect(await screen.findByText('A comment body')).toBeInTheDocument();
    expect(screen.queryByText('(edited)')).not.toBeInTheDocument();
  });

  test('exposes the original posting time on the edited marker', async () => {
    mockedFetchComments.mockResolvedValue([
      makeComment({ createdAtUtc: POSTED_AT, lastUpdatedAtUtc: EDITED_AT }),
    ]);

    renderComments();

    const marker = await screen.findByText('(edited)');
    expect(marker).toHaveAttribute('title', `Originally posted ${formatDateTime(POSTED_AT)}`);
  });
});
