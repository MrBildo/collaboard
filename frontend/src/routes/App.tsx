import { DndContext, DragEndEvent, DragOverlay, DragStartEvent, useDraggable, useDroppable } from '@dnd-kit/core';
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
  const boardQuery = useQuery({ queryKey: ['board'], queryFn: fetchBoard, retry: 2, staleTime: 30_000 });
  const labelsQuery = useQuery({ queryKey: ['labels'], queryFn: fetchLabels });

  const [activeCardId, setActiveCardId] = useState<string | null>(null);
  const [selectedCard, setSelectedCard] = useState<CardItem | null>(null);
  const [detailOpen, setDetailOpen] = useState(false);
  const [createOpen, setCreateOpen] = useState(false);
  const [createLaneId, setCreateLaneId] = useState<string | undefined>(undefined);
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

  const onDragStart = (event: DragStartEvent) => {
    setActiveCardId(String(event.active.id));
  };

  const onDragEnd = (event: DragEndEvent) => {
    const cardId = String(event.active.id);
    const overLaneId = event.over?.id ? String(event.over.id) : null;

    if (!overLaneId || !cards.find((c) => c.id === cardId && c.laneId !== overLaneId)) {
      setActiveCardId(null);
      return;
    }

    // Optimistic update BEFORE clearing active — both in same sync batch
    queryClient.setQueryData<{ lanes: Lane[]; cards: CardItem[] }>(['board'], (old) => {
      if (!old) return old;
      return {
        ...old,
        cards: old.cards.map((c) => (c.id === cardId ? { ...c, laneId: overLaneId } : c)),
      };
    });
    setActiveCardId(null);

    // Fire-and-forget patch, then sync
    api.patch(`/cards/${cardId}`, { laneId: overLaneId }).then(() => {
      queryClient.invalidateQueries({ queryKey: ['board'] });
    });
  };

  const handleCardClick = (card: CardItem) => {
    setSelectedCard(card);
    setDetailOpen(true);
  };

  return (
    <main className="min-h-screen bg-background p-4 text-foreground">
      <header className="mb-6 flex items-center justify-between">
        <img
            src="/collaboard-logo.png"
            alt="Collaboard"
            className="h-28 w-auto"
            style={{
              maskImage: 'radial-gradient(ellipse 90% 80% at center, black 40%, transparent 100%)',
              WebkitMaskImage: 'radial-gradient(ellipse 90% 80% at center, black 40%, transparent 100%)',
            }}
          />
        <div className="flex items-center gap-2">
          <Button onClick={() => { setCreateLaneId(undefined); setCreateOpen(true); }}>New Card</Button>
          <Button variant="outline" onClick={() => setAdminOpen(true)}>
            Admin
          </Button>
        </div>
      </header>
      {boardQuery.isError && (
        <div className="rounded-lg border border-destructive/30 bg-destructive/5 p-4 text-center text-sm text-destructive">
          Failed to load board. Check your auth key in <code>.env</code> and clear localStorage.
        </div>
      )}
      {boardQuery.isLoading && (
        <p className="py-8 text-center text-muted-foreground">Loading board...</p>
      )}
      <DndContext onDragStart={onDragStart} onDragEnd={onDragEnd}>
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
              onAddCard={() => { setCreateLaneId(lane.id); setCreateOpen(true); }}
              activeCardId={activeCardId}
            />
          ))}
        </section>
        <DragOverlay>
          {activeCardId ? (
            <CardOverlay card={cards.find((c) => c.id === activeCardId)!} labels={allLabels} />
          ) : null}
        </DragOverlay>
      </DndContext>

      <CardDetailSheet card={selectedCard} open={detailOpen} onOpenChange={setDetailOpen} />

      <CreateCardDialog lanes={lanes} open={createOpen} onOpenChange={setCreateOpen} defaultLaneId={createLaneId} />

      <AdminPanel open={adminOpen} onOpenChange={setAdminOpen} />
    </main>
  );
}

function LaneColumn({
  lane,
  cards,
  labels,
  onCardClick,
  onAddCard,
  activeCardId,
}: {
  lane: Lane;
  cards: CardItem[];
  labels: Label[];
  onCardClick: (card: CardItem) => void;
  onAddCard: () => void;
  activeCardId: string | null;
}) {
  const { setNodeRef, isOver } = useDroppable({ id: lane.id });

  return (
    <article
      ref={setNodeRef}
      className={cn(
        'rounded-lg border border-border/60 bg-card p-4 shadow-md border-t-2 border-t-primary/50',
        isOver && 'ring-2 ring-primary/40',
      )}
    >
      <div className="mb-3 flex items-center justify-between">
        <h2 className="font-semibold">{lane.name}</h2>
        <button
          type="button"
          onClick={onAddCard}
          className="flex h-9 w-9 items-center justify-center rounded-md border text-2xl font-semibold text-muted-foreground transition-colors hover:bg-secondary hover:text-foreground"
        >
          +
        </button>
      </div>
      <div className="space-y-3">
        {cards.map((card) => (
          <DraggableCard key={card.id} card={card} labels={labels} onCardClick={onCardClick} isDragging={card.id === activeCardId} />
        ))}
      </div>
    </article>
  );
}

function DraggableCard({
  card,
  labels,
  onCardClick,
  isDragging,
}: {
  card: CardItem;
  labels: Label[];
  onCardClick: (card: CardItem) => void;
  isDragging: boolean;
}) {
  const { attributes, listeners, setNodeRef } = useDraggable({ id: card.id });

  const isBlocked = card.blocked != null && card.blocked.trim() !== '';

  // Truncate description for preview
  const descriptionPreview =
    card.descriptionMarkdown && card.descriptionMarkdown.length > 80
      ? card.descriptionMarkdown.slice(0, 80) + '...'
      : (card.descriptionMarkdown ?? '');

  return (
    <div
      ref={setNodeRef}
      {...listeners}
      {...attributes}
      onClick={() => onCardClick(card)}
      className={cn(
        'cursor-grab rounded-md border bg-card p-2 hover:shadow-md',
        isBlocked ? 'border-destructive ring-1 ring-destructive/30' : 'border-border',
        isDragging && 'opacity-0',
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

function CardOverlay({ card, labels }: { card: CardItem; labels: Label[] }) {
  const isBlocked = card.blocked != null && card.blocked.trim() !== '';

  return (
    <div
      className={cn(
        'w-64 rounded-md border bg-card p-2 shadow-xl',
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
    </div>
  );
}
