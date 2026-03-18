import { useCallback, useEffect, useRef, useState } from 'react';

const STORAGE_KEY = 'collaboard-comments-width';
const DEFAULT_WIDTH = 340;
const MIN_COMMENTS_WIDTH = 280;
const MIN_DETAILS_WIDTH = 300;

function loadWidth(): number {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored) {
      const parsed = Number(stored);
      if (!Number.isNaN(parsed) && parsed >= MIN_COMMENTS_WIDTH) return parsed;
    }
  } catch {
    // localStorage unavailable
  }
  return DEFAULT_WIDTH;
}

export function usePanelResize(containerRef: React.RefObject<HTMLElement | null>) {
  const [width, setWidth] = useState(loadWidth);
  const [isDragging, setIsDragging] = useState(false);
  const startXRef = useRef(0);
  const startWidthRef = useRef(0);

  const onMouseDown = useCallback(
    (e: React.MouseEvent) => {
      e.preventDefault();
      startXRef.current = e.clientX;
      startWidthRef.current = width;
      setIsDragging(true);
    },
    [width],
  );

  useEffect(() => {
    if (!isDragging) return;

    document.body.classList.add('select-none');
    document.body.style.cursor = 'col-resize';

    function onMouseMove(e: MouseEvent) {
      const container = containerRef.current;
      if (!container) return;

      const containerWidth = container.getBoundingClientRect().width;
      const maxWidth = containerWidth - MIN_DETAILS_WIDTH;

      // Dragging left increases comments width (panel is on the right)
      const delta = startXRef.current - e.clientX;
      const next = Math.min(maxWidth, Math.max(MIN_COMMENTS_WIDTH, startWidthRef.current + delta));
      setWidth(next);
    }

    function onMouseUp() {
      setIsDragging(false);
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
  }, [isDragging, containerRef]);

  // Persist to localStorage on change (debounced by animation frame)
  useEffect(() => {
    try {
      localStorage.setItem(STORAGE_KEY, String(Math.round(width)));
    } catch {
      // localStorage unavailable
    }
  }, [width]);

  return { width, isDragging, onMouseDown };
}
