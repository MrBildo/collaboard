import { useCallback, useEffect, useRef, useState } from 'react';

const STORAGE_PREFIX = 'collaboard-lane-widths-';
const MIN_LANE_WIDTH = 280;
const DEFAULT_LANE_WIDTH = 0; // 0 = use 1fr (equal distribution)

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
      // Default: equal share
      return Math.max(MIN_LANE_WIDTH, containerWidth / Math.max(laneIds.length, 1));
    },
    [widths, laneIds.length],
  );

  const gridTemplateColumns = useCallback(
    (containerWidth: number) => {
      if (laneIds.length === 0) return undefined;

      const hasCustom = laneIds.some((id) => widths[id] && widths[id] >= MIN_LANE_WIDTH);
      if (!hasCustom) {
        // No custom widths — use equal columns with minmax
        return `repeat(${laneIds.length}, minmax(${MIN_LANE_WIDTH}px, 1fr))`;
      }

      // Mix of custom and default widths
      return laneIds
        .map((id) => {
          const w = widths[id];
          return w && w >= MIN_LANE_WIDTH ? `${Math.round(w)}px` : `minmax(${MIN_LANE_WIDTH}px, 1fr)`;
        })
        .join(' ');
    },
    [laneIds, widths],
  );

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
      const delta = e.clientX - startXRef.current;
      const newLeft = Math.max(MIN_LANE_WIDTH, startLeftRef.current + delta);
      const newRight = Math.max(MIN_LANE_WIDTH, startRightRef.current - delta);

      // Only update if both lanes stay above minimum
      if (newLeft >= MIN_LANE_WIDTH && newRight >= MIN_LANE_WIDTH) {
        setWidths((prev) => ({
          ...prev,
          [leftId]: Math.round(newLeft),
          [rightId]: Math.round(newRight),
        }));
      }
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
    onHandleMouseDown,
    isDragging: draggingIndex !== null,
    draggingIndex,
    resetWidths,
  };
}
