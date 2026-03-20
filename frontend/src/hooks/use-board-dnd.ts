import {
  type DragEndEvent,
  type DragOverEvent,
  type DragStartEvent,
  MouseSensor,
  TouchSensor,
  closestCenter,
  pointerWithin,
  useSensor,
  useSensors,
  type CollisionDetection,
} from '@dnd-kit/core';
import { arrayMove } from '@dnd-kit/sortable';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { reorderCard } from '@/lib/api';
import { queryKeys } from '@/lib/query-keys';
import type { CardItem } from '@/types';

const kanbanCollision: CollisionDetection = (args) => {
  const pointerCollisions = pointerWithin(args);
  if (pointerCollisions.length > 0) return pointerCollisions;
  return closestCenter(args);
};

export function useBoardDnd(boardId: string | undefined, serverCards: CardItem[], laneIds: Set<string>) {
  const queryClient = useQueryClient();

  const mouseSensor = useSensor(MouseSensor, { activationConstraint: { distance: 8 } });
  const touchSensor = useSensor(TouchSensor, { activationConstraint: { delay: 200, tolerance: 5 } });
  const sensors = useSensors(mouseSensor, touchSensor);

  const [activeCardId, setActiveCardId] = useState<string | null>(null);
  const [dragPhase, setDragPhase] = useState<'idle' | 'dragging' | 'settling'>('idle');
  const [dragCards, setDragCards] = useState<CardItem[] | null>(null);

  // Reset drag state when switching boards (React-recommended "adjust state during render" pattern)
  const [prevBoardId, setPrevBoardId] = useState(boardId);
  if (boardId !== prevBoardId) {
    setPrevBoardId(boardId);
    setDragPhase('idle');
    setDragCards(null);
    setActiveCardId(null);
  }

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

  const localCards = dragPhase === 'idle' ? sortedServerCards : (dragCards ?? sortedServerCards);

  const reorderMutation = useMutation({
    mutationFn: (vars: { cardId: string; laneId: string; index: number }) =>
      reorderCard(vars.cardId, vars.laneId, vars.index),
    onMutate: async () => {
      if (!boardId) return;
      await queryClient.cancelQueries({ queryKey: queryKeys.boards.data(boardId) });
    },
    onSuccess: () => {
      if (!boardId) return;
      // Invalidate instead of replacing cache — the reorder API returns
      // plain CardItem objects without labels/enrichment. A refetch
      // pulls full CardSummary data including labels.
      queryClient.invalidateQueries({ queryKey: queryKeys.boards.data(boardId) });
    },
    onError: () => {
      if (!boardId) return;
      queryClient.invalidateQueries({ queryKey: queryKeys.boards.data(boardId) });
    },
    onSettled: () => {
      setDragPhase('idle');
      setDragCards(null);
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

  return {
    sensors,
    collisionDetection: kanbanCollision,
    activeCardId,
    localCards,
    sortedServerCards,
    onDragStart,
    onDragOver,
    onDragEnd,
  };
}
