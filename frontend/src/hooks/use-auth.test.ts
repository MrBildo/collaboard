import { describe, test, expect, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';
import { useAuth } from './use-auth';
import { clearUserKey, isLoggedIn, setUserKey } from '@/lib/auth';

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return createElement(QueryClientProvider, { client: queryClient }, children);
  };
}

beforeEach(() => {
  clearUserKey();
});

describe('useAuth', () => {
  test('initial state matches isLoggedIn() when no key is stored', () => {
    const { result } = renderHook(() => useAuth(), { wrapper: createWrapper() });
    expect(result.current.loggedIn).toBe(false);
    expect(isLoggedIn()).toBe(false);
  });

  test('initial state matches isLoggedIn() when a key is stored', () => {
    setUserKey('test-key-123');
    const { result } = renderHook(() => useAuth(), { wrapper: createWrapper() });
    expect(result.current.loggedIn).toBe(true);
    expect(isLoggedIn()).toBe(true);
  });

  test('handleLogin sets user key and updates loggedIn to true', () => {
    const { result } = renderHook(() => useAuth(), { wrapper: createWrapper() });
    expect(result.current.loggedIn).toBe(false);

    act(() => {
      result.current.handleLogin('my-auth-key');
    });

    expect(result.current.loggedIn).toBe(true);
    expect(isLoggedIn()).toBe(true);
  });

  test('handleLogout clears user key and updates loggedIn to false', () => {
    setUserKey('existing-key');
    const { result } = renderHook(() => useAuth(), { wrapper: createWrapper() });
    expect(result.current.loggedIn).toBe(true);

    act(() => {
      result.current.handleLogout();
    });

    expect(result.current.loggedIn).toBe(false);
    expect(isLoggedIn()).toBe(false);
  });

  test('handleLogin is referentially stable across renders', () => {
    const { result, rerender } = renderHook(() => useAuth(), { wrapper: createWrapper() });
    const first = result.current.handleLogin;
    rerender();
    expect(result.current.handleLogin).toBe(first);
  });
});
