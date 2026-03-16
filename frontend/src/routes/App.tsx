import { DndContext, type DragEndEvent, type DragOverEvent, DragOverlay, type DragStartEvent, MouseSensor, TouchSensor, closestCenter, pointerWithin, useSensor, useSensors, type CollisionDetection } from '@dnd-kit/core';
import { arrayMove } from '@dnd-kit/sortable';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { AdminPanel } from '@/components/AdminPanel';
import { BoardSwitcher } from '@/components/BoardSwitcher';
import { CardDetailSheet } from '@/components/CardDetailSheet';
import { CardOverlay } from '@/components/CardOverlay';
import { CreateCardDialog } from '@/components/CreateCardDialog';
import { GlobalAdminPanel } from '@/components/GlobalAdminPanel';
import { LaneColumn } from '@/components/LaneColumn';
import { LoginScreen } from '@/components/LoginScreen';
import { ThemeToggle } from '@/components/ThemeToggle';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { Menu } from 'lucide-react';
import { fetchBoardBySlug, fetchBoardData, fetchBoards, fetchCards, fetchMe, fetchUsers, fetchVersion, reorderCard } from '@/lib/api';
import { isLoggedIn, setUserKey, clearUserKey, setLastBoardSlug } from '@/lib/auth';
import { QUERY_DEFAULTS } from '@/lib/query-config';
import { queryKeys } from '@/lib/query-keys';
import { useBoardEvents } from '@/lib/use-board-events';
import type { CardItem, CardSummary } from '@/types';

const kanbanCollision: CollisionDetection = (args) => {
  const pointerCollisions = pointerWithin(args);
  if (pointerCollisions.length > 0) return pointerCollisions;
  return closestCenter(args);
};

