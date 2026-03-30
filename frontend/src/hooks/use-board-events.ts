import { useEffect, useRef } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { queryKeys } from '@/lib/query-keys';

const SSE_DEBOUNCE_MS = 300;

export function useBoardEvents(boardId: string | undefined) {
  const queryClient = useQueryClient();
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    if (!boardId) {
      return;
    }

    const es = new EventSource(`/api/v1/boards/${boardId}/events`);

    es.addEventListener('board-updated', () => {
      if (debounceRef.current) clearTimeout(debounceRef.current);
      debounceRef.current = setTimeout(() => {
        queryClient.cancelQueries({ queryKey: queryKeys.boards.data(boardId) });
        queryClient.invalidateQueries({ queryKey: queryKeys.boards.data(boardId) });
        queryClient.invalidateQueries({ queryKey: queryKeys.boards.cards(boardId) });
        queryClient.invalidateQueries({ queryKey: queryKeys.labels.all(boardId) });
        queryClient.invalidateQueries({ queryKey: queryKeys.users.directory() });
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
