import { useLayoutEffect, useRef, useState, type RefObject } from 'react';

export type LabelLike = {
  id: string;
  name: string;
  color?: string | null;
};

export type LabelDisplayMode = 'full' | 'dot';

export type LabelLayoutItem = {
  label: LabelLike;
  mode: LabelDisplayMode;
};

export type LabelLayout = {
  items: LabelLayoutItem[];
  overflowCount: number;
};

// Badge geometry constants (derived from Tailwind classes)
const GAP = 4;            // gap-1 = 0.25rem = 4px
const BADGE_H_PAD = 16;   // px-2 = 0.5rem × 2 = 16px
const BADGE_BUFFER = 6;   // border + rounding + sub-pixel safety
const DOT_SIZE = 24;      // w-6 = 1.5rem = 24px (collapsed label pill)
const FONT = '500 12px "Space Grotesk", ui-sans-serif, system-ui, sans-serif';

let canvas: HTMLCanvasElement | null = null;

function measureText(text: string): number {
  if (!canvas) canvas = document.createElement('canvas');
  const ctx = canvas.getContext('2d')!;
  ctx.font = FONT;
  return Math.ceil(ctx.measureText(text).width);
}

function badgeWidth(text: string): number {
  return measureText(text) + BADGE_H_PAD + BADGE_BUFFER;
}

function totalWidth(labels: LabelLike[], modes: LabelDisplayMode[], plusBadgeW: number): number {
  const count = labels.length + (plusBadgeW > 0 ? 1 : 0);
  if (count === 0) return 0;

  let w = 0;
  for (let i = 0; i < labels.length; i++) {
    w += modes[i] === 'full' ? badgeWidth(labels[i].name) : DOT_SIZE;
  }
  w += plusBadgeW;
  w += (count - 1) * GAP;
  return w;
}

function computeLayout(labels: LabelLike[], containerWidth: number): LabelLayout {
  if (labels.length === 0 || containerWidth <= 0) {
    return { items: labels.map((l) => ({ label: l, mode: 'full' })), overflowCount: 0 };
  }

  const modes: LabelDisplayMode[] = labels.map(() => 'full');

  // Phase 1 & 2: greedily collapse longest full label to dot until it fits
  while (totalWidth(labels, modes, 0) > containerWidth) {
    let longestIdx = -1;
    let longestW = -1;
    for (let i = 0; i < labels.length; i++) {
      if (modes[i] === 'full') {
        const w = badgeWidth(labels[i].name);
        if (w > longestW) {
          longestIdx = i;
          longestW = w;
        }
      }
    }
    if (longestIdx === -1) break; // all are already dots
    modes[longestIdx] = 'dot';
  }

  // Check if all-dots fits
  if (totalWidth(labels, modes, 0) <= containerWidth) {
    return { items: labels.map((l, i) => ({ label: l, mode: modes[i] })), overflowCount: 0 };
  }

  // Phase 3: all dots and still overflow → figure out how many dots + "+N" fits
  const plusNW = badgeWidth('+99'); // worst-case +N badge width
  const slotSize = DOT_SIZE + GAP;
  const available = containerWidth - plusNW - GAP;
  const fittingDots = Math.max(0, Math.min(Math.floor((available + GAP) / slotSize), labels.length));
  const overflowCount = labels.length - fittingDots;

  return {
    items: labels.slice(0, fittingDots).map((l) => ({ label: l, mode: 'dot' })),
    overflowCount,
  };
}

export function useLabelLayout(
  labels: LabelLike[],
  containerRef: RefObject<HTMLElement | null>,
): LabelLayout {
  const [layout, setLayout] = useState<LabelLayout>(() => ({
    items: labels.map((l) => ({ label: l, mode: 'full' })),
    overflowCount: 0,
  }));

  // Keep a ref so the ResizeObserver callback always reads fresh labels
  const labelsRef = useRef(labels);
  labelsRef.current = labels;

  // Stable key — only re-run effect when actual label set changes
  const labelsKey = labels.map((l) => l.id).join(',');

  // Throttle ResizeObserver with rAF to avoid layout thrashing
  const rafRef = useRef(0);

  useLayoutEffect(() => {
    const el = containerRef.current;
    const currentLabels = labelsRef.current;
    if (!el || currentLabels.length === 0) {
      setLayout({ items: currentLabels.map((l) => ({ label: l, mode: 'full' })), overflowCount: 0 });
      return;
    }

    const recompute = () => {
      cancelAnimationFrame(rafRef.current);
      rafRef.current = requestAnimationFrame(() => {
        const width = el.clientWidth;
        if (width > 0) {
          setLayout(computeLayout(labelsRef.current, width));
        }
      });
    };

    // Initial computation (synchronous for useLayoutEffect — avoid flash)
    const width = el.clientWidth;
    if (width > 0) {
      setLayout(computeLayout(currentLabels, width));
    }

    const observer = new ResizeObserver(recompute);
    observer.observe(el);
    return () => {
      observer.disconnect();
      cancelAnimationFrame(rafRef.current);
    };
  }, [labelsKey, containerRef]);

  return layout;
}
