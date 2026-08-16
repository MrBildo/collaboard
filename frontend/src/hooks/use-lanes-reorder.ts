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
import { reorderLanes } from '@/lib/api';
import { queryKeys } from '@/lib/query-keys';
import type { BoardData, Lane } from '@/types';

// Lane drag-drop reorder inside the Board Settings admin dialog — the touch-
// reachable fallback for reordering (use-lane-dnd.ts owns the board-page desktop path;
// its grab target is the lane header, which has no touch listeners). This hook is
// the vertical-list sibling for the admin tab: same proven optimistic shape, one
// axis down, but it is NOT folded into use-lane-dnd because that hook lives in the
// board's shared <DndContext> (card-vs-lane discrimination) while the admin tab
// has its own self-contained context — different mount, different axis, no card
// concern.
//
// Optimistic shape mirrors use-lane-dnd: local reorder during drag, reconcile on
// drop via POST /boards/{boardId}/lanes/reorder (server owns all position math),
// merge-not-overwrite into the board-data cache on success, invalidate-rollback on
// error, no-op guard. The one addition over the board hook: the LanesTab reads
// from the queryKeys.lanes.all(boardId) list query, so this hook invalidates that
// too (the board hook only had board-data subscribers to satisfy).
export function useLanesReorder(boardId: string, serverLanes: Lane[]) {
  const queryClient = useQueryClient();

  // Touch-sensor arbitration is the real new surface here (the admin dialog
  // is the mobile-reachable path). Reuse the board hook's proven thresholds: a
  // MouseSensor distance gate so a click on the handle isn't a drag, and a
  // TouchSensor delay+tolerance so a touch-press that turns into a scroll inside
  // the dialog panel is not stolen by the drag. Paired with `touch-action: none`
  // on the handle in LanesTab so the browser yields the gesture once the delay
  // elapses.
  const mouseSensor = useSensor(MouseSensor, { activationConstraint: { distance: 8 } });
  const touchSensor = useSensor(TouchSensor, {
    activationConstraint: { delay: 200, tolerance: 5 },
  });
  const sensors = useSensors(mouseSensor, touchSensor);

  const [activeLaneId, setActiveLaneId] = useState<string | null>(null);
  const [dragLanes, setDragLanes] = useState<Lane[] | null>(null);

  // Reset drag state when switching boards (React-recommended "adjust state
  // during render" pattern — mirrors use-lane-dnd.ts).
  const [prevBoardId, setPrevBoardId] = useState(boardId);
  if (boardId !== prevBoardId) {
    setPrevBoardId(boardId);
    setDragLanes(null);
    setActiveLaneId(null);
  }

  const sortedServerLanes = useMemo(
    () => [...serverLanes].sort((a, b) => a.position - b.position),
    [serverLanes],
  );

  // During a drag, render the optimistic order; otherwise the server order.
  const localLanes = dragLanes ?? sortedServerLanes;

  const invalidate = () => {
    // The LanesTab subscribes to the lanes list query; the board page subscribes
    // to board-data. Refresh both so both views settle on the server's truth.
    queryClient.invalidateQueries({ queryKey: queryKeys.lanes.all(boardId) });
    queryClient.invalidateQueries({ queryKey: queryKeys.boards.data(boardId) });
  };

  const reorderMutation = useMutation({
    // The operator is looking at the list when they drag, so a failed reorder
    // snaps back via the onError invalidate (the self-evident signal) and a
    // toast adds the "why" — same disposition use-lane-dnd uses.
    meta: { errorMessage: "Couldn't reorder lanes — try again" },
    mutationFn: (orderedLaneIds: string[]) => reorderLanes(boardId, orderedLaneIds),
    onMutate: async () => {
      await queryClient.cancelQueries({ queryKey: queryKeys.lanes.all(boardId) });
      await queryClient.cancelQueries({ queryKey: queryKeys.boards.data(boardId) });
    },
    onSuccess: (lanes) => {
      // Merge the server's new dense positions into the cached lane list AND the
      // board-data lane list, so both rendered views match before the SSE refetch
      // lands. The server returns only the reordered non-archive lanes; cards,
      // sizes, and any archive lane in the caches are left untouched.
      const positionById = new Map(lanes.map((l) => [l.id, l.position]));

      queryClient.setQueryData<Lane[]>(queryKeys.lanes.all(boardId), (old) => {
        if (!old) return old;
        const merged = old.map((lane) => {
          const position = positionById.get(lane.id);
          return position === undefined ? lane : { ...lane, position };
        });
        return [...merged].sort((a, b) => a.position - b.position);
      });

      queryClient.setQueryData<BoardData>(queryKeys.boards.data(boardId), (old) => {
        if (!old) return old;
        const merged = old.lanes.map((lane) => {
          const position = positionById.get(lane.id);
          return position === undefined ? lane : { ...lane, position };
        });
        return {
          ...old,
          lanes: [...merged].sort((a, b) => a.position - b.position),
        };
      });
    },
    onError: () => {
      invalidate();
    },
    onSettled: () => {
      setDragLanes(null);
    },
  });

  const onDragStart = (event: DragStartEvent) => {
    setActiveLaneId(String(event.active.id));
    setDragLanes(sortedServerLanes);
  };

  const onDragOver = (event: DragOverEvent) => {
    const { active, over } = event;
    if (!over) return;

    const activeId = String(active.id);
    const overId = String(over.id);
    if (activeId === overId) return;

    setDragLanes((prev) => {
      const base = prev ?? sortedServerLanes;
      const oldIdx = base.findIndex((l) => l.id === activeId);
      const newIdx = base.findIndex((l) => l.id === overId);
      if (oldIdx === -1 || newIdx === -1 || oldIdx === newIdx) return base;
      return arrayMove(base, oldIdx, newIdx);
    });
  };

  const onDragEnd = (event: DragEndEvent) => {
    const { active, over } = event;
    setActiveLaneId(null);

    if (!over) {
      setDragLanes(null);
      return;
    }

    const activeId = String(active.id);
    const overId = String(over.id);

    const base = dragLanes ?? sortedServerLanes;
    const oldIdx = base.findIndex((l) => l.id === activeId);
    const newIdx = base.findIndex((l) => l.id === overId);
    if (oldIdx === -1 || newIdx === -1) {
      setDragLanes(null);
      return;
    }

    const finalOrder = arrayMove(base, oldIdx, newIdx);

    // No-op guard: if the drop lands the lane back where it started, the order
    // is unchanged — skip the round-trip (and the renumber it would trigger).
    const unchanged = finalOrder.every((lane, i) => lane.id === sortedServerLanes[i]?.id);
    if (unchanged) {
      setDragLanes(null);
      return;
    }

    setDragLanes(finalOrder);
    reorderMutation.mutate(finalOrder.map((l) => l.id));
  };

  return {
    sensors,
    activeLaneId,
    localLanes,
    onDragStart,
    onDragOver,
    onDragEnd,
  };
}
