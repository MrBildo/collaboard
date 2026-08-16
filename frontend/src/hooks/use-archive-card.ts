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
    // Board action — the operator's attention has moved, so the global error
    // floor toasts a failure rather than showing it inline. Success is silent:
    // the card visibly leaves the lane, which is confirmation enough.
    meta: { errorMessage: "Couldn't archive card" },
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
    // No onError: the global mutation-error floor handles the toast and the developer log.
  });
}
