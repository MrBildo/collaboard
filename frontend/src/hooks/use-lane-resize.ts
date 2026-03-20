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
    const sectionRect = el.getBoundingClientRect();
    const children = el.querySelectorAll<HTMLElement>(':scope > [data-lane]');
    const positions: number[] = [];
    children.forEach((child, i) => {
      if (i < children.length - 1) {
        const childRect = child.getBoundingClientRect();
        positions.push(childRect.right - sectionRect.left);
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

  const getEffectiveWidth = useCallback(
    (laneId: string, containerWidth: number) => {
      const stored = widths[laneId];
      if (stored && stored >= MIN_LANE_WIDTH) return stored;
      return Math.max(MIN_LANE_WIDTH, containerWidth / Math.max(laneIds.length, 1));
    },
    [widths, laneIds.length],
  );

  // Pre-computed grid string
  const gridTemplateColumns = useMemo(() => {
    if (laneIds.length === 0) return undefined;

    const hasCustom = laneIds.some((id) => widths[id] && widths[id] >= MIN_LANE_WIDTH);
    if (!hasCustom) {
      return `repeat(${laneIds.length}, minmax(${MIN_LANE_WIDTH}px, 1fr))`;
    }

    return laneIds
      .map((id) => {
        const w = widths[id];
        return w && w >= MIN_LANE_WIDTH ? `${Math.round(w)}px` : `minmax(${MIN_LANE_WIDTH}px, 1fr)`;
      })
      .join(' ');
  }, [laneIds, widths]);

  const onHandleMouseDown = useCallback(
    (handleIndex: number, e: React.MouseEvent) => {
      e.preventDefault();
      const section = sectionRef.current;
      if (!section) return;

      const leftId = laneIds[handleIndex];
      const rightId = laneIds[handleIndex + 1];
      if (!leftId || !rightId) return;

      const containerWidth = section.getBoundingClientRect().width;

      startXRef.current = e.clientX;
      startLeftRef.current = getEffectiveWidth(leftId, containerWidth);
      startRightRef.current = getEffectiveWidth(rightId, containerWidth);
      setDraggingIndex(handleIndex);
    },
    [laneIds, getEffectiveWidth],
  );

  useEffect(() => {
    if (draggingIndex === null) return;

    document.body.classList.add('select-none');
    document.body.style.cursor = 'col-resize';

    const leftId = laneIds[draggingIndex];
    const rightId = laneIds[draggingIndex + 1];

    function onMouseMove(e: MouseEvent) {
      const rawDelta = e.clientX - startXRef.current;
      // Clamp delta so neither lane goes below minimum
      const maxDelta = startRightRef.current - MIN_LANE_WIDTH;
      const minDelta = -(startLeftRef.current - MIN_LANE_WIDTH);
      const delta = Math.max(minDelta, Math.min(maxDelta, rawDelta));

      setWidths((prev) => ({
        ...prev,
        [leftId]: Math.round(startLeftRef.current + delta),
        [rightId]: Math.round(startRightRef.current - delta),
      }));
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
