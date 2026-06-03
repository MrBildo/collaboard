import { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useParams } from 'react-router-dom';
import { fetchBoardData } from '@/lib/api';
import { queryKeys } from '@/lib/query-keys';
import { QUERY_DEFAULTS } from '@/lib/query-config';

export type CardLinkContext = {
  boardSlug: string | undefined;
  cardNumbers: Set<number>;
};

// Supplies the `MarkdownRenderer` autolinking inputs (card #273): the current
// board slug (from the route, for the relative link href) and the set of live
// (non-archived) card numbers on the board (for fork-3a validation). Both come
// from data already loaded by the board view — the slug from the URL, the card
// numbers from the composite board-data cache. Subscribing to the same query
// key dedupes against the parent's existing subscription, so this adds no fetch.
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

  return { boardSlug: slug, cardNumbers };
}
