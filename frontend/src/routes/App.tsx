import { DndContext, DragEndEvent, useDraggable, useDroppable } from '@dnd-kit/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { AdminPanel } from '@/components/AdminPanel';
import { CardDetailSheet } from '@/components/CardDetailSheet';
import { CreateCardDialog } from '@/components/CreateCardDialog';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { api, fetchBoard, fetchLabels } from '@/lib/api';
import { cn } from '@/lib/utils';
import type { CardItem, Label, Lane } from '@/types';

export function App() {
  const queryClient = useQueryClient();
  const boardQuery = useQuery({ queryKey: ['board'], queryFn: fetchBoard });
  const labelsQuery = useQuery({ queryKey: ['labels'], queryFn: fetchLabels });

  const [selectedCard, setSelectedCard] = useState<CardItem | null>(null);
  const [detailOpen, setDetailOpen] = useState(false);
  const [createOpen, setCreateOpen] = useState(false);
  const [adminOpen, setAdminOpen] = useState(false);

  const lanes = useMemo(() => boardQuery.data?.lanes ?? [], [boardQuery.data]);
  const cards = useMemo(() => boardQuery.data?.cards ?? [], [boardQuery.data]);
  const allLabels = useMemo(() => labelsQuery.data ?? [], [labelsQuery.data]);

  const byLane = useMemo(() => {
    const map = new Map<string, CardItem[]>();
    lanes.forEach((lane) => map.set(lane.id, []));
    cards.forEach((card) => map.get(card.laneId)?.push(card));
    map.forEach((list) => list.sort((a, b) => a.position - b.position));
    return map;
  }, [lanes, cards]);

  const onDragEnd = (event: DragEndEvent) => {
    const cardId = String(event.active.id);
    const overLaneId = event.over?.id ? String(event.over.id) : null;
    if (!overLaneId) return;

    const card = cards.find((c) => c.id === cardId);
    if (!card || card.laneId === overLaneId) return;

    queryClient.setQueryData<{ lanes: Lane[]; cards: CardItem[] }>(['board'], (old) => {
      if (!old) return old;
      return {
        ...old,
        cards: old.cards.map((c) => (c.id === cardId ? { ...c, laneId: overLaneId } : c)),
      };
    });

    api.patch(`/cards/${cardId}`, { laneId: overLaneId });
  };

  const handleCardClick = (card: CardItem) => {
    setSelectedCard(card);
    setDetailOpen(true);
  };

  return (
    <main className="min-h-screen bg-background p-4 text-foreground">
      <header className="mb-6 flex items-center justify-between">
        <img src="/collaboard-logo.png" alt="Collaboard" className="h-10" />
        <div className="flex items-center gap-2">
          <Button onClick={() => setCreateOpen(true)}>New Card</Button>
          <Button variant="outline" onClick={() => setAdminOpen(true)}>
            Admin
          </Button>
        </div>
      </header>
      <DndContext onDragEnd={onDragEnd}>
        <section
          className="grid grid-cols-1 gap-4 md:grid-cols-3"
          style={
            lanes.length > 0
              ? { gridTemplateColumns: `repeat(${lanes.length}, minmax(0, 1fr))` }
              : undefined
          }
          aria-label="Kanban board"
        >
          {lanes.map((lane) => (
            <LaneColumn
              key={lane.id}
              lane={lane}
              cards={byLane.get(lane.id) ?? []}
              labels={allLabels}
              onCardClick={handleCardClick}
            />
          ))}
        </section>
      </DndContext>

      <CardDetailSheet card={selectedCard} open={detailOpen} onOpenChange={setDetailOpen} />

      <CreateCardDialog lanes={lanes} open={createOpen} onOpenChange={setCreateOpen} />

      <AdminPanel open={adminOpen} onOpenChange={setAdminOpen} />
    </main>
  );
}

function LaneColumn({
  lane,
  cards,
  labels,
  onCardClick,
}: {
  lane: Lane;
  cards: CardItem[];
  labels: Label[];
  onCardClick: (card: CardItem) => void;
}) {
  const { setNodeRef, isOver } = useDroppable({ id: lane.id });

  return (
    <article
      ref={setNodeRef}
      className={cn(
        'rounded-lg border bg-card p-3 shadow',
        isOver && 'ring-2 ring-primary/40',
      )}
    >
      <h2 className="mb-3 font-semibold">{lane.name}</h2>
      <div className="space-y-3">
        {cards.map((card) => (
          <DraggableCard key={card.id} card={card} labels={labels} onCardClick={onCardClick} />
        ))}
      </div>
    </article>
  );
}

function DraggableCard({
  card,
  labels,
  onCardClick,
}: {
  card: CardItem;
  labels: Label[];
  onCardClick: (card: CardItem) => void;
}) {
  const { attributes, listeners, setNodeRef, transform } = useDraggable({ id: card.id });
  const style = transform
    ? { transform: `translate(${transform.x}px, ${transform.y}px)` }
    : undefined;

  const isBlocked = card.blocked != null && card.blocked.trim() !== '';

  // Truncate description for preview
  const descriptionPreview =
    card.descriptionMarkdown && card.descriptionMarkdown.length > 80
      ? card.descriptionMarkdown.slice(0, 80) + '...'
      : (card.descriptionMarkdown ?? '');

  return (
    <div
      ref={setNodeRef}
      style={style}
      {...listeners}
      {...attributes}
      onClick={() => onCardClick(card)}
      className={cn(
        'cursor-grab rounded-md border bg-card p-2 hover:shadow-md',
        isBlocked ? 'border-destructive ring-1 ring-destructive/30' : 'border-border',
      )}
    >
      <div className="flex items-start justify-between gap-1">
        <p className="text-xs text-muted-foreground">#{card.number}</p>
        <Badge variant="outline" className="text-[10px]">
          {card.size}
        </Badge>
      </div>
      <h3 className="mt-0.5 font-medium">{card.name}</h3>

      {descriptionPreview && (
        <p className="mt-1 text-xs text-muted-foreground">{descriptionPreview}</p>
      )}

      {isBlocked && (
        <Badge variant="destructive" className="mt-1.5">
          Blocked: {card.blocked}
        </Badge>
      )}

      {labels.length > 0 && (
        <div className="mt-1.5 flex flex-wrap gap-1">
          {labels.map((label) => (
            <Badge
              key={label.id}
              variant="secondary"
              className="text-[10px]"
              style={label.color ? { backgroundColor: label.color, color: '#fff' } : undefined}
            >
              {label.name}
            </Badge>
          ))}
        </div>
      )}
    </div>
  );
}
