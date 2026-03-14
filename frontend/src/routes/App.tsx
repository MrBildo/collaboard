import { DndContext, DragEndEvent, DragOverlay, DragStartEvent, MouseSensor, TouchSensor, useDraggable, useDroppable, useSensor, useSensors } from '@dnd-kit/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback, useMemo, useState } from 'react';
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

  const lanes = useMemo(() => boardQuery.data?.lanes ?? [], [boardQuery.data]);
  const cards = useMemo(() => boardQuery.data?.cards ?? [], [boardQuery.data]);

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
      <DndContext sensors={sensors} onDragStart={onDragStart} onDragEnd={onDragEnd}>
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
          <DraggableCard key={card.id} card={card} onCardClick={onCardClick} isDragging={card.id === activeCardId} />
        ))}
      </div>
    </article>
  );
}

function DraggableCard({
  card,
  onCardClick,
  isDragging,
}: {
  card: CardItem;
  onCardClick: (card: CardItem) => void;
  isDragging: boolean;
}) {
  const { attributes, listeners, setNodeRef } = useDraggable({ id: card.id });
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
