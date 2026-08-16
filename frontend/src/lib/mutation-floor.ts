import axios from 'axios';
import { MutationCache, type Mutation } from '@tanstack/react-query';
import { toast } from 'sonner';

// The global mutation-error floor. MutationCache.onError
// fires once per failed mutation, reads the call-site `meta`, and shows a
// generic toast unless the call site opted out (`skipToast: true`). This is the
// floor that makes silent mutation failure structurally impossible — every
// mutation either lets the floor toast it or handles its own surface inline.
//
// The per-mutation `console.error` sites this replaces are deleted at their
// call sites: the floor logs centrally here, so a mutation `onError` never
// carries `console.error` as its only operator-facing behavior again.
//
// onSuccess reads `meta.successMessage` for the rare success toast. Nothing
// populates it today (successes in this app are self-evident from the UI);
// the plumbing exists so a future "did that work?" gap is a one-line meta add.

// The typed `meta` shape. Module augmentation keeps it off `any`.
declare module '@tanstack/react-query' {
  interface Register {
    mutationMeta: {
      errorMessage?: string;
      successMessage?: string;
      skipToast?: boolean;
    };
  }
}

// Surface the most actionable message we have for a failed mutation. For an
// axios error the API's validation 400s carry a plain-string body that names
// exactly what to fix (e.g. the webhook SSRF rejection: "...resolves to a
// private or otherwise blocked address (192.168.50.135); set
// Webhooks:AllowPrivateNetworkTargets to allow it."), whereas axios's own
// error.message is the generic "Request failed with status code 400" that
// buries it. Prefer the server's body; fall back to the generic message only
// when there is no usable body (network error, empty/HTML 500, etc.).
function toMessage(error: unknown): string {
  if (axios.isAxiosError(error)) {
    const serverMessage = serverMessageFrom(error.response?.data);
    if (serverMessage) return serverMessage;
  }
  return error instanceof Error ? error.message : String(error);
}

// Pull a human-readable message out of a response body. The API returns the
// message as a bare JSON string on its validation 400s (Results.BadRequest(msg));
// other framework responses use ProblemDetails ({ detail, title }) or a
// { message } object. An HTML body (a proxy or unhandled-500 error page) is not
// an actionable message — skip it so the generic fallback wins.
function serverMessageFrom(data: unknown): string | null {
  if (typeof data === 'string') {
    const trimmed = data.trim();
    return trimmed.length > 0 && !trimmed.startsWith('<') ? trimmed : null;
  }
  if (data !== null && typeof data === 'object') {
    const body = data as Record<string, unknown>;
    for (const key of ['detail', 'message', 'title'] as const) {
      const value = body[key];
      if (typeof value === 'string' && value.trim().length > 0) return value.trim();
    }
  }
  return null;
}

function prefersReducedMotion(): boolean {
  return (
    typeof window !== 'undefined' && window.matchMedia('(prefers-reduced-motion: reduce)').matches
  );
}

type MutationLike = Mutation<unknown, unknown, unknown, unknown>;

export function createMutationFloor(): MutationCache {
  return new MutationCache({
    onError: (error, _variables, _context, mutation: MutationLike) => {
      const meta = mutation.options.meta;

      // Central developer log — the floor does this once, replacing every
      // per-call console.error site.
      console.error('[mutation]', meta?.errorMessage ?? 'mutation failed', error);

      // The call site handles its own surface inline (skipToast: true) — the
      // floor stays quiet so the operator isn't told twice.
      if (meta?.skipToast === true) return;

      // Hold the toast longer under reduced-motion so the operator has time to
      // read without relying on the enter/exit animation to draw the eye.
      toast.error(meta?.errorMessage ?? 'Something went wrong', {
        duration: prefersReducedMotion() ? 6000 : 4000,
      });
    },
    onSuccess: (_data, _variables, _context, mutation: MutationLike) => {
      const meta = mutation.options.meta;
      if (!meta?.successMessage) return;

      toast.success(meta.successMessage, {
        duration: prefersReducedMotion() ? 5000 : 3000,
      });
    },
  });
}

export { toMessage };
