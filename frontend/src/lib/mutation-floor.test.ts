import { describe, test, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider, useMutation } from '@tanstack/react-query';
import { createElement, type ReactNode } from 'react';
import { createMutationFloor } from './mutation-floor';

// The floor's whole job is that a mutation failure surfaces *something* —
// either a toast (the floor) or, when the call site opts out, nothing from the
// floor (the call site renders it inline). That wiring is exactly the
// regression that is silent AND expensive: if it breaks, every mutation goes
// back to failing without a trace. So we test the floor's surface decision
// against the meta contract, not the per-mutation message strings.

vi.mock('sonner', () => ({
  toast: {
    error: vi.fn(),
    success: vi.fn(),
  },
}));

import { toast } from 'sonner';

const mockToastError = vi.mocked(toast.error);
const mockToastSuccess = vi.mocked(toast.success);

function makeClient() {
  return new QueryClient({
    mutationCache: createMutationFloor(),
    defaultOptions: { mutations: { retry: false } },
  });
}

function renderMutationWithMeta(meta: Record<string, unknown> | undefined, shouldFail: boolean) {
  const queryClient = makeClient();
  const wrapper = ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: queryClient }, children);

  return renderHook(
    () =>
      useMutation({
        meta,
        mutationFn: async () => {
          if (shouldFail) throw new Error('boom');
          return 'ok';
        },
      }),
    { wrapper },
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  // jsdom has no matchMedia; the floor reads it for reduced-motion dwell.
  vi.stubGlobal('matchMedia', vi.fn().mockReturnValue({ matches: false }));
});

describe('createMutationFloor onError', () => {
  test('toasts the meta.errorMessage when the call site did not opt out', async () => {
    const { result } = renderMutationWithMeta({ errorMessage: "Couldn't archive card" }, true);

    result.current.mutate();

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(mockToastError).toHaveBeenCalledWith("Couldn't archive card", expect.anything());
  });

  test('toasts a generic fallback when a failing mutation declares no message', async () => {
    const { result } = renderMutationWithMeta(undefined, true);

    result.current.mutate();

    await waitFor(() => expect(result.current.isError).toBe(true));
    // The point of the floor: silent failure is impossible — something surfaces.
    expect(mockToastError).toHaveBeenCalledWith('Something went wrong', expect.anything());
  });

  test('stays silent when the call site opted out with skipToast (it surfaces inline)', async () => {
    const { result } = renderMutationWithMeta({ skipToast: true }, true);

    result.current.mutate();

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(mockToastError).not.toHaveBeenCalled();
  });
});

describe('createMutationFloor onSuccess', () => {
  test('does not toast on success when no successMessage is declared (silent by default)', async () => {
    const { result } = renderMutationWithMeta({ errorMessage: 'unused' }, false);

    result.current.mutate();

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockToastSuccess).not.toHaveBeenCalled();
  });

  test('toasts a successMessage when one is declared (the rare opt-in)', async () => {
    const { result } = renderMutationWithMeta({ successMessage: 'Saved' }, false);

    result.current.mutate();

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(mockToastSuccess).toHaveBeenCalledWith('Saved', expect.anything());
  });
});
