import { DndContext, DragEndEvent, DragOverEvent, DragOverlay, DragStartEvent, MouseSensor, TouchSensor, closestCorners, useDroppable, useSensor, useSensors } from '@dnd-kit/core';
import { SortableContext, useSortable, verticalListSortingStrategy, arrayMove } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { AdminPanel } from '@/components/AdminPanel';
import { CardDetailSheet } from '@/components/CardDetailSheet';
import { CreateCardDialog } from '@/components/CreateCardDialog';
import { LoginScreen } from '@/components/LoginScreen';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { fetchBoard, fetchCardAttachments, fetchCardLabels, fetchComments, fetchUsers, reorderCard } from '@/lib/api';
import { isLoggedIn, setUserKey, clearUserKey } from '@/lib/auth';
import { cn } from '@/lib/utils';
import type { CardItem, Lane } from '@/types';

type BoardData = { lanes: Lane[]; cards: CardItem[] };

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

  const boardQuery = useQuery({ queryKey: ['board'], queryFn: fetchBoard, retry: 2, staleTime: 30_000, enabled: loggedIn, refetchOnWindowFocus: false });
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

  const dragPhaseRef = useRef<'idle' | 'dragging' | 'settling'>('idle');

  const lanes = useMemo(() => boardQuery.data?.lanes ?? [], [boardQuery.data]);
  const serverCards = useMemo(() => boardQuery.data?.cards ?? [], [boardQuery.data]);

  const [localCards, setLocalCards] = useState<CardItem[]>([]);

  useEffect(() => {
    if (dragPhaseRef.current === 'idle' && serverCards.length > 0) {
      setLocalCards([...serverCards].sort((a, b) => a.position - b.position));
    }
  }, [serverCards]);

  const laneIds = useMemo(() => new Set(lanes.map((l) => l.id)), [lanes]);

  const byLane = useMemo(() => {
    const map = new Map<string, CardItem[]>();
    lanes.forEach((lane) => map.set(lane.id, []));
    localCards.forEach((card) => map.get(card.laneId)?.push(card));
    return map;
  }, [lanes, localCards]);

  const reorderMutation = useMutation({
    mutationFn: (vars: { cardId: string; laneId: string; index: number }) =>
      reorderCard(vars.cardId, vars.laneId, vars.index),
    onMutate: async () => {
      await queryClient.cancelQueries({ queryKey: ['board'] });
    },
    onSuccess: (data) => {
      queryClient.setQueryData(['board'], data);
    },
    onError: () => {
      queryClient.invalidateQueries({ queryKey: ['board'] });
    },
    onSettled: () => {
      dragPhaseRef.current = 'idle';
    },
  });

  const onDragStart = (event: DragStartEvent) => {
    dragPhaseRef.current = 'dragging';
    setActiveCardId(String(event.active.id));
  };

  const onDragOver = (event: DragOverEvent) => {
    const { active, over } = event;
    if (!over) return;

    const activeId = String(active.id);
    const overId = String(over.id);

    setLocalCards((prev) => {
      const activeIdx = prev.findIndex((c) => c.id === activeId);
      if (activeIdx === -1) return prev;

      const activeCard = prev[activeIdx];
      const activeLaneId = activeCard.laneId;
      const overLaneId = laneIds.has(overId)
        ? overId
        : prev.find((c) => c.id === overId)?.laneId ?? null;

      if (!overLaneId) return prev;

      // Same-lane reorder
      if (activeLaneId === overLaneId) {
        if (laneIds.has(overId)) return prev; // hovering over own lane container — no-op
        const laneCards = prev.filter((c) => c.laneId === activeLaneId);
        const oldIdx = laneCards.findIndex((c) => c.id === activeId);
        const newIdx = laneCards.findIndex((c) => c.id === overId);
        if (oldIdx === -1 || newIdx === -1 || oldIdx === newIdx) return prev;

        const reordered = arrayMove(laneCards, oldIdx, newIdx);
        const reorderedIds = new Set(reordered.map((c) => c.id));
        const rest = prev.filter((c) => !reorderedIds.has(c.id));
        return [...rest, ...reordered];
      }

      // Cross-lane move
      const overLaneCards = prev.filter((c) => c.laneId === overLaneId && c.id !== activeId);

      let newIndex = overLaneCards.length; // default: append
      if (!laneIds.has(overId)) {
        const overIndex = overLaneCards.findIndex((c) => c.id === overId);
        if (overIndex >= 0) {
          const isBelowOver =
            active.rect.current.translated &&
            active.rect.current.translated.top > over.rect.top + over.rect.height;
          newIndex = isBelowOver ? overIndex + 1 : overIndex;
        }
      }

      const movedCard = { ...activeCard, laneId: overLaneId };
      const withoutActive = prev.filter((c) => c.id !== activeId);
      const targetLaneCards = withoutActive.filter((c) => c.laneId === overLaneId);
      const otherCards = withoutActive.filter((c) => c.laneId !== overLaneId);

      targetLaneCards.splice(newIndex, 0, movedCard);
      return [...otherCards, ...targetLaneCards];
    });
  };

  const onDragEnd = (event: DragEndEvent) => {
    const { active, over } = event;
    const cardId = String(active.id);

    if (!over) {
      // Cancel — reset local cards from server
      dragPhaseRef.current = 'idle';
      setLocalCards([...serverCards].sort((a, b) => a.position - b.position));
      setActiveCardId(null);
      return;
    }

    // Compute target lane and index from localCards (onDragOver already positioned it)
    const card = localCards.find((c) => c.id === cardId);
    if (!card) {
      dragPhaseRef.current = 'idle';
      setActiveCardId(null);
      return;
    }

    const targetLaneId = card.laneId;
    const laneCards = localCards.filter((c) => c.laneId === targetLaneId);
    const index = laneCards.findIndex((c) => c.id === cardId);

    dragPhaseRef.current = 'settling';
    reorderMutation.mutate({ cardId, laneId: targetLaneId, index: index === -1 ? 0 : index });
    setActiveCardId(null);
  };

  const handleCardClick = (card: CardItem) => {
    setSelectedCard(card);
    setDetailOpen(true);
  };

  if (!loggedIn) {
    return <LoginScreen onLogin={handleLogin} />;
  }

  return (
    <main className="flex h-screen flex-col bg-background p-4 text-foreground">
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
          className="grid min-h-0 flex-1 gap-4 overflow-x-auto pb-2"
          style={{
            gridTemplateColumns: lanes.length > 0
              ? `repeat(${lanes.length}, minmax(320px, 1fr))`
              : undefined,
          }}
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
            <CardOverlay card={localCards.find((c) => c.id === activeCardId)!} />
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
        'flex flex-col overflow-hidden rounded-lg border border-border/60 bg-card p-4 shadow-md border-t-2 border-t-primary/50',
        isOver && 'ring-2 ring-primary/40',
      )}
    >
      <div className="mb-3 flex shrink-0 items-center justify-between">
        <h2 className="font-semibold">{lane.name}</h2>
        <button
          type="button"
          onClick={onAddCard}
          className="flex h-7 w-7 items-center justify-center rounded-md border text-muted-foreground transition-colors hover:bg-secondary hover:text-foreground"
        >
          <svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
            <line x1="8" y1="3" x2="8" y2="13" />
            <line x1="3" y1="8" x2="13" y2="8" />
          </svg>
        </button>
      </div>
      <SortableContext items={cardIds} strategy={verticalListSortingStrategy}>
        <div className="flex-1 space-y-3 overflow-y-auto pr-3">
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
