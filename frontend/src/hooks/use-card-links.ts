import { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useParams } from 'react-router-dom';
import type { CardLinkPreviewData } from '@/components/CardLinkPreview';
import { fetchBoardData } from '@/lib/api';
import { queryKeys } from '@/lib/query-keys';
import { QUERY_DEFAULTS } from '@/lib/query-config';

export type CardLinkContext = {
  boardSlug: string | undefined;
  cardNumbers: Set<number>;
  cardPreviews: Map<number, CardLinkPreviewData>;
};

// Supplies the `MarkdownRenderer` autolinking inputs (card #273) and the
// hover-preview data (card #283): the current board slug (from the route, for
// the relative link href), the set of live (non-archived) card numbers on the
// board (for fork-3a validation), and a per-number preview map (CardSummary +
// lane name). All come from data already loaded by the board view — the slug
// from the URL, the rest from the composite board-data cache. Subscribing to
// the same query key dedupes against the parent's existing subscription, so
// this adds no fetch, and the preview reads straight from cache (no
// fetch-on-hover).
export function useCardLinkContext(boardId: string | undefined): CardLinkContext {
  const { slug } = useParams<{ slug: string }>();

  const boardDataQuery = useQuery({
    queryKey: queryKeys.boards.data(boardId as string),
    queryFn: () => fetchBoardData(boardId as string),
    enabled: !!boardId,
    ...QUERY_DEFAULTS.boardData,
  });

  const cardNumbers = useMemo(
    () => new Set((boardDataQuery.data?.cards ?? []).map((card) => card.number)),
    [boardDataQuery.data],
  );

  const cardPreviews = useMemo(() => {
    const data = boardDataQuery.data;
    const map = new Map<number, CardLinkPreviewData>();
    if (!data) return map;

    const laneNames = new Map(data.lanes.map((lane) => [lane.id, lane.name]));
    for (const card of data.cards) {
      map.set(card.number, { card, laneName: laneNames.get(card.laneId) ?? 'Unknown lane' });
    }
    return map;
  }, [boardDataQuery.data]);

  return { boardSlug: slug, cardNumbers, cardPreviews };
}
