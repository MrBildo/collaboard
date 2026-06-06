import { describe, test, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import type { ReactNode } from 'react';
import { App } from './App';

// useAuth is the route-level gate. Mock it so each test drives the logged-in flag
// directly rather than through localStorage + the auth library.
const mockUseAuth = vi.fn();
vi.mock('@/hooks/use-auth', () => ({
  useAuth: () => mockUseAuth(),
}));

// useBoardData is stubbed to empty lanes (per the card) so the logged-in path
// renders board chrome without exercising the board-fetch query chain.
vi.mock('@/hooks/use-board-data', () => ({
  useBoardData: () => ({
    board: { id: 'board-1', name: 'Test Board', slug: 'test' },
    boardId: 'board-1',
    boardMetaQuery: { isLoading: false, isError: false },
    boardDataQuery: { isLoading: false, isError: false },
    lanes: [],
    sizes: [],
    sizeMap: new Map(),
    serverCards: [],
    enrichedCardMap: new Map(),
  }),
}));

// useBoardEvents opens a real EventSource (absent in jsdom). The gate test does
// not exercise SSE — that surface is covered by use-board-events.test.ts.
vi.mock('@/hooks/use-board-events', () => ({
  useBoardEvents: () => {},
}));

// API layer: the board-list and version queries are enabled once logged in.
// Resolve them to benign values so nothing hits the network.
vi.mock('@/lib/api', () => ({
  fetchBoards: vi.fn().mockResolvedValue([]),
  fetchCards: vi.fn().mockResolvedValue({ items: [] }),
  fetchVersion: vi.fn().mockResolvedValue({ version: '1.0.0' }),
  fetchMe: vi.fn().mockResolvedValue({ id: 'user-1', name: 'Test User', role: 1 }),
  fetchUsers: vi.fn().mockResolvedValue([]),
}));

function renderApp() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={['/boards/test']}>
          <Routes>
            <Route path="/boards/:slug" element={children} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>
    );
  }
  return render(<App />, { wrapper: Wrapper });
}

beforeEach(() => {
  vi.clearAllMocks();
  mockUseAuth.mockReturnValue({
    loggedIn: false,
    handleLogin: vi.fn(),
    handleLogout: vi.fn(),
  });
});

describe('App route-level login gate', () => {
  test('logged out renders the login screen and no board chrome', () => {
    mockUseAuth.mockReturnValue({
      loggedIn: false,
      handleLogin: vi.fn(),
      handleLogout: vi.fn(),
    });

    renderApp();

    expect(screen.getByText(/enter your auth key to continue/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /log in/i })).toBeInTheDocument();
    // Board chrome must be absent: no kanban region, no new-card action.
    expect(screen.queryByRole('region', { name: /kanban board/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /new card/i })).not.toBeInTheDocument();
  });

  test('logged in renders board chrome and not the login screen', () => {
    mockUseAuth.mockReturnValue({
      loggedIn: true,
      handleLogin: vi.fn(),
      handleLogout: vi.fn(),
    });

    renderApp();

    expect(screen.getByRole('region', { name: /kanban board/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /new card/i })).toBeInTheDocument();
    expect(screen.queryByText(/enter your auth key to continue/i)).not.toBeInTheDocument();
  });
});
