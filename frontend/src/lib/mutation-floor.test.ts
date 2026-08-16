import { describe, test, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider, useMutation } from '@tanstack/react-query';
import { createElement, type ReactNode } from 'react';
import { AxiosError, AxiosHeaders } from 'axios';
import { createMutationFloor, toMessage } from './mutation-floor';

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

// toMessage feeds the inline-error surfaces (the webhook form, card comments,
// attachments, prune, etc.). The contract that matters: an axios error shows the
// server's actionable body, NOT axios's generic status-code string — that
// generic string is what buried the webhook SSRF diagnosis.
describe('toMessage', () => {
  function axiosErrorWith(body: unknown): AxiosError {
    const response = {
      data: body,
      status: 400,
      statusText: 'Bad Request',
      headers: {},
      config: { headers: new AxiosHeaders() },
    };
    return new AxiosError(
      'Request failed with status code 400',
      'ERR_BAD_REQUEST',
      undefined,
      undefined,
      response as AxiosError['response'],
    );
  }

  test('returns the bare-string 400 body (Results.BadRequest(msg)), not the axios message', () => {
    const serverMessage = "Webhook host 'x' resolves to a private or otherwise blocked address.";
    expect(toMessage(axiosErrorWith(serverMessage))).toBe(serverMessage);
  });

  test('returns ProblemDetails.detail over the generic axios message', () => {
    expect(
      toMessage(axiosErrorWith({ title: 'Bad Request', detail: 'A payload URL is required.' })),
    ).toBe('A payload URL is required.');
  });

  test('returns a { message } body', () => {
    expect(toMessage(axiosErrorWith({ message: 'Events must be non-empty.' }))).toBe(
      'Events must be non-empty.',
    );
  });

  test('falls back to the generic axios message for an HTML error-page body', () => {
    expect(toMessage(axiosErrorWith('<!DOCTYPE html><title>500</title>'))).toBe(
      'Request failed with status code 400',
    );
  });

  test('falls back to the generic axios message when there is no response body', () => {
    expect(toMessage(new AxiosError('Network Error', 'ERR_NETWORK'))).toBe('Network Error');
  });

  test('handles a plain Error (non-axios)', () => {
    expect(toMessage(new Error('boom'))).toBe('boom');
  });

  test('stringifies a non-Error throw', () => {
    expect(toMessage('weird')).toBe('weird');
  });
});
