import { useEffect, useRef } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { queryKeys } from '@/lib/query-keys';
import { getApiBaseUrl } from '@/lib/runtime-config';

const SSE_DEBOUNCE_MS = 300;

export function useBoardEvents(boardId: string | undefined) {
  const queryClient = useQueryClient();
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    if (!boardId) {
      return;
    }

    // withCredentials lets the cross-origin EventSource participate in the
    // CORS contract (the API echoes Access-Control-Allow-Origin via
    // .RequireCors on the SSE endpoint). The same-origin LAN release ignores
    // the flag harmlessly. getApiBaseUrl() includes the /api/v1 suffix.
    const es = new EventSource(`${getApiBaseUrl()}/boards/${boardId}/events`, {
      withCredentials: true,
    });

    es.addEventListener('board-updated', () => {
      if (debounceRef.current) clearTimeout(debounceRef.current);
      debounceRef.current = setTimeout(() => {
        queryClient.cancelQueries({ queryKey: queryKeys.boards.data(boardId) });
        queryClient.invalidateQueries({ queryKey: queryKeys.boards.data(boardId) });
        queryClient.invalidateQueries({ queryKey: queryKeys.boards.cards(boardId) });
        queryClient.invalidateQueries({ queryKey: queryKeys.labels.all(boardId) });
        queryClient.invalidateQueries({ queryKey: queryKeys.users.directory() });
        // Description edits ring this same bell (no dedicated event type), so
        // the open card's history gate and trail refresh alongside the board
        // data. The bell doesn't say which card changed; the predicate marks
        // every card's history queries stale, and only mounted ones refetch.
        queryClient.invalidateQueries({
          predicate: (query) => query.queryKey[0] === 'cards' && query.queryKey[2] === 'history',
        });
      }, SSE_DEBOUNCE_MS);
    });

    es.onerror = () => {
      if (import.meta.env.DEV) console.error('[SSE] Connection error for board', boardId);
    };

    return () => {
      if (debounceRef.current) clearTimeout(debounceRef.current);
      es.close();
    };
  }, [queryClient, boardId]);
}
