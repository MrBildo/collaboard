import { describe, test, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { CardLinkPreview, type CardLinkPreviewData } from './CardLinkPreview';
import type { CardSummary } from '@/types';

function makeCard(overrides: Partial<CardSummary> = {}): CardSummary {
  return {
    id: 'card-1',
    number: 42,
    name: 'Implement the thing',
    descriptionMarkdown: 'desc',
    sizeId: 'size-1',
    sizeName: 'M',
    laneId: 'lane-1',
    position: 0,
    isArchived: false,
    createdByUserId: 'u1',
    createdAtUtc: '2026-01-01T00:00:00Z',
    lastUpdatedByUserId: 'u1',
    lastUpdatedAtUtc: '2026-01-01T00:00:00Z',
    labels: [],
    commentCount: 0,
    attachmentCount: 0,
    ...overrides,
  };
}

function makeData(overrides: Partial<CardLinkPreviewData> = {}): CardLinkPreviewData {
  return { card: makeCard(), laneName: 'In Progress', ...overrides };
}

describe('CardLinkPreview', () => {
  test('renders the card title, number, lane, and size', () => {
    render(<CardLinkPreview data={makeData()} />);

    expect(screen.getByText('Implement the thing')).toBeInTheDocument();
    expect(screen.getByText('#42')).toBeInTheDocument();
    expect(screen.getByText('In Progress')).toBeInTheDocument();
    expect(screen.getByText('M')).toBeInTheDocument();
  });

  test('renders label chips when the card has labels', () => {
    const data = makeData({
      card: makeCard({
        labels: [
          { id: 'l1', name: 'Bug', color: '#ff0000' },
          { id: 'l2', name: 'Improvement', color: null },
        ],
      }),
    });
    render(<CardLinkPreview data={data} />);

    expect(screen.getByText('Bug')).toBeInTheDocument();
    expect(screen.getByText('Improvement')).toBeInTheDocument();
  });

  test('omits comment and attachment counts when zero', () => {
    render(<CardLinkPreview data={makeData()} />);

    // Counts only render when > 0 — no stray "0" badges in the meta row.
    expect(screen.queryByText('0')).toBeNull();
  });

  test('shows comment and attachment counts when present', () => {
    const data = makeData({ card: makeCard({ commentCount: 3, attachmentCount: 2 }) });
    render(<CardLinkPreview data={data} />);

    expect(screen.getByText('3')).toBeInTheDocument();
    expect(screen.getByText('2')).toBeInTheDocument();
  });
});
