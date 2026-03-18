import { useQuery } from '@tanstack/react-query';
import { useEffect, useMemo } from 'react';
import { fetchBoardBySlug, fetchBoardData } from '@/lib/api';
import { setLastBoardSlug } from '@/lib/auth';
import { QUERY_DEFAULTS } from '@/lib/query-config';
import { queryKeys } from '@/lib/query-keys';
import type { CardItem, CardSummary } from '@/types';

export function useBoardData(
  slug: string | undefined,
  loggedIn: boolean,
) {
  // Fetch the board metadata by slug
  const boardMetaQuery = useQuery({
    queryKey: queryKeys.boards.detail(slug as string),
    queryFn: () => fetchBoardBySlug(slug as string),
    enabled: loggedIn && !!slug,
    ...QUERY_DEFAULTS.boards,
  });
  const board = boardMetaQuery.data;
  const boardId = board?.id;

  // Track last visited board
  useEffect(() => {
    if (slug) {
      setLastBoardSlug(slug);
    }
  }, [slug]);

  // Fetch board data (lanes + cards)
  const boardDataQuery = useQuery({
    queryKey: queryKeys.boards.data(boardId as string),
    queryFn: () => fetchBoardData(boardId as string),
    retry: 2,
    staleTime: 30_000,
    enabled: loggedIn && !!boardId,
    refetchOnWindowFocus: false,
  });

  // Enriched card map — derived from composite board data (now includes CardSummary)
  const enrichedCardMap = useMemo(() => {
    const map = new Map<string, CardSummary>();
    for (const card of boardDataQuery.data?.cards ?? []) {
      map.set(card.id, card);
    }
    return map;
  }, [boardDataQuery.data]);

  const lanes = useMemo(() => boardDataQuery.data?.lanes ?? [], [boardDataQuery.data]);
  const sizes = useMemo(() => boardDataQuery.data?.sizes ?? [], [boardDataQuery.data]);
  const sizeMap = useMemo(() => new Map(sizes.map((s) => [s.id, s.name])), [sizes]);
  const serverCards = useMemo(() => boardDataQuery.data?.cards ?? [], [boardDataQuery.data]);

  return {
    board,
    boardId,
    boardMetaQuery,
    boardDataQuery,
    lanes,
    sizes,
    sizeMap,
    serverCards,
    enrichedCardMap,
  };
}
