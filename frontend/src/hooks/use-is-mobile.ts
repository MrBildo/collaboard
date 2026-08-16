import { useSyncExternalStore } from 'react';

// The single source of truth for "are we in mobile view" across the app. The
// breakpoint is Tailwind's `md` (768px) — the same one the responsive CSS uses
// (`max-md:` / `md:` variants that collapse the board header into the gear menu
// and turn lanes into collapsible stacks). Drag-and-drop is a desktop-only
// feature; every dnd-kit surface gates off this one hook so the rule has
// a single definition.
//
// `max-width: 767.98px` mirrors Tailwind's `md` boundary: `md:` styles apply at
// >= 768px, so "mobile" is everything below that. The .98 avoids a one-pixel
// dead zone at exactly 768px on fractional-DPI displays.
const MOBILE_QUERY = '(max-width: 767.98px)';

function subscribe(onChange: () => void): () => void {
  const mql = window.matchMedia(MOBILE_QUERY);
  mql.addEventListener('change', onChange);
  return () => mql.removeEventListener('change', onChange);
}

function getSnapshot(): boolean {
  return window.matchMedia(MOBILE_QUERY).matches;
}

// SSR / non-DOM environments have no viewport — default to desktop (drag enabled)
// so the server-rendered markup matches the most common client and never claims
// "mobile" before hydration can confirm it.
function getServerSnapshot(): boolean {
  return false;
}

export function useIsMobile(): boolean {
  return useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);
}
