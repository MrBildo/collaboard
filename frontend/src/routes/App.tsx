import { DndContext, DragEndEvent, DragOverEvent, DragOverlay, DragStartEvent, MouseSensor, TouchSensor, closestCorners, useDroppable, useSensor, useSensors } from '@dnd-kit/core';
import { SortableContext, useSortable, verticalListSortingStrategy } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback, useMemo, useRef, useState } from 'react';
import { AdminPanel } from '@/components/AdminPanel';
import { CardDetailSheet } from '@/components/CardDetailSheet';
import { CreateCardDialog } from '@/components/CreateCardDialog';
import { LoginScreen } from '@/components/LoginScreen';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { api, fetchBoard, fetchCardAttachments, fetchCardLabels, fetchComments, fetchUsers } from '@/lib/api';
import { isLoggedIn, setUserKey, clearUserKey } from '@/lib/auth';
import { cn } from '@/lib/utils';
import type { CardItem, Lane } from '@/types';

type BoardData = { lanes: Lane[]; cards: CardItem[] };

function calculatePosition(laneCards: CardItem[], dropIndex: number): number {
  if (laneCards.length === 0) return 0;
  if (dropIndex <= 0) return laneCards[0].position - 10;
  if (dropIndex >= laneCards.length) return laneCards[laneCards.length - 1].position + 10;
  return (laneCards[dropIndex - 1].position + laneCards[dropIndex].position) / 2;
}

