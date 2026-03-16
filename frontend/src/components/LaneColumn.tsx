import { useDroppable } from '@dnd-kit/core';
import { SortableContext, verticalListSortingStrategy } from '@dnd-kit/sortable';
import { useMemo } from 'react';
import { SortableCard } from '@/components/SortableCard';
import { cn } from '@/lib/utils';
import type { CardItem, Lane } from '@/types';

type LaneColumnProps = {
  lane: Lane;
  cards: CardItem[];
  onCardClick: (card: CardItem) => void;
  onAddCard: () => void;
  activeCardId: string | null;
  sizeMap: Map<string, string>;
};

export function LaneColumn({
  lane,
  cards,
  onCardClick,
  onAddCard,
  activeCardId,
  sizeMap,
}: LaneColumnProps) {
  const { setNodeRef, isOver } = useDroppable({ id: lane.id });
  const cardIds = useMemo(() => cards.map((c) => c.id), [cards]);

  return (
    <article
      ref={setNodeRef}
      className={cn(
        'flex flex-col rounded-lg border border-lane-border bg-lane-bg border-t-2 border-t-primary md:overflow-hidden',
        isOver && 'ring-2 ring-primary/40',
      )}
    >
      <div className="flex shrink-0 items-center justify-between px-4 py-3">
        <div className="flex items-center gap-2">
          <span className="inline-block h-2 w-2 rounded-full bg-primary" />
          <h2 className="text-sm font-semibold uppercase tracking-wide" style={{ fontFamily: "'Space Grotesk', sans-serif" }}>{lane.name}</h2>
          <span className="text-xs text-muted-foreground">{cards.length}</span>
        </div>
        <button
          type="button"
          onClick={onAddCard}
          className="flex h-6 w-6 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-secondary hover:text-foreground"
        >
          <svg width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
            <line x1="8" y1="3" x2="8" y2="13" />
            <line x1="3" y1="8" x2="13" y2="8" />
          </svg>
        </button>
      </div>
      <SortableContext items={cardIds} strategy={verticalListSortingStrategy}>
        <div className="space-y-2 px-3 pb-3 md:flex-1 md:overflow-y-auto">
          {cards.map((card) => (
            <SortableCard key={card.id} card={card} onCardClick={onCardClick} isDragging={card.id === activeCardId} sizeMap={sizeMap} />
          ))}
        </div>
      </SortableContext>
    </article>
  );
}
