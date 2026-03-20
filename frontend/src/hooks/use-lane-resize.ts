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
      if (i < children.length - 1) {
        // offsetLeft + offsetWidth gives position relative to the section's padding box
        // Add half the gap (8px) to center the handle in the gap
        positions.push(child.offsetLeft + child.offsetWidth + 8);
      }
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

  // Pre-computed grid string
  // During drag: all lanes are px (prevents rebalancing).
  // At rest: rightmost lane is 1fr (anchored to right edge).
  const isDragging = draggingIndex !== null;
  const gridTemplateColumns = useMemo(() => {
    if (laneIds.length === 0) return undefined;

    const hasCustom = laneIds.some((id) => widths[id] && widths[id] >= MIN_LANE_WIDTH);
    if (!hasCustom) {
      return `repeat(${laneIds.length}, minmax(${MIN_LANE_WIDTH}px, 1fr))`;
    }

    return laneIds
      .map((id, i) => {
        const w = widths[id];
        // During drag: all lanes are px to prevent rebalancing
        // At rest: rightmost lane fills remaining space
        if (i === laneIds.length - 1 && !isDragging) return 'minmax(280px, 1fr)';
        return w && w >= MIN_LANE_WIDTH ? `${Math.round(w)}px` : `minmax(${MIN_LANE_WIDTH}px, 1fr)`;
      })
      .join(' ');
  }, [laneIds, widths, isDragging]);

  const onHandleMouseDown = useCallback(
    (handleIndex: number, e: React.MouseEvent) => {
      e.preventDefault();
      const section = sectionRef.current;
      if (!section) return;

      const leftId = laneIds[handleIndex];
      const rightId = laneIds[handleIndex + 1];
      if (!leftId || !rightId) return;

      // Snapshot ALL lanes to their current rendered px widths so the grid
      // doesn't rebalance 1fr lanes during the drag
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
      const rawDelta = e.clientX - startXRef.current;
      const maxDelta = startRightRef.current - MIN_LANE_WIDTH;
      const minDelta = -(startLeftRef.current - MIN_LANE_WIDTH);
      const delta = Math.max(minDelta, Math.min(maxDelta, rawDelta));

      // Only store px width for the left lane; rightmost lane stays 1fr
      const isRightLast = rightId === laneIds[laneIds.length - 1];
      setWidths((prev) => {
        const next = { ...prev, [leftId]: Math.round(startLeftRef.current + delta) };
        if (!isRightLast) next[rightId] = Math.round(startRightRef.current - delta);
        else delete next[rightId]; // rightmost is always 1fr
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
