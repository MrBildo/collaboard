import { MutationCache, type Mutation } from '@tanstack/react-query';
import { toast } from 'sonner';

// The global mutation-error floor (card #203, spec §5). MutationCache.onError
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
// populates it today (successes in Collaboard are self-evident — see spec §3);
// the plumbing exists so a future "did that work?" gap is a one-line meta add.

// The typed `meta` shape (spec §5). Module augmentation keeps it off `any`.
declare module '@tanstack/react-query' {
  interface Register {
    mutationMeta: {
      errorMessage?: string;
      successMessage?: string;
      skipToast?: boolean;
    };
  }
}

function toMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
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
      // per-call console.error site (spec §5 Rule 1).
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
