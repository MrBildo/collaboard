import { describe, test, expect, beforeEach, afterEach, vi } from 'vitest';

// runtime-config.ts caches the resolved config in a module-level variable. To
// exercise the cache-miss path independently per test, the module is re-imported
// with a fresh registry after vi.resetModules(). The cache-hit test deliberately
// calls the same module instance twice.
async function importFresh() {
  vi.resetModules();
  return import('./runtime-config');
}

function jsonResponse(body: unknown, ok = true, status = 200): Response {
  return {
    ok,
    status,
    json: async () => body,
  } as unknown as Response;
}

beforeEach(() => {
  vi.restoreAllMocks();
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe('fetchRuntimeConfig', () => {
  test('returns the parsed config when /config.json is valid', async () => {
    const fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValue(jsonResponse({ apiBaseUrl: 'https://api.example.com/api/v1' }));
    const { fetchRuntimeConfig, getApiBaseUrl } = await importFresh();

    const config = await fetchRuntimeConfig();

    expect(config).toEqual({ apiBaseUrl: 'https://api.example.com/api/v1' });
    expect(getApiBaseUrl()).toBe('https://api.example.com/api/v1');
    expect(fetchSpy).toHaveBeenCalledWith('/config.json', { cache: 'no-cache' });
  });

  test('falls back to the relative base URL on a non-2xx response', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(null, false, 404));
    const { fetchRuntimeConfig, getApiBaseUrl } = await importFresh();

    const config = await fetchRuntimeConfig();

    expect(config).toEqual({ apiBaseUrl: '/api/v1' });
    expect(getApiBaseUrl()).toBe('/api/v1');
  });

  test('falls back to the relative base URL when fetch rejects (network error)', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('Failed to fetch'));
    vi.spyOn(console, 'warn').mockImplementation(() => {});
    const { fetchRuntimeConfig } = await importFresh();

    const config = await fetchRuntimeConfig();

    expect(config).toEqual({ apiBaseUrl: '/api/v1' });
  });

  test('falls back to the relative base URL when JSON parsing rejects', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => {
        throw new SyntaxError('Unexpected token < in JSON');
      },
    } as unknown as Response);
    vi.spyOn(console, 'warn').mockImplementation(() => {});
    const { fetchRuntimeConfig } = await importFresh();

    const config = await fetchRuntimeConfig();

    expect(config).toEqual({ apiBaseUrl: '/api/v1' });
  });

  test('falls back to the relative base URL on schema validation failure', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse({ wrongKey: 123 }));
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {});
    const { fetchRuntimeConfig } = await importFresh();

    const config = await fetchRuntimeConfig();

    expect(config).toEqual({ apiBaseUrl: '/api/v1' });
    expect(warnSpy).toHaveBeenCalled();
  });

  test('returns the cached value on the second call without re-fetching', async () => {
    const fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValue(jsonResponse({ apiBaseUrl: 'https://api.example.com/api/v1' }));
    const { fetchRuntimeConfig } = await importFresh();

    const first = await fetchRuntimeConfig();
    const second = await fetchRuntimeConfig();

    expect(first).toEqual(second);
    expect(fetchSpy).toHaveBeenCalledTimes(1);
  });
});
