import { useCallback, useEffect, useMemo, useRef } from 'react';
import { Dialog, DialogContent } from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { isTextInputFocused } from '@/lib/utils';
import { CardDetailForm } from '@/components/CardDetailForm';
import { ChevronLeft, ChevronRight } from 'lucide-react';
import type { CardItem, CardSize, Lane } from '@/types';

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

  const { prevCard, nextCard } = useMemo(() => {
    if (!card || !cardsInLane || cardsInLane.length === 0) return { prevCard: null, nextCard: null };
    const idx = cardsInLane.findIndex((c) => c.id === card.id);
    if (idx === -1) return { prevCard: null, nextCard: null };
    return {
      prevCard: idx > 0 ? cardsInLane[idx - 1] : null,
      nextCard: idx < cardsInLane.length - 1 ? cardsInLane[idx + 1] : null,
    };
  }, [card, cardsInLane]);

  const handleNavigate = useCallback(
    (direction: 'prev' | 'next') => {
      const target = direction === 'prev' ? prevCard : nextCard;
      if (!target || !onNavigateCard) return;
      if (isDirtyRef.current) {
        if (!window.confirm('You have unsaved changes. Discard them?')) return;
      }
      isDirtyRef.current = false;
      onNavigateCard(target.number);
    },
    [prevCard, nextCard, onNavigateCard],
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
        if (isDirtyRef.current) {
          if (!window.confirm('You have unsaved changes. Discard them?')) return;
        }
      }
      onOpenChange(nextOpen);
    },
    [onOpenChange],
  );

  if (!card) return null;

  const navPosition = cardsInLane && cardsInLane.length > 1
    ? `${(cardsInLane.findIndex((c) => c.id === card.id) ?? 0) + 1} / ${cardsInLane.length}`
    : null;

  return (
    <Dialog open={open} onOpenChange={handleDialogOpenChange}>
      <DialogContent data-mobile-fullscreen className="flex flex-col p-0 md:max-h-[85vh] md:!w-[80vw] md:!max-w-[80vw]" style={{ overflow: 'visible' }}>
        {/* Floating nav buttons — desktop only, positioned outside the dialog */}
        {prevCard && (
          <Button
            variant="outline"
            size="icon"
            onClick={() => handleNavigate('prev')}
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
            className="absolute top-1/2 -right-14 z-50 hidden -translate-y-1/2 rounded-full shadow-lg md:flex"
            aria-label="Next card"
          >
            <ChevronRight className="h-5 w-5" />
          </Button>
        )}
        <div className="flex min-h-0 flex-1 flex-col overflow-hidden">
          <CardDetailForm
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
  );
}
