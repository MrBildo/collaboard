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
    // Board action — operator's attention has moved; the floor toasts it
    // (card #203, spec §2b). Success is silent: the card leaves the lane,
    // which is self-evident (spec §3).
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
    // No onError: the floor handles the toast (spec §5 Rule 1 — console.error retired).
  });
}
