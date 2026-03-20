import { DndContext, DragOverlay } from '@dnd-kit/core';
import { useQuery } from '@tanstack/react-query';
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
import { fetchBoards, fetchVersion } from '@/lib/api';
import { queryKeys } from '@/lib/query-keys';
import { useAuth } from '@/hooks/use-auth';
import { useBoardData } from '@/hooks/use-board-data';
import { useBoardEvents } from '@/hooks/use-board-events';
import { useBoardDnd } from '@/hooks/use-board-dnd';
import { useCurrentUser } from '@/hooks/use-current-user';
import { useLaneCollapse } from '@/hooks/use-lane-collapse';
import { cn } from '@/lib/utils';
import { useLaneResize } from '@/hooks/use-lane-resize';
import type { CardItem } from '@/types';

export function App() {
  const { slug, cardNumber } = useParams<{ slug: string; cardNumber: string }>();
  const navigate = useNavigate();
  const { loggedIn, handleLogin, handleLogout } = useAuth();
  const { currentUserId, currentUserRole, isAdmin } = useCurrentUser(loggedIn);

  const {
    board,
    boardId,
    boardMetaQuery,
    boardDataQuery,
    lanes,
    sizes,
    sizeMap,
    serverCards,
    enrichedCardMap,
  } = useBoardData(slug, loggedIn);

  // SSE for this board
  useBoardEvents(boardId);

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

  const laneIds = useMemo(() => new Set(lanes.map((l) => l.id)), [lanes]);

  const {
    sensors,
    collisionDetection,
    activeCardId,
    localCards,
    sortedServerCards,
    onDragStart,
    onDragOver,
    onDragEnd,
  } = useBoardDnd(boardId, serverCards, laneIds);

  // Derive selected card and detail-open state from URL (cardNumber param is the source of truth)
  const selectedCard = useMemo(() => {
    if (!cardNumber || sortedServerCards.length === 0) return null;
    const num = parseInt(cardNumber, 10);
    return sortedServerCards.find((c) => c.number === num) ?? null;
  }, [cardNumber, sortedServerCards]);
  const isDetailOpen = selectedCard !== null;

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

  const { isCollapsed, toggle: toggleLaneCollapse, initDefaults: initCollapseDefaults } = useLaneCollapse(boardId);

  const laneIdList = useMemo(() => lanes.map((l) => l.id), [lanes]);
  const {
    sectionRef,
    gridTemplateColumns,
    onHandleMouseDown,
    draggingIndex: resizingHandleIndex,
  } = useLaneResize(boardId ?? '', laneIdList);

  // Auto-collapse empty lanes on first data load (when no saved state exists)
  useEffect(() => {
    if (lanes.length > 0 && byLane.size > 0) {
      initCollapseDefaults(lanes.map((l) => ({ id: l.id, cardCount: byLane.get(l.id)?.length ?? 0 })));
    }
  }, [lanes, byLane, initCollapseDefaults]);

  const handleCardClick = useCallback((card: CardItem) => {
    navigate(`/boards/${slug}/cards/${card.number}`, { replace: true });
  }, [slug, navigate]);

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
        currentSlug={slug}
        boardName={board?.name}
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
        onDragStart={onDragStart}
        onDragOver={onDragOver}
        onDragEnd={onDragEnd}
      >
        <section
          ref={sectionRef}
          className="grid min-h-0 flex-1 overflow-x-auto p-4 pb-2"
          style={{
            gridTemplateColumns,
            gap: 0,
          }}
          aria-label="Kanban board"
        >
          {lanes.map((lane, i) => (
            <div key={lane.id} className="flex min-h-0 min-w-0">
              <div className="min-w-0 flex-1 p-2">
                <LaneColumn
                  lane={lane}
                  cards={byLane.get(lane.id) ?? []}
                  onCardClick={handleCardClick}
                  onAddCard={() => { setCreateLaneId(lane.id); setCreateDialogKey((k) => k + 1); setCreateOpen(true); }}
                  activeCardId={activeCardId}
                  sizeMap={sizeMap}
                  enrichedCardMap={enrichedCardMap}
                  isCollapsed={isCollapsed(lane.id)}
                  onToggleCollapse={() => toggleLaneCollapse(lane.id)}
                />
              </div>
              {i < lanes.length - 1 && (
                <div
                  onMouseDown={(e) => onHandleMouseDown(i, e)}
                  className={cn(
                    'relative hidden w-2 shrink-0 cursor-col-resize md:block',
                    'after:absolute after:inset-y-4 after:left-1/2 after:w-px after:-translate-x-1/2 after:rounded-full after:bg-border after:transition-colors',
                    resizingHandleIndex === i ? 'after:bg-primary/60' : 'hover:after:bg-primary/40',
                  )}
                />
              )}
            </div>
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
