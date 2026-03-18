import { useCallback, useState } from 'react';

const STORAGE_KEY_PREFIX = 'collaboard-collapsed-lanes-';

type CollapseMap = Record<string, boolean>;

function readCollapseMap(boardId: string): CollapseMap | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY_PREFIX + boardId);
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
}

function writeCollapseMap(boardId: string, map: CollapseMap) {
  localStorage.setItem(STORAGE_KEY_PREFIX + boardId, JSON.stringify(map));
}

export function useLaneCollapse(boardId: string | undefined) {
  const [collapseMap, setCollapseMap] = useState<CollapseMap>(() =>
    boardId ? readCollapseMap(boardId) ?? {} : {},
  );
  const [defaultsApplied, setDefaultsApplied] = useState(false);

  // Reset when board changes (React-recommended "adjust state during render" pattern)
  const [prevBoardId, setPrevBoardId] = useState(boardId);
  if (boardId !== prevBoardId) {
    setPrevBoardId(boardId);
    setCollapseMap(boardId ? readCollapseMap(boardId) ?? {} : {});
    setDefaultsApplied(false);
  }

  const initDefaults = useCallback(
    (lanes: { id: string; cardCount: number }[]) => {
      if (!boardId || defaultsApplied) return;
      setDefaultsApplied(true);

      const saved = readCollapseMap(boardId);
      if (saved) return;

      const defaults: CollapseMap = {};
      for (const lane of lanes) {
        defaults[lane.id] = lane.cardCount === 0;
      }
      setCollapseMap(defaults);
      writeCollapseMap(boardId, defaults);
    },
    [boardId, defaultsApplied],
  );

  const toggle = useCallback(
    (laneId: string) => {
      setCollapseMap((prev) => {
        const next = { ...prev, [laneId]: !prev[laneId] };
        if (boardId) writeCollapseMap(boardId, next);
        return next;
      });
    },
    [boardId],
  );

  const isCollapsed = useCallback(
    (laneId: string) => !!collapseMap[laneId],
    [collapseMap],
  );

  return { isCollapsed, toggle, initDefaults };
}
