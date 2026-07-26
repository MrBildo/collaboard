import { describe, test, expect, vi, beforeEach, afterEach, type MockInstance } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement, type ReactNode } from 'react';
import { useBoardEvents } from './use-board-events';
import { queryKeys } from '@/lib/query-keys';

// getApiBaseUrl pulls from runtime-config (resolved at app boot). Stub it so the
// EventSource URL is deterministic and the test doesn't depend on boot order.
vi.mock('@/lib/runtime-config', () => ({
  getApiBaseUrl: () => '/api/v1',
}));

// Minimal fake EventSource: records the listeners registered against it and
// whether close() was called, and lets a test fire a named event. Assigned to
// global.EventSource so the hook constructs this instead of the real thing.
class FakeEventSource {
  static instances: FakeEventSource[] = [];

  url: string;
  listeners = new Map<string, Set<EventListener>>();
  closed = false;
  onerror: ((this: EventSource, ev: Event) => unknown) | null = null;

  constructor(url: string) {
    this.url = url;
    FakeEventSource.instances.push(this);
  }

  addEventListener(type: string, listener: EventListener) {
    const set = this.listeners.get(type) ?? new Set();
    set.add(listener);
    this.listeners.set(type, set);
  }

  close() {
    this.closed = true;
  }

  // Test-only: dispatch a named event to all registered listeners.
  emit(type: string) {
    for (const listener of this.listeners.get(type) ?? []) {
      listener(new Event(type));
    }
  }
}

const SSE_DEBOUNCE_MS = 300;

function createWrapper(queryClient: QueryClient) {
  return function Wrapper({ children }: { children: ReactNode }) {
    return createElement(QueryClientProvider, { client: queryClient }, children);
  };
}

let queryClient: QueryClient;
let invalidateSpy: MockInstance;
let cancelSpy: MockInstance;

beforeEach(() => {
  vi.useFakeTimers();
  FakeEventSource.instances = [];
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (globalThis as any).EventSource = FakeEventSource;

  queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries').mockResolvedValue(undefined);
  cancelSpy = vi.spyOn(queryClient, 'cancelQueries').mockResolvedValue(undefined);
});

afterEach(() => {
  vi.useRealTimers();
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  delete (globalThis as any).EventSource;
});