export function App() {
  const queryClient = useQueryClient();
  const [loggedIn, setLoggedIn] = useState(isLoggedIn());

  const handleLogin = useCallback((key: string) => {
    setUserKey(key);
    setLoggedIn(true);
  }, []);

  const handleLogout = useCallback(() => {
    clearUserKey();
    queryClient.clear();
    setLoggedIn(false);
  }, [queryClient]);

  const boardQuery = useQuery({ queryKey: ['board'], queryFn: fetchBoard, retry: 2, staleTime: 30_000, enabled: loggedIn });
  const adminCheck = useQuery({
    queryKey: ['adminCheck'],
    queryFn: () => fetchUsers().then(() => true),
    retry: false,
    enabled: loggedIn,
  });
  const isAdmin = adminCheck.data === true;

  const mouseSensor = useSensor(MouseSensor, { activationConstraint: { distance: 8 } });
  const touchSensor = useSensor(TouchSensor, { activationConstraint: { delay: 200, tolerance: 5 } });
  const sensors = useSensors(mouseSensor, touchSensor);

  const [activeCardId, setActiveCardId] = useState<string | null>(null);
  const [selectedCard, setSelectedCard] = useState<CardItem | null>(null);
  const [detailOpen, setDetailOpen] = useState(false);
  const [createOpen, setCreateOpen] = useState(false);
  const [createLaneId, setCreateLaneId] = useState<string | undefined>(undefined);
  const [adminOpen, setAdminOpen] = useState(false);

  // Track the lane the dragged card came from so we can restore on cancel
  const dragSourceLaneRef = useRef<string | null>(null);

  const lanes = useMemo(() => boardQuery.data?.lanes ?? [], [boardQuery.data]);
  const cards = useMemo(() => boardQuery.data?.cards ?? [], [boardQuery.data]);

  const laneIds = useMemo(() => new Set(lanes.map((l) => l.id)), [lanes]);

  const byLane = useMemo(() => {
    const map = new Map<string, CardItem[]>();
    lanes.forEach((lane) => map.set(lane.id, []));
    cards.forEach((card) => map.get(card.laneId)?.push(card));
    map.forEach((list) => list.sort((a, b) => a.position - b.position));
    return map;
  }, [lanes, cards]);

  const onDragStart = (event: DragStartEvent) => {
    const cardId = String(event.active.id);
    setActiveCardId(cardId);
    const card = cards.find((c) => c.id === cardId);
    dragSourceLaneRef.current = card?.laneId ?? null;
  };

  const onDragOver = (event: DragOverEvent) => {
    const { active, over } = event;
    if (!over) return;

    const activeId = String(active.id);
    const overId = String(over.id);

    // Find which lane the active card is currently in (from query cache)
    const currentData = queryClient.getQueryData<BoardData>(['board']);
    if (!currentData) return;

    const activeCard = currentData.cards.find((c) => c.id === activeId);
    if (!activeCard) return;

    // Determine the target lane
    let targetLaneId: string;
    if (laneIds.has(overId)) {
      targetLaneId = overId;
    } else {
      const overCard = currentData.cards.find((c) => c.id === overId);
      if (!overCard) return;
      targetLaneId = overCard.laneId;
    }

    // Only update if moving to a different lane
    if (activeCard.laneId === targetLaneId) return;

    queryClient.setQueryData<BoardData>(['board'], (old) => {
      if (!old) return old;

      // Remove card from old lane, add to new lane
      const updatedCards = old.cards.map((c) =>
        c.id === activeId ? { ...c, laneId: targetLaneId } : c,
      );

      return { ...old, cards: updatedCards };
    });
  };

  const onDragEnd = (event: DragEndEvent) => {
    const { active, over } = event;
    const cardId = String(active.id);

    if (!over) {
      // Cancelled — restore original lane
      if (dragSourceLaneRef.current) {
        queryClient.setQueryData<BoardData>(['board'], (old) => {
          if (!old) return old;
          return {
            ...old,
            cards: old.cards.map((c) =>
              c.id === cardId ? { ...c, laneId: dragSourceLaneRef.current! } : c,
            ),
          };
        });
      }
      setActiveCardId(null);
      dragSourceLaneRef.current = null;
      return;
    }

    const overId = String(over.id);
    const currentData = queryClient.getQueryData<BoardData>(['board']);
    if (!currentData) {
      setActiveCardId(null);
      dragSourceLaneRef.current = null;
      return;
    }

    const activeCard = currentData.cards.find((c) => c.id === cardId);
    if (!activeCard) {
      setActiveCardId(null);
      dragSourceLaneRef.current = null;
      return;
    }

    // Determine target lane
    let targetLaneId: string;
    if (laneIds.has(overId)) {
      targetLaneId = overId;
    } else {
      const overCard = currentData.cards.find((c) => c.id === overId);
      targetLaneId = overCard?.laneId ?? activeCard.laneId;
    }

    // Get sorted cards in the target lane (excluding the active card)
    const targetLaneCards = currentData.cards
      .filter((c) => c.laneId === targetLaneId && c.id !== cardId)
      .sort((a, b) => a.position - b.position);

    // Calculate drop index
    let dropIndex: number;
    if (laneIds.has(overId)) {
      // Dropped on the lane itself — append at end
      dropIndex = targetLaneCards.length;
    } else {
      // Dropped on a card — find its index in the filtered list
      const overIndex = targetLaneCards.findIndex((c) => c.id === overId);
      dropIndex = overIndex === -1 ? targetLaneCards.length : overIndex;
    }

    const newPosition = calculatePosition(targetLaneCards, dropIndex);

    const sourceLaneId = dragSourceLaneRef.current;
    const laneChanged = targetLaneId !== sourceLaneId;
    const positionChanged = activeCard.position !== newPosition;

    if (!laneChanged && !positionChanged) {
      setActiveCardId(null);
      dragSourceLaneRef.current = null;
      return;
    }

    // Optimistic update
    queryClient.setQueryData<BoardData>(['board'], (old) => {
      if (!old) return old;
      return {
        ...old,
        cards: old.cards.map((c) =>
          c.id === cardId ? { ...c, laneId: targetLaneId, position: newPosition } : c,
        ),
      };
    });
    setActiveCardId(null);
    dragSourceLaneRef.current = null;

    // Build patch payload — only send changed fields
    const patch: Record<string, unknown> = {};
    if (laneChanged) patch.laneId = targetLaneId;
    patch.position = newPosition;

    api.patch(`/cards/${cardId}`, patch).then(() => {
      queryClient.invalidateQueries({ queryKey: ['board'] });
    });
  };

  const handleCardClick = (card: CardItem) => {
    setSelectedCard(card);
    setDetailOpen(true);
  };

  if (!loggedIn) {
    return <LoginScreen onLogin={handleLogin} />;
  }

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
          {isAdmin && (
            <Button variant="outline" onClick={() => setAdminOpen(true)}>
              Admin
            </Button>
          )}
          <Button variant="ghost" onClick={handleLogout} className="text-muted-foreground">
            Logout
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
      <DndContext
        sensors={sensors}
        collisionDetection={closestCorners}
        onDragStart={onDragStart}
        onDragOver={onDragOver}
        onDragEnd={onDragEnd}
      >
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
              onCardClick={handleCardClick}
              onAddCard={() => { setCreateLaneId(lane.id); setCreateOpen(true); }}
              activeCardId={activeCardId}
            />
          ))}
        </section>
        <DragOverlay>
          {activeCardId ? (
            <CardOverlay card={cards.find((c) => c.id === activeCardId)!} />
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
  onCardClick,
  onAddCard,
  activeCardId,
}: {
  lane: Lane;
  cards: CardItem[];
  onCardClick: (card: CardItem) => void;
  onAddCard: () => void;
  activeCardId: string | null;
}) {
  const { setNodeRef, isOver } = useDroppable({ id: lane.id });
  const cardIds = useMemo(() => cards.map((c) => c.id), [cards]);

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
      <SortableContext items={cardIds} strategy={verticalListSortingStrategy}>
        <div className="space-y-3">
          {cards.map((card) => (
            <SortableCard key={card.id} card={card} onCardClick={onCardClick} isDragging={card.id === activeCardId} />
          ))}
        </div>
      </SortableContext>
    </article>
  );
}

