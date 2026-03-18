import { useDroppable } from '@dnd-kit/core';
import { SortableContext, verticalListSortingStrategy } from '@dnd-kit/sortable';
import { ChevronDown, Plus } from 'lucide-react';
import { useMemo } from 'react';
import { Button } from '@/components/ui/button';
import { SortableCard } from '@/components/SortableCard';
import { cn } from '@/lib/utils';
import type { CardItem, CardSummary, Lane } from '@/types';

type LaneColumnProps = {
  lane: Lane;
  cards: CardItem[];
  onCardClick: (card: CardItem) => void;
  onAddCard: () => void;
  activeCardId: string | null;
  sizeMap: Map<string, string>;
  enrichedCardMap: Map<string, CardSummary>;
  isCollapsed: boolean;
  onToggleCollapse: () => void;
};

export function LaneColumn({
  lane,
  cards,
  onCardClick,
  onAddCard,
  activeCardId,
  sizeMap,
  enrichedCardMap,
  isCollapsed,
  onToggleCollapse,
}: LaneColumnProps) {
  const { setNodeRef, isOver } = useDroppable({ id: lane.id });
  const cardIds = useMemo(() => cards.map((c) => c.id), [cards]);

  return (
    <article
      ref={setNodeRef}
      className={cn(
        'flex flex-col rounded-lg border border-lane-border bg-lane-bg border-t-2 border-t-primary md:overflow-hidden transition-all duration-150',
        isOver && 'ring-2 ring-primary/60 bg-primary/5',
      )}
    >
      <div
        className="flex shrink-0 items-center justify-between px-4 py-3 max-md:cursor-pointer max-md:select-none"
        onClick={(e) => {
          if (window.innerWidth < 768) {
            e.preventDefault();
            onToggleCollapse();
          }
        }}
      >
        <div className="flex items-center gap-2">
          <ChevronDown
            className={cn(
              'h-4 w-4 text-muted-foreground transition-transform duration-200 md:hidden',
              isCollapsed && '-rotate-90',
            )}
          />
          <span className="inline-block h-2 w-2 rounded-full bg-primary" />
          <h2 className="text-sm font-semibold uppercase tracking-wide">{lane.name}</h2>
          <span className="text-xs text-muted-foreground">{cards.length}</span>
        </div>
        <Button
          variant="ghost"
          size="icon-xs"
          onClick={(e) => {
            e.stopPropagation();
            onAddCard();
          }}
          className="text-muted-foreground"
        >
          <Plus className="h-3.5 w-3.5" />
        </Button>
      </div>
      <SortableContext items={cardIds} strategy={verticalListSortingStrategy}>
        <div
          className={cn(
            'space-y-2 px-3 pb-3 md:flex-1 md:overflow-y-auto',
            'max-md:grid max-md:transition-[grid-template-rows] max-md:duration-200 max-md:ease-in-out',
            isCollapsed ? 'max-md:grid-rows-[0fr]' : 'max-md:grid-rows-[1fr]',
          )}
        >
          <div className="max-md:overflow-hidden">
            {cards.map((card) => (
              <div key={card.id} className="mb-2 last:mb-0">
                <SortableCard card={card} onCardClick={onCardClick} isDragging={card.id === activeCardId} sizeMap={sizeMap} enrichedData={enrichedCardMap.get(card.id)} />
              </div>
            ))}
          </div>
        </div>
      </SortableContext>
    </article>
  );
}
