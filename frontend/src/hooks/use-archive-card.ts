import { useMutation, useQueryClient } from '@tanstack/react-query';
import { archiveCard } from '@/lib/api';
import { queryKeys } from '@/lib/query-keys';
import type { BoardData } from '@/types';

type UseArchiveCardOptions = {
  cardId: string;
  boardId?: string;
  onSuccess?: () => void;
};

export function useArchiveCard({ cardId, boardId, onSuccess }: UseArchiveCardOptions) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => archiveCard(cardId),
    onSuccess: () => {
      if (boardId) {
        queryClient.setQueryData<BoardData>(queryKeys.boards.data(boardId), (old) =>
          old ? { ...old, cards: old.cards.filter((c) => c.id !== cardId) } : old,
        );
        queryClient.invalidateQueries({ queryKey: queryKeys.boards.data(boardId) });
      }
      onSuccess?.();
    },
    onError: (error: unknown) => {
      console.error('Failed to archive card:', error);
    },
  });
}
