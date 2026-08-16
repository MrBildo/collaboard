import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';

const STORAGE_PREFIX = 'collaboard-lane-widths-';
const MIN_LANE_WIDTH = 280;
const HANDLE_HIT_WIDTH = 16; // px — invisible hit area width for drag handles

type LaneWidths = Record<string, number>;

function storageKey(boardId: string): string {
  return `${STORAGE_PREFIX}${boardId}`;
}

function loadWidths(boardId: string): LaneWidths {
  try {
    const stored = localStorage.getItem(storageKey(boardId));
    if (stored) return JSON.parse(stored) as LaneWidths;
  } catch {
    // localStorage unavailable
  }
  return {};
}

function saveWidths(boardId: string, widths: LaneWidths) {
  try {
    localStorage.setItem(storageKey(boardId), JSON.stringify(widths));
  } catch {
    // localStorage unavailable
  }
}

export function useLaneResize(boardId: string, laneIds: string[]) {
  const [widths, setWidths] = useState<LaneWidths>(() => loadWidths(boardId));
  const [draggingIndex, setDraggingIndex] = useState<number | null>(null);
  const startXRef = useRef(0);
  const startLeftRef = useRef(0);
  const startRightRef = useRef(0);
  const sectionRef = useRef<HTMLElement | null>(null);

  // Handle positions (px from section left edge) — computed from DOM after render
  const [handlePositions, setHandlePositions] = useState<number[]>([]);

  const computeHandlePositions = useCallback(() => {
    const el = sectionRef.current;
    if (!el) return;
    const children = el.querySelectorAll<HTMLElement>(':scope > [data-lane]');
    const positions: number[] = [];
    children.forEach((child, i) => {
      // One handle per lane, sitting at that lane's right edge. Handle `i` resizes
      // lane `i`. The LAST lane gets a handle too — without it the
      // right-most lane had no resize affordance and was stuck (every interior lane
      // could be widened by grabbing the boundary on its right; the last lane's
      // right edge had no handle). For interior handles the right edge is the gap
      // between lane `i` and `i+1`, so center the handle in the 8px gap; for the
      // last lane there is no following gap, so the handle sits on the lane's own
      // right edge.
      const isLast = i === children.length - 1;
      positions.push(child.offsetLeft + child.offsetWidth + (isLast ? 0 : 8));
    });
    setHandlePositions(positions);
  }, []);

  // Recompute handle positions on resize and after width changes
  useLayoutEffect(() => {
    const el = sectionRef.current;
    if (!el) return;
    computeHandlePositions();
    const observer = new ResizeObserver(computeHandlePositions);
    observer.observe(el);
    return () => observer.disconnect();
  }, [computeHandlePositions]);

  // Also recompute when widths or lanes change
  useEffect(() => {
    // Defer to next frame so the grid has re-laid out
    const id = requestAnimationFrame(computeHandlePositions);
    return () => cancelAnimationFrame(id);
  }, [widths, laneIds, computeHandlePositions]);

  // Reload widths when board changes
  useEffect(() => {
    setWidths(loadWidths(boardId));
  }, [boardId]);

  // Persist on change
  useEffect(() => {
    saveWidths(boardId, widths);
  }, [boardId, widths]);

  // Pre-computed grid string.
  // Every lane is a fixed-width track (its resized px, or MIN_LANE_WIDTH at
  // default) — no track is `1fr`. The old rightmost-lane `1fr`
  // anchored the last lane to the viewport, so widening the window inflated it.
  // With every track fixed, the grid is exactly as wide as its lanes; the
  // section's `justify-start` (App.tsx) parks any leftover viewport width to the
  // right of the last lane as plain board background instead of growing a lane.
  const gridTemplateColumns = useMemo(() => {
    if (laneIds.length === 0) return undefined;

    const hasCustom = laneIds.some((id) => widths[id] && widths[id] >= MIN_LANE_WIDTH);
    if (!hasCustom) {
      return `repeat(${laneIds.length}, ${MIN_LANE_WIDTH}px)`;
    }

    return laneIds
      .map((id) => {
        const w = widths[id];
        return w && w >= MIN_LANE_WIDTH ? `${Math.round(w)}px` : `${MIN_LANE_WIDTH}px`;
      })
      .join(' ');
  }, [laneIds, widths]);

  const onHandleMouseDown = useCallback(
    (handleIndex: number, e: React.MouseEvent) => {
      e.preventDefault();
      const section = sectionRef.current;
      if (!section) return;

      const leftId = laneIds[handleIndex];
      // The last handle resizes the last lane with no right neighbor to donate to;
      // rightId is intentionally undefined there. Only bail if there's no left lane.
      const rightId = laneIds[handleIndex + 1];
      if (!leftId) return;

      // Snapshot ALL lanes to their current rendered px widths so the first
      // drag frame has a concrete px baseline for every lane (default lanes
      // render at MIN_LANE_WIDTH; this captures any that the browser rounded).
      const children = section.querySelectorAll<HTMLElement>(':scope > [data-lane]');
      const snapshot: LaneWidths = {};
      children.forEach((child, i) => {
        if (i < laneIds.length) {
          snapshot[laneIds[i]] = child.getBoundingClientRect().width;
        }
      });
      setWidths(snapshot);

      startXRef.current = e.clientX;
      startLeftRef.current = snapshot[leftId] ?? MIN_LANE_WIDTH;
      startRightRef.current = snapshot[rightId] ?? MIN_LANE_WIDTH;
      setDraggingIndex(handleIndex);
    },
    [laneIds],
  );

  useEffect(() => {
    if (draggingIndex === null) return;

    document.body.classList.add('select-none');
    document.body.style.cursor = 'col-resize';

    const leftId = laneIds[draggingIndex];
    const rightId = laneIds[draggingIndex + 1];

    function onMouseMove(e: MouseEvent) {
      // The left lane can never go below MIN. Dragging left past that is clamped;
      // dragging right is unbounded (the board scrolls to accommodate).
      const rawDelta = e.clientX - startXRef.current;
      const minDelta = -(startLeftRef.current - MIN_LANE_WIDTH);
      const delta = Math.max(minDelta, rawDelta);

      // The resize is NOT zero-sum-clamped to the neighbor's slack.
      // On a board with enough lanes to overflow horizontally every lane sits at
      // MIN, so the neighbor had zero slack and the old `maxDelta` clamp pinned the
      // drag to zero movement — resize "stopped working". Now the left lane always
      // gets the full delta; the neighbor donates only down to MIN (so a widen on a
      // full board scrolls the board instead of dying), and reclaims space on a
      // shrink. When the board still fits, the neighbor absorbs the whole delta and
      // the original boundary-follows-cursor feel is preserved.
      setWidths((prev) => {
        const next = { ...prev, [leftId]: Math.round(startLeftRef.current + delta) };

        // The last lane's handle has no right neighbor. The lane simply grows or
        // shrinks against the board background — exactly the missing affordance
        // that left the right-most lane stuck. Interior handles keep the
        // neighbor-donation feel (the neighbor donates down to MIN).
        if (rightId) {
          next[rightId] = Math.round(Math.max(MIN_LANE_WIDTH, startRightRef.current - delta));
        }

        return next;
      });
    }

    function onMouseUp() {
      setDraggingIndex(null);
      document.body.classList.remove('select-none');
      document.body.style.cursor = '';
    }

    document.addEventListener('mousemove', onMouseMove);
    document.addEventListener('mouseup', onMouseUp);
    return () => {
      document.removeEventListener('mousemove', onMouseMove);
      document.removeEventListener('mouseup', onMouseUp);
      document.body.classList.remove('select-none');
      document.body.style.cursor = '';
    };
  }, [draggingIndex, laneIds]);

  const resetWidths = useCallback(() => {
    setWidths({});
  }, []);

  return {
    sectionRef,
    gridTemplateColumns,
    handlePositions,
    handleHitWidth: HANDLE_HIT_WIDTH,
    onHandleMouseDown,
    draggingIndex,
    resetWidths,
  };
}
