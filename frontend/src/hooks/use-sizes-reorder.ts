import {
  type DragEndEvent,
  type DragOverEvent,
  type DragStartEvent,
  MouseSensor,
  TouchSensor,
  useSensor,
  useSensors,
} from '@dnd-kit/core';
import { arrayMove } from '@dnd-kit/sortable';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useMemo, useState } from 'react';
import { reorderSizes } from '@/lib/api';
import { queryKeys } from '@/lib/query-keys';
import type { BoardData, CardSize } from '@/types';

// Card #306: size drag-drop reorder inside the Board Settings admin dialog — the
// vertical-list sibling of use-lanes-reorder (#305), one resource over. Same
// proven optimistic shape, its own self-contained <DndContext> scoped to the
// SizesTab (the admin dialog is the mobile-reachable reorder path, so the touch
// sensor is the point). It is deliberately symmetric with the lane reorder hook;
// the differences are all "size vs lane": ordinal not position, no archive-lane
// carve-out (every size on the board is in the reorder set), and the board-data
// merge touches the flat `sizes` list.
//
// Optimistic shape: local reorder during drag, reconcile on drop via
// POST /boards/{boardId}/sizes/reorder (server owns all ordinal math — dense
// 0..n-1), merge-not-overwrite into the sizes-list AND board-data caches on
// success, invalidate-rollback on error, no-op guard.
export function useSizesReorder(boardId: string, serverSizes: CardSize[]) {
  const queryClient = useQueryClient();

  // Touch-sensor arbitration is the new surface (the admin dialog is mobile-
  // reachable). Reuse the lane hook's proven thresholds: a MouseSensor distance
  // gate so a click on the handle isn't a drag, and a TouchSensor delay+tolerance
  // so a touch-press that turns into a scroll inside the dialog panel is not
  // stolen by the drag. Paired with `touch-action: none` on the handle in
  // SizesTab so the browser yields the gesture once the delay elapses.
  const mouseSensor = useSensor(MouseSensor, { activationConstraint: { distance: 8 } });
  const touchSensor = useSensor(TouchSensor, {
    activationConstraint: { delay: 200, tolerance: 5 },
  });
  const sensors = useSensors(mouseSensor, touchSensor);

  const [activeSizeId, setActiveSizeId] = useState<string | null>(null);
  const [dragSizes, setDragSizes] = useState<CardSize[] | null>(null);

  // Reset drag state when switching boards (React-recommended "adjust state
  // during render" pattern — mirrors use-lanes-reorder.ts).
  const [prevBoardId, setPrevBoardId] = useState(boardId);
  if (boardId !== prevBoardId) {
    setPrevBoardId(boardId);
    setDragSizes(null);
    setActiveSizeId(null);
  }

  const sortedServerSizes = useMemo(
    () => [...serverSizes].sort((a, b) => a.ordinal - b.ordinal),
    [serverSizes],
  );

  // During a drag, render the optimistic order; otherwise the server order.
  const localSizes = dragSizes ?? sortedServerSizes;

  const invalidate = () => {
    // The SizesTab subscribes to the sizes list query; the board page subscribes
    // to board-data. Refresh both so both views settle on the server's truth.
    queryClient.invalidateQueries({ queryKey: queryKeys.sizes.all(boardId) });
    queryClient.invalidateQueries({ queryKey: queryKeys.boards.data(boardId) });
  };

  const reorderMutation = useMutation({
    // #203 convention: the operator is looking at the list when they drag, so a
    // failed reorder snaps back via the onError invalidate (the self-evident
    // signal) and a toast adds the "why" — same disposition use-lanes-reorder uses.
    meta: { errorMessage: "Couldn't reorder sizes — try again" },
    mutationFn: (orderedSizeIds: string[]) => reorderSizes(boardId, orderedSizeIds),
    onMutate: async () => {
      await queryClient.cancelQueries({ queryKey: queryKeys.sizes.all(boardId) });
      await queryClient.cancelQueries({ queryKey: queryKeys.boards.data(boardId) });
    },
    onSuccess: (sizes) => {
      // Merge the server's new dense ordinals into the cached sizes list AND the
      // board-data sizes list, so both rendered views match before the SSE
      // refetch lands. The server returns the full board size set in final order.
      const ordinalById = new Map(sizes.map((s) => [s.id, s.ordinal]));

      queryClient.setQueryData<CardSize[]>(queryKeys.sizes.all(boardId), (old) => {
        if (!old) return old;
        const merged = old.map((size) => {
          const ordinal = ordinalById.get(size.id);
          return ordinal === undefined ? size : { ...size, ordinal };
        });
        return [...merged].sort((a, b) => a.ordinal - b.ordinal);
      });

      queryClient.setQueryData<BoardData>(queryKeys.boards.data(boardId), (old) => {
        if (!old) return old;
        const merged = old.sizes.map((size) => {
          const ordinal = ordinalById.get(size.id);
          return ordinal === undefined ? size : { ...size, ordinal };
        });
        return {
          ...old,
          sizes: [...merged].sort((a, b) => a.ordinal - b.ordinal),
        };
      });
    },
    onError: () => {
      invalidate();
    },
    onSettled: () => {
      setDragSizes(null);
    },
  });

  const onDragStart = (event: DragStartEvent) => {
    setActiveSizeId(String(event.active.id));
    setDragSizes(sortedServerSizes);
  };

  const onDragOver = (event: DragOverEvent) => {
    const { active, over } = event;
    if (!over) return;

    const activeId = String(active.id);
    const overId = String(over.id);
    if (activeId === overId) return;

    setDragSizes((prev) => {
      const base = prev ?? sortedServerSizes;
      const oldIdx = base.findIndex((s) => s.id === activeId);
      const newIdx = base.findIndex((s) => s.id === overId);
      if (oldIdx === -1 || newIdx === -1 || oldIdx === newIdx) return base;
      return arrayMove(base, oldIdx, newIdx);
    });
  };

  const onDragEnd = (event: DragEndEvent) => {
    const { active, over } = event;
    setActiveSizeId(null);

    if (!over) {
      setDragSizes(null);
      return;
    }

    const activeId = String(active.id);
    const overId = String(over.id);

    const base = dragSizes ?? sortedServerSizes;
    const oldIdx = base.findIndex((s) => s.id === activeId);
    const newIdx = base.findIndex((s) => s.id === overId);
    if (oldIdx === -1 || newIdx === -1) {
      setDragSizes(null);
      return;
    }

    const finalOrder = arrayMove(base, oldIdx, newIdx);

    // No-op guard: if the drop lands the size back where it started, the order
    // is unchanged — skip the round-trip (and the renumber it would trigger).
    const unchanged = finalOrder.every((size, i) => size.id === sortedServerSizes[i]?.id);
    if (unchanged) {
      setDragSizes(null);
      return;
    }

    setDragSizes(finalOrder);
    reorderMutation.mutate(finalOrder.map((s) => s.id));
  };

  return {
    sensors,
    activeSizeId,
    localSizes,
    onDragStart,
    onDragOver,
    onDragEnd,
  };
}
