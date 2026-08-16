import type { Lane } from '@/types';

type LaneOverlayProps = {
  lane: Lane;
};

// The drag ghost for a lane reorder — a column-header ghost reusing
// the board's existing DragOverlay slot. Same visual language as CardOverlay
// (rounded card surface, primary top-accent, shadow); no new design tokens.
export function LaneOverlay({ lane }: LaneOverlayProps) {
  return (
    <div className="flex items-center gap-2 rounded-lg border border-lane-border border-t-2 border-t-primary bg-lane-bg px-4 py-3 shadow-xl">
      <span className="truncate text-sm font-semibold uppercase tracking-wide">{lane.name}</span>
    </div>
  );
}
