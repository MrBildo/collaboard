import { describe, test, expect, vi, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useIsMobile } from './use-is-mobile';

// A controllable matchMedia stub: it records the change listener so a test can
// flip `matches` and fire `change`, exercising the useSyncExternalStore subscribe
// path the resize-across-the-breakpoint transition relies on.
function installMatchMedia(initialMatches: boolean) {
  const listeners = new Set<() => void>();
  let matches = initialMatches;

  const mql = {
    get matches() {
      return matches;
    },
    media: '(max-width: 767.98px)',
    onchange: null,
    addEventListener: (_event: string, cb: () => void) => listeners.add(cb),
    removeEventListener: (_event: string, cb: () => void) => listeners.delete(cb),
    addListener: () => {},
    removeListener: () => {},
    dispatchEvent: () => false,
  };

  window.matchMedia = vi.fn().mockReturnValue(mql) as unknown as typeof window.matchMedia;

  return {
    setMatches(next: boolean) {
      matches = next;
      listeners.forEach((cb) => cb());
    },
  };
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe('useIsMobile', () => {
  test('returns false (desktop) when the viewport is at or above the md breakpoint', () => {
    installMatchMedia(false);
    const { result } = renderHook(() => useIsMobile());
    expect(result.current).toBe(false);
  });

  test('returns true (mobile) when the viewport is below the md breakpoint', () => {
    installMatchMedia(true);
    const { result } = renderHook(() => useIsMobile());
    expect(result.current).toBe(true);
  });

  test('reacts to a resize across the breakpoint without a stuck value', () => {
    const media = installMatchMedia(false);
    const { result } = renderHook(() => useIsMobile());
    expect(result.current).toBe(false);

    // Shrink past the breakpoint — the change event must flip the hook to mobile.
    act(() => media.setMatches(true));
    expect(result.current).toBe(true);

    // Grow back — the hook must return to desktop, never sticking on mobile.
    act(() => media.setMatches(false));
    expect(result.current).toBe(false);
  });
});
