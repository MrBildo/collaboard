import { describe, test, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement } from 'react';
import { useCurrentUser } from './use-current-user';

// Mock the API module
vi.mock('@/lib/api', () => ({
  fetchMe: vi.fn(),
  fetchUsers: vi.fn(),
}));

import { fetchMe, fetchUsers } from '@/lib/api';

const mockFetchMe = vi.mocked(fetchMe);
const mockFetchUsers = vi.mocked(fetchUsers);

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return createElement(QueryClientProvider, { client: queryClient }, children);
  };
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe('useCurrentUser', () => {
  test('returns undefined values when not logged in', () => {
    const { result } = renderHook(() => useCurrentUser(false), { wrapper: createWrapper() });
    expect(result.current.currentUserId).toBeUndefined();
    expect(result.current.currentUserRole).toBeUndefined();
    expect(result.current.isAdmin).toBe(false);
  });

  test('returns user data when logged in and queries resolve', async () => {
    mockFetchMe.mockResolvedValue({
      id: 'user-1',
      name: 'Test User',
      role: 'HumanUser',
      authKey: 'key-123',
    });
    mockFetchUsers.mockResolvedValue([]);

    const { result } = renderHook(() => useCurrentUser(true), { wrapper: createWrapper() });

    await waitFor(() => {
      expect(result.current.currentUserId).toBe('user-1');
    });

    expect(result.current.currentUserRole).toBe('HumanUser');
    expect(result.current.isAdmin).toBe(true);
  });

  test('isAdmin is false when fetchUsers rejects (non-admin)', async () => {
    mockFetchMe.mockResolvedValue({
      id: 'user-2',
      name: 'Agent',
      role: 'AgentUser',
      authKey: 'key-456',
    });
    mockFetchUsers.mockRejectedValue(new Error('403'));

    const { result } = renderHook(() => useCurrentUser(true), { wrapper: createWrapper() });

    await waitFor(() => {
      expect(result.current.currentUserId).toBe('user-2');
    });

    expect(result.current.isAdmin).toBe(false);
  });
});
