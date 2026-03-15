import { useEffect } from 'react';
import { useQueryClient } from '@tanstack/react-query';

export function useBoardEvents(boardId: string | undefined) {
  const queryClient = useQueryClient();

  useEffect(() => {
    if (!boardId) {
      return;
    }

    const es = new EventSource(`/api/v1/boards/${boardId}/events`);

    es.addEventListener('board-updated', () => {
      queryClient.invalidateQueries({ queryKey: ['boardData', boardId] });
      queryClient.invalidateQueries({ queryKey: ['labels'] });
      queryClient.invalidateQueries({ queryKey: ['cardLabels'] });
      queryClient.invalidateQueries({ queryKey: ['comments'] });
      queryClient.invalidateQueries({ queryKey: ['attachments'] });
      queryClient.invalidateQueries({ queryKey: ['userDirectory'] });
    });

    es.onerror = () => {
      // EventSource auto-reconnects — no action needed
    };

    return () => es.close();
  }, [queryClient, boardId]);
}
