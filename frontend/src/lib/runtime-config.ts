import { runtimeConfigSchema } from '@/lib/schemas';
import type { RuntimeConfig } from '@/types';

const FALLBACK_API_BASE_URL = '/api/v1';

let resolved: RuntimeConfig | null = null;

/**
 * Fetch and validate /config.json from the Portal's own origin.
 *
 * Called once at app boot in main.tsx, before ReactDOM.createRoot(...).render(...).
 * On 404, network error, JSON parse error, or schema validation failure, falls back
 * to a same-origin relative base URL — this preserves the single-process LAN release,
 * where the Portal and API are the same process at the same origin.
 *
 * Safe to call multiple times (idempotent): subsequent calls return the cached
 * resolved value without re-fetching.
 *
 * Note on timeouts: no client-side timeout / AbortController is set on this fetch.
 * This is a deliberate choice, not an oversight. /config.json is fetched from the
 * Portal's own origin — the same host that just served index.html. A hung fetch
 * implies the Portal-serving host is in a state that no client-side timeout fixes:
 * falling back to the relative /api/v1 would point at the same host that is already
 * hung, producing a Portal that fails on its first API request instead of its first
 * config fetch. The loading shell in index.html is the operator-facing signal that
 * boot has not completed; the Network tab shows a pending /config.json for anyone
 * who opens DevTools. Revisit only if empirical hung-but-eventually-responsive host
 * deployments emerge (adding an AbortController would be a future-additive change).
 */
export async function fetchRuntimeConfig(): Promise<RuntimeConfig> {
  if (resolved) return resolved;

  try {
    const res = await fetch('/config.json', { cache: 'no-cache' });
    if (!res.ok) {
      resolved = { apiBaseUrl: FALLBACK_API_BASE_URL };
      return resolved;
    }

    const json: unknown = await res.json();
    const parseResult = runtimeConfigSchema.safeParse(json);
    if (!parseResult.success) {
      console.warn(
        '[runtime-config] /config.json was present but invalid; falling back to relative base URL.',
        parseResult.error.issues,
      );
      resolved = { apiBaseUrl: FALLBACK_API_BASE_URL };
      return resolved;
    }

    resolved = parseResult.data;
    return resolved;
  } catch (err) {
    console.warn(
      '[runtime-config] failed to fetch /config.json; falling back to relative base URL.',
      err,
    );
    resolved = { apiBaseUrl: FALLBACK_API_BASE_URL };
    return resolved;
  }
}

/**
 * Synchronous accessor for the resolved API base URL.
 *
 * MUST be called only after fetchRuntimeConfig() has resolved (the boot sequence
 * in main.tsx awaits it before rendering the React tree).
 *
 * Defensive: returns the same-origin fallback if `resolved` is still null.
 */
export function getApiBaseUrl(): string {
  return resolved?.apiBaseUrl ?? FALLBACK_API_BASE_URL;
}
