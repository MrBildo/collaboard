import { useMutation, useQueryClient } from '@tanstack/react-query';
import { restoreCard } from '@/lib/api';
import { queryKeys } from '@/lib/query-keys';

type UseRestoreCardOptions = {
  cardId: string;
  boardId?: string;
  onSuccess?: () => void;
};

export function useRestoreCard({ cardId, boardId, onSuccess }: UseRestoreCardOptions) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (laneId: string) => restoreCard(cardId, laneId),
    onSuccess: () => {
      if (boardId) {
        queryClient.invalidateQueries({ queryKey: queryKeys.boards.data(boardId) });
      }
      onSuccess?.();
    },
    onError: (error: unknown) => {
      console.error('Failed to restore card:', error);
    },
  });
}
