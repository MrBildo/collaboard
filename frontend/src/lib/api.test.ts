import { describe, test, expect, vi } from 'vitest';
import type { InternalAxiosRequestConfig } from 'axios';

// The interceptor reads getApiBaseUrl() lazily per request. Mock the resolver
// so the assertion does not depend on a real /config.json fetch.
vi.mock('@/lib/runtime-config', () => ({
  getApiBaseUrl: () => 'https://api.example.com/api/v1',
}));

import { api } from './api';

describe('api request interceptor', () => {
  test('populates config.baseURL from getApiBaseUrl() per request', async () => {
    const handlers = api.interceptors.request as unknown as {
      handlers: { fulfilled: (c: InternalAxiosRequestConfig) => InternalAxiosRequestConfig }[];
    };
    const requestInterceptor = handlers.handlers[0].fulfilled;

    const config = {
      headers: {},
    } as unknown as InternalAxiosRequestConfig;

    const result = requestInterceptor(config);

    expect(result.baseURL).toBe('https://api.example.com/api/v1');
  });
});
