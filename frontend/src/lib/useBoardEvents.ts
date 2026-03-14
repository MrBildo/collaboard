import { useEffect } from 'react';
import { useQueryClient } from '@tanstack/react-query';

export function useBoardEvents(enabled: boolean) {
  const queryClient = useQueryClient();

  useEffect(() => {
    if (!enabled) {
      return;
    }

    const es = new EventSource('/api/v1/board/events');

    es.addEventListener('board-updated', () => {
      queryClient.invalidateQueries({ queryKey: ['board'] });
      queryClient.invalidateQueries({ queryKey: ['labels'] });
      queryClient.invalidateQueries({ queryKey: ['userDirectory'] });
    });

    es.onerror = () => {
      // EventSource auto-reconnects — no action needed
    };

    return () => es.close();
  }, [queryClient, enabled]);
}
