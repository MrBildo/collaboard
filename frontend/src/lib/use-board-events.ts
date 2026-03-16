import { useEffect } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { queryKeys } from '@/lib/query-keys';

export function useBoardEvents(boardId: string | undefined) {
  const queryClient = useQueryClient();

  useEffect(() => {
    if (!boardId) {
      return;
    }

    const es = new EventSource(`/api/v1/boards/${boardId}/events`);

    es.addEventListener('board-updated', () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.boards.data(boardId) });
      queryClient.invalidateQueries({ queryKey: queryKeys.labels.all(boardId) });
      queryClient.invalidateQueries({ queryKey: queryKeys.users.directory() });
      // Invalidate all card-scoped data (comments, labels, attachments) by prefix
      queryClient.invalidateQueries({ queryKey: ['cards'] });
    });

    es.onerror = () => {
      console.error('[SSE] Connection error for board', boardId);
    };

    return () => es.close();
  }, [queryClient, boardId]);
}