export function App() {
  const { slug, cardNumber } = useParams<{ slug: string; cardNumber: string }>();
  const navigate = useNavigate();
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

  // Fetch the board metadata by slug
  const boardMetaQuery = useQuery({
    queryKey: queryKeys.boards.detail(slug!),
    queryFn: () => fetchBoardBySlug(slug!),
    enabled: loggedIn && !!slug,
    ...QUERY_DEFAULTS.boards,
  });
  const board = boardMetaQuery.data;
  const boardId = board?.id;

  // Track last visited board
  useEffect(() => {
    if (slug) {
      setLastBoardSlug(slug);
    }
  }, [slug]);

  // SSE for this board
  useBoardEvents(boardId);

  // Fetch board data (lanes + cards)
  const boardDataQuery = useQuery({
    queryKey: queryKeys.boards.data(boardId!),
    queryFn: () => fetchBoardData(boardId!),
    retry: 2,
    staleTime: 30_000,
    enabled: loggedIn && !!boardId,
    refetchOnWindowFocus: false,
  });

  // Enriched cards with labels, commentCount, attachmentCount
  const enrichedCardsQuery = useQuery({
    queryKey: queryKeys.boards.cards(boardId!),
    queryFn: () => fetchCards(boardId!),
    enabled: loggedIn && !!boardId,
    staleTime: 30_000,
  });

  const enrichedCardMap = useMemo(() => {
    const map = new Map<string, CardSummary>();
    for (const card of enrichedCardsQuery.data ?? []) {
      map.set(card.id, card);
    }
    return map;
  }, [enrichedCardsQuery.data]);

  const meQuery = useQuery({
    queryKey: queryKeys.users.me(),
    queryFn: fetchMe,
    enabled: loggedIn,
    staleTime: Infinity,
  });
  const currentUserId = meQuery.data?.id;
  const currentUserRole = meQuery.data?.role;
  const adminCheck = useQuery({
    queryKey: queryKeys.users.adminCheck(),
    queryFn: async () => { await fetchUsers(); return true; },
    retry: false,
    enabled: loggedIn,
  });
  const isAdmin = adminCheck.data === true;

  // Board list for switcher
  const boardsQuery = useQuery({
    queryKey: queryKeys.boards.all(),
    queryFn: fetchBoards,
    enabled: loggedIn,
    staleTime: 60_000,
  });

  const versionQuery = useQuery({
    queryKey: queryKeys.version(),
    queryFn: fetchVersion,
    staleTime: 5 * 60 * 1000,
    refetchOnWindowFocus: true,
  });

  const mouseSensor = useSensor(MouseSensor, { activationConstraint: { distance: 8 } });
  const touchSensor = useSensor(TouchSensor, { activationConstraint: { delay: 200, tolerance: 5 } });
  const sensors = useSensors(mouseSensor, touchSensor);

  const [activeCardId, setActiveCardId] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [createLaneId, setCreateLaneId] = useState<string | undefined>(undefined);
  const [createDialogKey, setCreateDialogKey] = useState(0);
  const [adminOpen, setAdminOpen] = useState(false);
  const [globalAdminOpen, setGlobalAdminOpen] = useState(false);

  const [dragPhase, setDragPhase] = useState<'idle' | 'dragging' | 'settling'>('idle');

  const lanes = useMemo(() => boardDataQuery.data?.lanes ?? [], [boardDataQuery.data]);
  const sizes = useMemo(() => boardDataQuery.data?.sizes ?? [], [boardDataQuery.data]);
  const sizeMap = useMemo(() => new Map(sizes.map((s) => [s.id, s.name])), [sizes]);
  const serverCards = useMemo(() => boardDataQuery.data?.cards ?? [], [boardDataQuery.data]);

  const sortedServerCards = useMemo(() => {
    const seen = new Set<string>();
    const unique: CardItem[] = [];
    for (const card of serverCards) {
      if (!seen.has(card.id)) {
        seen.add(card.id);
        unique.push(card);
      }
    }
    return unique.sort((a, b) => a.position - b.position);
  }, [serverCards]);
  const [dragCards, setDragCards] = useState<CardItem[] | null>(null);
  const localCards = dragPhase === 'idle' ? sortedServerCards : (dragCards ?? sortedServerCards);

  // Derive selected card and detail-open state from URL (cardNumber param is the source of truth)
  const selectedCard = useMemo(() => {
    if (!cardNumber || serverCards.length === 0) return null;
    const num = parseInt(cardNumber, 10);
    return serverCards.find((c) => c.number === num) ?? null;
  }, [cardNumber, serverCards]);
  const isDetailOpen = selectedCard !== null;

  const handleDetailOpenChange = useCallback((open: boolean) => {
    if (!open) {
      navigate(`/boards/${slug}`, { replace: true });
    }
  }, [slug, navigate]);

  const handleNavigateCard = useCallback((cardNumber: number) => {
    navigate(`/boards/${slug}/cards/${cardNumber}`, { replace: true });
  }, [slug, navigate]);

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
      await queryClient.cancelQueries({ queryKey: queryKeys.boards.data(boardId!) });
    },
    onSuccess: (data) => {
      queryClient.setQueryData(queryKeys.boards.data(boardId!), data);
    },
    onError: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.boards.data(boardId!) });
    },
    onSettled: () => {
      setDragPhase('idle');
    },
  });

  const onDragStart = (event: DragStartEvent) => {
    setDragPhase('dragging');
    setActiveCardId(String(event.active.id));
  };

  const onDragOver = (event: DragOverEvent) => {
    const { active, over } = event;
    if (!over) return;

    const activeId = String(active.id);
    const overId = String(over.id);

    setDragCards((prev) => {
      prev = prev ?? sortedServerCards;
      const activeIdx = prev.findIndex((c) => c.id === activeId);
      if (activeIdx === -1) return prev;

      const activeCard = prev[activeIdx];
      const activeLaneId = activeCard.laneId;
      const overLaneId = laneIds.has(overId)
        ? overId
        : prev.find((c) => c.id === overId)?.laneId ?? null;

      if (!overLaneId) return prev;

      if (activeLaneId === overLaneId) {
        if (laneIds.has(overId)) return prev;
        const laneCards = prev.filter((c) => c.laneId === activeLaneId);
        const oldIdx = laneCards.findIndex((c) => c.id === activeId);
        const newIdx = laneCards.findIndex((c) => c.id === overId);
        if (oldIdx === -1 || newIdx === -1 || oldIdx === newIdx) return prev;

        const reordered = arrayMove(laneCards, oldIdx, newIdx);
        const reorderedIds = new Set(reordered.map((c) => c.id));
        const rest = prev.filter((c) => !reorderedIds.has(c.id));
        return [...rest, ...reordered];
      }

      const overLaneCards = prev.filter((c) => c.laneId === overLaneId && c.id !== activeId);

      let newIndex = overLaneCards.length;
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
      setDragPhase('idle');
      setDragCards(null);
      setActiveCardId(null);
      return;
    }

    const card = localCards.find((c) => c.id === cardId);
    if (!card) {
      setDragPhase('idle');
      setActiveCardId(null);
      return;
    }

    const targetLaneId = card.laneId;
    const laneCards = localCards.filter((c) => c.laneId === targetLaneId);
    const index = laneCards.findIndex((c) => c.id === cardId);

    setDragPhase('settling');
    reorderMutation.mutate({ cardId, laneId: targetLaneId, index: index === -1 ? 0 : index });
    setActiveCardId(null);
  };

  const handleCardClick = (card: CardItem) => {
    navigate(`/boards/${slug}/cards/${card.number}`, { replace: true });
  };

  if (!loggedIn) {
    return <LoginScreen onLogin={handleLogin} />;
  }

  const boards = boardsQuery.data ?? [];

  return (
    <main className="flex h-screen flex-col bg-background text-foreground">
      <header className="flex h-14 shrink-0 items-center justify-between border-b border-border px-4">
        <div className="flex items-center gap-3">
          <img
            src="/collaboard-logo.png"
            alt="Collaboard"
            className="w-32 md:w-48"
            style={{ imageRendering: 'pixelated' }}
          />
          {boards.length > 1 && (
            <BoardSwitcher boards={boards} currentSlug={slug} />
          )}
          {boards.length === 1 && board && (
            <span className="hidden text-sm font-medium text-muted-foreground md:inline">{board.name}</span>
          )}
        </div>
        {/* Desktop actions */}
        <div className="hidden items-center gap-2 md:flex">
          <Button onClick={() => { setCreateLaneId(undefined); setCreateDialogKey((k) => k + 1); setCreateOpen(true); }}>+ New Card</Button>
          {isAdmin && (
            <>
              <Button variant="outline" onClick={() => setAdminOpen(true)}>
                Board Settings
              </Button>
              <Button variant="outline" onClick={() => setGlobalAdminOpen(true)}>
                Admin
              </Button>
            </>
          )}
          <ThemeToggle />
          <Button variant="ghost" onClick={handleLogout} className="text-muted-foreground">
            Logout
          </Button>
          {versionQuery.data && (
            <span className="text-xs text-muted-foreground/50">v{versionQuery.data.version}</span>
          )}
        </div>
        {/* Mobile menu */}
        <div className="flex items-center gap-1 md:hidden">
          <ThemeToggle />
          <DropdownMenu>
            <DropdownMenuTrigger className="inline-flex h-9 w-9 items-center justify-center rounded-md text-muted-foreground hover:bg-secondary hover:text-foreground">
              <Menu className="h-5 w-5" />
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="w-48">
              <DropdownMenuItem onClick={() => { setCreateLaneId(undefined); setCreateDialogKey((k) => k + 1); setCreateOpen(true); }}>
                + New Card
              </DropdownMenuItem>
              {isAdmin && (
                <>
                  <DropdownMenuItem onClick={() => setAdminOpen(true)}>
                    Board Settings
                  </DropdownMenuItem>
                  <DropdownMenuItem onClick={() => setGlobalAdminOpen(true)}>
                    Admin
                  </DropdownMenuItem>
                </>
              )}
              <DropdownMenuSeparator />
              <DropdownMenuItem onClick={handleLogout}>
                Logout
              </DropdownMenuItem>
              {versionQuery.data && (
                <>
                  <DropdownMenuSeparator />
                  <div className="px-1.5 py-1 text-xs text-muted-foreground">v{versionQuery.data.version}</div>
                </>
              )}
            </DropdownMenuContent>
          </DropdownMenu>
        </div>
      </header>
      {boardDataQuery.isError && (
        <div className="mx-4 mt-4 rounded-lg border border-destructive/30 bg-destructive/5 p-4 text-center text-sm text-destructive">
          Failed to load board. Check your auth key and try again.
        </div>
      )}
      {(boardDataQuery.isLoading || boardMetaQuery.isLoading) && (
        <p className="py-8 text-center text-muted-foreground">Loading board...</p>
      )}
      <DndContext
        sensors={sensors}
        collisionDetection={kanbanCollision}
        onDragStart={onDragStart}
        onDragOver={onDragOver}
        onDragEnd={onDragEnd}
      >
        <section
          className="grid min-h-0 flex-1 gap-4 overflow-x-auto p-4 pb-2"
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
              onAddCard={() => { setCreateLaneId(lane.id); setCreateDialogKey((k) => k + 1); setCreateOpen(true); }}
              activeCardId={activeCardId}
              sizeMap={sizeMap}
              enrichedCardMap={enrichedCardMap}
            />
          ))}
        </section>
        <DragOverlay>
          {(() => {
            const activeCard = activeCardId ? localCards.find((c) => c.id === activeCardId) : undefined;
            return activeCard ? <CardOverlay card={activeCard} sizeMap={sizeMap} /> : null;
          })()}
        </DragOverlay>
      </DndContext>

      <CardDetailSheet
        card={selectedCard}
        open={isDetailOpen}
        onOpenChange={handleDetailOpenChange}
        currentUserId={currentUserId}
        currentUserRole={currentUserRole}
        lanes={lanes}
        boardId={boardId}
        sizes={sizes}
        cardsInLane={selectedCard ? (byLane.get(selectedCard.laneId) ?? []) : []}
        onNavigateCard={handleNavigateCard}
      />

      {boardId && (
        <CreateCardDialog key={createDialogKey} boardId={boardId} lanes={lanes} sizes={sizes} open={createOpen} onOpenChange={setCreateOpen} defaultLaneId={createLaneId} />
      )}

      {boardId && (
        <AdminPanel boardId={boardId} open={adminOpen} onOpenChange={setAdminOpen} />
      )}

      <GlobalAdminPanel open={globalAdminOpen} onOpenChange={setGlobalAdminOpen} />
    </main>
  );
}