function SortableCard({
  card,
  onCardClick,
  isDragging,
}: {
  card: CardItem;
  onCardClick: (card: CardItem) => void;
  isDragging: boolean;
}) {
  const { attributes, listeners, setNodeRef, transform, transition } = useSortable({ id: card.id });
  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
  };
  const labelsQuery = useQuery({
    queryKey: ['cardLabels', card.id],
    queryFn: () => fetchCardLabels(card.id),
  });
  const commentsQuery = useQuery({
    queryKey: ['comments', card.id],
    queryFn: () => fetchComments(card.id),
    staleTime: 30_000,
  });
  const attachmentsQuery = useQuery({
    queryKey: ['attachments', card.id],
    queryFn: () => fetchCardAttachments(card.id),
    staleTime: 30_000,
  });
  const labels = labelsQuery.data ?? [];
  const commentCount = commentsQuery.data?.length ?? 0;
  const attachmentCount = attachmentsQuery.data?.length ?? 0;

  const isBlocked = card.blocked != null && card.blocked.trim() !== '';

  return (
    <div
      ref={setNodeRef}
      style={style}
      {...listeners}
      {...attributes}
      onClick={() => onCardClick(card)}
      className={cn(
        'cursor-pointer rounded-md border bg-card p-3 hover:shadow-md',
        isBlocked ? 'border-destructive ring-1 ring-destructive/30' : 'border-border',
        isDragging && 'opacity-0',
      )}
    >
      {/* Title + Size */}
      <div className="flex items-start justify-between gap-2">
        <h3 className="text-base font-semibold leading-snug">{card.name}</h3>
        <Badge variant="outline" className="mt-0.5 shrink-0 text-[10px]">{card.size}</Badge>
      </div>

      {isBlocked && (
        <Badge variant="destructive" className="mt-1.5 text-[10px]">
          Blocked
        </Badge>
      )}

      {/* Bottom row — metadata */}
      <div className="mt-2 flex items-center gap-3 text-xs text-muted-foreground">
        <span>#{card.number}</span>

        {commentCount > 0 && (
          <span className="flex items-center gap-0.5" title={`${commentCount} comments`}>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
            </svg>
            {commentCount}
          </span>
        )}

        {attachmentCount > 0 && (
          <span className="flex items-center gap-0.5" title={`${attachmentCount} attachments`}>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <path d="M21.44 11.05l-9.19 9.19a6 6 0 0 1-8.49-8.49l9.19-9.19a4 4 0 0 1 5.66 5.66l-9.2 9.19a2 2 0 0 1-2.83-2.83l8.49-8.48" />
            </svg>
            {attachmentCount}
          </span>
        )}

      </div>

      {/* Labels */}
      {labels.length > 0 && (
        <div className="mt-2 flex flex-wrap gap-1">
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

function CardOverlay({ card }: { card: CardItem }) {
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
