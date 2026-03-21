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

  const { prevCard, nextCard } = useMemo(() => {
    if (!card || !cardsInLane || cardsInLane.length === 0)
      return { prevCard: null, nextCard: null };
    const idx = cardsInLane.findIndex((c) => c.id === card.id);
    if (idx === -1) return { prevCard: null, nextCard: null };
    return {
      prevCard: idx > 0 ? cardsInLane[idx - 1] : null,
      nextCard: idx < cardsInLane.length - 1 ? cardsInLane[idx + 1] : null,
    };
  }, [card, cardsInLane]);

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

  const handleUnsavedAction = useCallback(
    (action: UnsavedChangesAction) => {
      const pending = pendingAction;
      setPendingAction(null);

      if (action === 'cancel') return;

      if (action === 'save') {
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
    cardsInLane && cardsInLane.length > 1
      ? `${(cardsInLane.findIndex((c) => c.id === card.id) ?? 0) + 1} / ${cardsInLane.length}`
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
      <UnsavedChangesDialog open={pendingAction !== null} onAction={handleUnsavedAction} />
    </>
  );
}