describe('useBoardEvents', () => {
  test('does not open an EventSource when boardId is undefined', () => {
    renderHook(() => useBoardEvents(undefined), { wrapper: createWrapper(queryClient) });

    expect(FakeEventSource.instances).toHaveLength(0);
  });

  test('opens one EventSource at the board events URL when boardId is set', () => {
    renderHook(() => useBoardEvents('board-1'), { wrapper: createWrapper(queryClient) });

    expect(FakeEventSource.instances).toHaveLength(1);
    expect(FakeEventSource.instances[0].url).toBe('/api/v1/boards/board-1/events');
  });

  test('debounced board-updated invalidates the five query slices after the debounce window', () => {
    renderHook(() => useBoardEvents('board-1'), { wrapper: createWrapper(queryClient) });
    const es = FakeEventSource.instances[0];

    // Event arrives — nothing fires yet (the invalidation is queued behind the debounce).
    act(() => {
      es.emit('board-updated');
    });
    expect(invalidateSpy).not.toHaveBeenCalled();
    expect(cancelSpy).not.toHaveBeenCalled();

    // Debounce window elapses — the fan-out fires.
    act(() => {
      vi.advanceTimersByTime(SSE_DEBOUNCE_MS);
    });

    expect(cancelSpy).toHaveBeenCalledWith({ queryKey: queryKeys.boards.data('board-1') });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: queryKeys.boards.data('board-1') });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: queryKeys.boards.cards('board-1') });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: queryKeys.labels.all('board-1') });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: queryKeys.users.directory() });
    expect(invalidateSpy).toHaveBeenCalledWith({ predicate: expect.any(Function) });
    expect(invalidateSpy).toHaveBeenCalledTimes(5);
  });

  test('the history invalidation predicate matches card history queries and nothing else', () => {
    renderHook(() => useBoardEvents('board-1'), { wrapper: createWrapper(queryClient) });
    const es = FakeEventSource.instances[0];

    act(() => {
      es.emit('board-updated');
      vi.advanceTimersByTime(SSE_DEBOUNCE_MS);
    });

    const predicateCall = invalidateSpy.mock.calls.find(
      (call) => typeof call[0]?.predicate === 'function',
    );
    expect(predicateCall).toBeDefined();
    const predicate = predicateCall![0].predicate as (query: { queryKey: unknown[] }) => boolean;

    // A description edit rings the plain board bell, so both history queries of
    // any card must be caught — and the card's sibling queries must not be.
    expect(predicate({ queryKey: [...queryKeys.cards.historyMeta('card-1')] })).toBe(true);
    expect(predicate({ queryKey: [...queryKeys.cards.historyTrail('card-1')] })).toBe(true);
    expect(predicate({ queryKey: [...queryKeys.cards.labels('card-1')] })).toBe(false);
    expect(predicate({ queryKey: [...queryKeys.cards.comments('card-1')] })).toBe(false);
    expect(predicate({ queryKey: [...queryKeys.boards.data('board-1')] })).toBe(false);
  });

  test('rapid board-updated bursts collapse into a single fan-out', () => {
    renderHook(() => useBoardEvents('board-1'), { wrapper: createWrapper(queryClient) });
    const es = FakeEventSource.instances[0];

    act(() => {
      es.emit('board-updated');
      vi.advanceTimersByTime(100);
      es.emit('board-updated');
      vi.advanceTimersByTime(100);
      es.emit('board-updated');
    });
    // Two earlier events were superseded; only the last one's window matters.
    expect(invalidateSpy).not.toHaveBeenCalled();

    act(() => {
      vi.advanceTimersByTime(SSE_DEBOUNCE_MS);
    });

    expect(invalidateSpy).toHaveBeenCalledTimes(5);
  });

  test('unmount before the debounce fires closes the source and drops the queued invalidation', () => {
    const { unmount } = renderHook(() => useBoardEvents('board-1'), {
      wrapper: createWrapper(queryClient),
    });
    const es = FakeEventSource.instances[0];

    // Queue an invalidation, then tear down before the window elapses.
    act(() => {
      es.emit('board-updated');
    });
    act(() => {
      unmount();
    });

    expect(es.closed).toBe(true);

    // Advancing past the window must NOT fire the dropped invalidation — the
    // cleanup cleared the pending timeout (closure-capture correctness).
    act(() => {
      vi.advanceTimersByTime(SSE_DEBOUNCE_MS * 2);
    });
    expect(invalidateSpy).not.toHaveBeenCalled();
    expect(cancelSpy).not.toHaveBeenCalled();
  });

  test('boardId change before the debounce fires closes the old source and drops its queued invalidation', () => {
    const { rerender } = renderHook(({ boardId }) => useBoardEvents(boardId), {
      wrapper: createWrapper(queryClient),
      initialProps: { boardId: 'board-1' },
    });
    const first = FakeEventSource.instances[0];

    // Queue an invalidation against board-1, then switch boards before it fires.
    act(() => {
      first.emit('board-updated');
    });
    act(() => {
      rerender({ boardId: 'board-2' });
    });

    expect(first.closed).toBe(true);
    expect(FakeEventSource.instances).toHaveLength(2);
    const second = FakeEventSource.instances[1];
    expect(second.url).toBe('/api/v1/boards/board-2/events');

    // board-1's queued invalidation was dropped by the cleanup.
    act(() => {
      vi.advanceTimersByTime(SSE_DEBOUNCE_MS * 2);
    });
    expect(invalidateSpy).not.toHaveBeenCalled();

    // The new source's own event still fans out normally.
    act(() => {
      second.emit('board-updated');
      vi.advanceTimersByTime(SSE_DEBOUNCE_MS);
    });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: queryKeys.boards.data('board-2') });
    expect(invalidateSpy).toHaveBeenCalledTimes(5);
  });
});
