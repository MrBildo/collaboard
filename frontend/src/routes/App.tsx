import { DndContext, DragOverlay } from '@dnd-kit/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { AdminPanel } from '@/components/AdminPanel';
import { BoardHeader } from '@/components/BoardHeader';
import { CardDetailSheet } from '@/components/CardDetailSheet';
import { CardOverlay } from '@/components/CardOverlay';
import { CreateCardDialog } from '@/components/CreateCardDialog';
import { GlobalAdminPanel } from '@/components/GlobalAdminPanel';
import { LaneColumn } from '@/components/LaneColumn';
import { LoginScreen } from '@/components/LoginScreen';
import { fetchBoardBySlug, fetchBoardData, fetchBoards, fetchCards, fetchMe, fetchUsers, fetchVersion } from '@/lib/api';
import { isLoggedIn, setUserKey, clearUserKey, setLastBoardSlug } from '@/lib/auth';
import { QUERY_DEFAULTS } from '@/lib/query-config';
import { queryKeys } from '@/lib/query-keys';
import { useBoardEvents } from '@/lib/use-board-events';
import { useBoardDnd } from '@/hooks/use-board-dnd';
import type { CardItem, CardSummary } from '@/types';

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

  const [createOpen, setCreateOpen] = useState(false);
  const [createLaneId, setCreateLaneId] = useState<string | undefined>(undefined);
  const [createDialogKey, setCreateDialogKey] = useState(0);
  const [adminOpen, setAdminOpen] = useState(false);
  const [globalAdminOpen, setGlobalAdminOpen] = useState(false);

  const lanes = useMemo(() => boardDataQuery.data?.lanes ?? [], [boardDataQuery.data]);
  const sizes = useMemo(() => boardDataQuery.data?.sizes ?? [], [boardDataQuery.data]);
  const sizeMap = useMemo(() => new Map(sizes.map((s) => [s.id, s.name])), [sizes]);
  const serverCards = useMemo(() => boardDataQuery.data?.cards ?? [], [boardDataQuery.data]);
  const laneIds = useMemo(() => new Set(lanes.map((l) => l.id)), [lanes]);

  const {
    sensors,
    collisionDetection,
    handleDragStart,
    handleDragOver,
    handleDragEnd,
    activeCard,
    localCards,
  } = useBoardDnd({ boardId, serverCards, laneIds });

  // Derive selected card and detail-open state from URL (cardNumber param is the source of truth)
  const selectedCard = useMemo(() => {
    if (!cardNumber || serverCards.length === 0) return null;
    const num = parseInt(cardNumber, 10);
    return serverCards.find((c) => c.number === num) ?? null;
  }, [cardNumber, serverCards]);
  const detailOpen = selectedCard !== null;

  const handleDetailOpenChange = useCallback((open: boolean) => {
    if (!open) {
      navigate(`/boards/${slug}`, { replace: true });
    }
  }, [slug, navigate]);

  const handleNavigateCard = useCallback((cardNumber: number) => {
    navigate(`/boards/${slug}/cards/${cardNumber}`, { replace: true });
  }, [slug, navigate]);

  const byLane = useMemo(() => {
    const map = new Map<string, CardItem[]>();
    lanes.forEach((lane) => map.set(lane.id, []));
    localCards.forEach((card) => map.get(card.laneId)?.push(card));
    return map;
  }, [lanes, localCards]);

  const handleCardClick = (card: CardItem) => {
    navigate(`/boards/${slug}/cards/${card.number}`, { replace: true });
  };

  const handleNewCard = useCallback(() => {
    setCreateLaneId(undefined);
    setCreateDialogKey((k) => k + 1);
    setCreateOpen(true);
  }, []);

  if (!loggedIn) {
    return <LoginScreen onLogin={handleLogin} />;
  }

  const boards = boardsQuery.data ?? [];

  return (
    <main className="flex h-screen flex-col bg-background text-foreground">
      <BoardHeader
        boards={boards}
        boardName={board?.name}
        currentSlug={slug}
        isAdmin={isAdmin}
        version={versionQuery.data?.version}
        onNewCard={handleNewCard}
        onBoardSettings={() => setAdminOpen(true)}
        onGlobalAdmin={() => setGlobalAdminOpen(true)}
        onLogout={handleLogout}
      />
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
        collisionDetection={collisionDetection}
        onDragStart={handleDragStart}
        onDragOver={handleDragOver}
        onDragEnd={handleDragEnd}
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
              activeCardId={activeCard?.id ?? null}
              sizeMap={sizeMap}
              enrichedCardMap={enrichedCardMap}
            />
          ))}
        </section>
        <DragOverlay>
          {activeCard ? <CardOverlay card={activeCard} sizeMap={sizeMap} /> : null}
        </DragOverlay>
      </DndContext>

      <CardDetailSheet
        card={selectedCard}
        open={detailOpen}
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
