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
    // Board action (no inline surface to attach to) — the global error floor toasts a failure.
    meta: { errorMessage: "Couldn't restore card" },
    mutationFn: (laneId: string) => restoreCard(cardId, laneId),
    onSuccess: () => {
      if (boardId) {
        queryClient.invalidateQueries({ queryKey: queryKeys.boards.data(boardId) });
      }
      onSuccess?.();
    },
    // No onError: the floor handles the toast.
  });
}
