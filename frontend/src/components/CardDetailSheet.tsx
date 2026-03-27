import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Dialog, DialogContent } from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { isTextInputFocused } from '@/lib/utils';
import { CardDetailForm } from '@/components/CardDetailForm';
import type { CardDetailFormHandle } from '@/components/CardDetailForm';
import { UnsavedChangesDialog } from '@/components/UnsavedChangesDialog';
import { ChevronLeft, ChevronRight } from 'lucide-react';
import type { CardItem, CardSize, Lane } from '@/types';
import type { UnsavedChangesAction } from '@/components/UnsavedChangesDialog';

// Snapshot navigation context so lane changes don't shift prev/next nav.
// Uses "setState during render" pattern — React allows this when value differs.
function useNavSnapshot(card: CardItem | null, cardsInLane: CardItem[] | undefined) {
  const [snapshot, setSnapshot] = useState<{ cardId: string | null; cards: CardItem[] }>({
    cardId: null,
    cards: [],
  });

  const cardId = card?.id ?? null;
  const cards = cardsInLane ?? [];

  // Card changed — take fresh snapshot (setState during render is OK for derived state)
  if (cardId !== snapshot.cardId) {
    setSnapshot({ cardId, cards });
    return cards;
  }

  // Same card, and it's still present in the lane list — update snapshot
  if (cardId && cards.some((c) => c.id === cardId) && cards !== snapshot.cards) {
    setSnapshot({ cardId, cards });
    return cards;
  }

  // Card moved to different lane — keep frozen snapshot
  return snapshot.cards;
}

type PendingAction = { type: 'close' } | { type: 'navigate'; cardNumber: number };

type CardDetailSheetProps = {
  card: CardItem | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  currentUserId?: string;
  currentUserRole?: number;
  lanes?: Lane[];
  boardId?: string;
  sizes?: CardSize[];
  cardsInLane?: CardItem[];
  onNavigateCard?: (cardNumber: number) => void;
};

export function CardDetailSheet({
  card,
  open,
  onOpenChange,
  currentUserId,
  currentUserRole,
  lanes,
  boardId,
  sizes,
  cardsInLane,
  onNavigateCard,
}: CardDetailSheetProps) {
  const isDirtyRef = useRef(false);
  const formRef = useRef<CardDetailFormHandle>(null);
  const [pendingAction, setPendingAction] = useState<PendingAction | null>(null);

  const navSnapshot = useNavSnapshot(card, cardsInLane);

  const { prevCard, nextCard } = useMemo(() => {
    if (!card || navSnapshot.length === 0) return { prevCard: null, nextCard: null };
    const idx = navSnapshot.findIndex((c) => c.id === card.id);
    if (idx === -1) return { prevCard: null, nextCard: null };
    return {
      prevCard: idx > 0 ? navSnapshot[idx - 1] : null,
      nextCard: idx < navSnapshot.length - 1 ? navSnapshot[idx + 1] : null,
    };
  }, [card, navSnapshot]);

  const executePendingAction = useCallback(
    (action: PendingAction) => {
      if (action.type === 'close') {
        onOpenChange(false);
      } else if (action.type === 'navigate' && onNavigateCard) {
        onNavigateCard(action.cardNumber);
      }
    },
    [onOpenChange, onNavigateCard],
  );

  // Ref to hold the pending action that should be executed after save completes
  const pendingAfterSaveRef = useRef<PendingAction | null>(null);

  const handleSaveComplete = useCallback(() => {
    const action = pendingAfterSaveRef.current;
    pendingAfterSaveRef.current = null;
    if (action) {
      executePendingAction(action);
    }
  }, [executePendingAction]);

  const handleUnsavedAction = useCallback(
    (action: UnsavedChangesAction) => {
      const pending = pendingAction;
      setPendingAction(null);

      if (action === 'cancel') return;

      if (action === 'save') {
        // Store the pending action so handleSaveComplete can execute it after save
        pendingAfterSaveRef.current = pending;
        formRef.current?.save();
        return;
      }

      // discard
      isDirtyRef.current = false;
      if (pending) executePendingAction(pending);
    },
    [pendingAction, executePendingAction],
  );

  const requestAction = useCallback(
    (action: PendingAction) => {
      if (isDirtyRef.current) {
        setPendingAction(action);
        return;
      }
      executePendingAction(action);
    },
    [executePendingAction],
  );

  const handleNavigate = useCallback(
    (direction: 'prev' | 'next') => {
      const target = direction === 'prev' ? prevCard : nextCard;
      if (!target || !onNavigateCard) return;
      requestAction({ type: 'navigate', cardNumber: target.number });
    },
    [prevCard, nextCard, onNavigateCard, requestAction],
  );

  useEffect(() => {
    if (!open) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (isTextInputFocused()) return;
      if (e.key === 'ArrowLeft' && prevCard) {
        e.preventDefault();
        handleNavigate('prev');
      } else if (e.key === 'ArrowRight' && nextCard) {
        e.preventDefault();
        handleNavigate('next');
      }
    };
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [open, prevCard, nextCard, handleNavigate]);

  const handleDialogOpenChange = useCallback(
    (nextOpen: boolean) => {
      if (!nextOpen) {
        requestAction({ type: 'close' });
        return;
      }
      onOpenChange(nextOpen);
    },
    [onOpenChange, requestAction],
  );

  if (!card) return null;

  const navPosition =
    navSnapshot.length > 1
      ? `${(navSnapshot.findIndex((c) => c.id === card.id) ?? 0) + 1} / ${navSnapshot.length}`
      : null;

  return (
    <>
      <Dialog open={open} onOpenChange={handleDialogOpenChange}>
        <DialogContent
          data-mobile-fullscreen
          className="flex flex-col p-0 md:max-h-[85vh] md:!w-[80vw] md:!max-w-[80vw]"
          style={{ overflow: 'visible' }}
        >
          {/* Floating nav buttons — desktop only, positioned outside the dialog */}
          {prevCard && (
            <Button
              variant="outline"
              size="icon"
              onClick={() => handleNavigate('prev')}
              onPointerDown={(e) => e.stopPropagation()}
              className="absolute top-1/2 -left-14 z-50 hidden -translate-y-1/2 rounded-full shadow-lg md:flex"
              aria-label="Previous card"
            >
              <ChevronLeft className="h-5 w-5" />
            </Button>
          )}
          {nextCard && (
            <Button
              variant="outline"
              size="icon"
              onClick={() => handleNavigate('next')}
              onPointerDown={(e) => e.stopPropagation()}
              className="absolute top-1/2 -right-14 z-50 hidden -translate-y-1/2 rounded-full shadow-lg md:flex"
              aria-label="Next card"
            >
              <ChevronRight className="h-5 w-5" />
            </Button>
          )}
          <div className="flex min-h-0 flex-1 flex-col overflow-hidden">
            <CardDetailForm
              ref={formRef}
              key={card.id}
              card={card}
              onClose={() => handleDialogOpenChange(false)}
              onSaveComplete={handleSaveComplete}
              currentUserId={currentUserId}
              currentUserRole={currentUserRole}
              lanes={lanes}
              boardId={boardId}
              sizes={sizes}
              isDirtyRef={isDirtyRef}
              navPosition={navPosition}
              onNavigatePrev={prevCard ? () => handleNavigate('prev') : undefined}
              onNavigateNext={nextCard ? () => handleNavigate('next') : undefined}
            />
          </div>
        </DialogContent>
      </Dialog>
      <UnsavedChangesDialog
        open={pendingAction !== null}
        onAction={handleUnsavedAction}
        actionType={pendingAction?.type}
      />
    </>
  );
}
