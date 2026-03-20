import { useDroppable } from '@dnd-kit/core';
import { SortableContext, verticalListSortingStrategy } from '@dnd-kit/sortable';
import { ChevronDown, Plus } from 'lucide-react';
import { useMemo } from 'react';
import { Button } from '@/components/ui/button';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip';
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
      data-lane=""
      className={cn(
        'flex min-w-0 flex-col rounded-lg max-md:border-0 md:border md:border-lane-border bg-lane-bg md:border-t-2 md:border-t-primary md:overflow-hidden transition-[box-shadow,background-color] duration-150',
        isOver && 'ring-2 ring-primary/60 bg-primary/5',
      )}
    >
      <div
        className="flex min-w-0 shrink-0 items-center justify-between overflow-hidden px-4 py-3 max-md:sticky max-md:top-0 max-md:z-10 max-md:bg-lane-bg max-md:border-t-2 max-md:border-t-primary max-md:rounded-t-lg max-md:cursor-pointer max-md:select-none"
        onClick={(e) => {
          if (window.innerWidth < 768) {
            e.preventDefault();
            onToggleCollapse();
          }
        }}
      >
        <div className="flex min-w-0 items-center gap-2">
          <ChevronDown
            className={cn(
              'h-4 w-4 shrink-0 text-muted-foreground transition-transform duration-200 md:hidden',
              isCollapsed && '-rotate-90',
            )}
          />
          <span className="inline-block h-2 w-2 shrink-0 rounded-full bg-primary" />
          <Tooltip>
            <TooltipTrigger render={<h2 className="truncate text-sm font-semibold uppercase tracking-wide" />}>
              {lane.name}
            </TooltipTrigger>
            <TooltipContent>{lane.name}</TooltipContent>
          </Tooltip>
          <span className="shrink-0 text-xs text-muted-foreground">{cards.length}</span>
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
