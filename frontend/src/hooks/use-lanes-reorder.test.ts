import { describe, test, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, createElement } from 'react';
import { useLanesReorder } from './use-lanes-reorder';
import { queryKeys } from '@/lib/query-keys';
import type { BoardData, Lane } from '@/types';

vi.mock('@/lib/api', () => ({
  reorderLanes: vi.fn(),
}));

import { reorderLanes } from '@/lib/api';

const mockReorderLanes = vi.mocked(reorderLanes);

const BOARD_ID = 'board-1';
const LANE_A = 'lane-a';
const LANE_B = 'lane-b';
const LANE_C = 'lane-c';

function makeLane(id: string, name: string, position: number): Lane {
  return { id, boardId: BOARD_ID, name, position };
}

const serverLanes = [
  makeLane(LANE_A, 'Backlog', 0),
  makeLane(LANE_B, 'In Progress', 1),
  makeLane(LANE_C, 'Done', 2),
];

// The server returns the reordered non-archive lanes with new dense positions.
const reorderedBAC = [
  makeLane(LANE_B, 'In Progress', 0),
  makeLane(LANE_A, 'Backlog', 1),
  makeLane(LANE_C, 'Done', 2),
];

function setupClient(opts?: { lanesList?: Lane[]; boardData?: BoardData }) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  if (opts?.lanesList) {
    queryClient.setQueryData(queryKeys.lanes.all(BOARD_ID), opts.lanesList);
  }
  if (opts?.boardData) {
    queryClient.setQueryData(queryKeys.boards.data(BOARD_ID), opts.boardData);
  }
  return queryClient;
}

function renderReorder(queryClient: QueryClient, lanes: Lane[] = serverLanes) {
  const wrapper = ({ children }: { children: React.ReactNode }) =>
    createElement(QueryClientProvider, { client: queryClient }, children);
  return renderHook(() => useLanesReorder(BOARD_ID, lanes), { wrapper });
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe('useLanesReorder optimistic reorder', () => {
  test('exposes configured sensors for mouse and touch', () => {
    const queryClient = setupClient();
    const { result } = renderReorder(queryClient);
    // Mouse + touch sensor descriptors registered (the touch path is the point).
    expect(result.current.sensors).toHaveLength(2);
  });

  test('localLanes reflects the server order at rest, sorted by position', () => {
    const queryClient = setupClient();
    const { result } = renderReorder(queryClient, [
      makeLane(LANE_C, 'Done', 2),
      makeLane(LANE_A, 'Backlog', 0),
      makeLane(LANE_B, 'In Progress', 1),
    ]);

    expect(result.current.localLanes.map((l) => l.id)).toEqual([LANE_A, LANE_B, LANE_C]);
    expect(result.current.activeLaneId).toBeNull();
  });

  test('dragging a lane over another reorders localLanes optimistically before drop', () => {
    const queryClient = setupClient();
    const { result } = renderReorder(queryClient);

    act(() => {
      result.current.onDragStart({ active: { id: LANE_A } } as never);
      result.current.onDragOver({ active: { id: LANE_A }, over: { id: LANE_C } } as never);
    });

    // A moved to where C was → B, C, A.
    expect(result.current.localLanes.map((l) => l.id)).toEqual([LANE_B, LANE_C, LANE_A]);
    expect(result.current.activeLaneId).toBe(LANE_A);
    expect(mockReorderLanes).not.toHaveBeenCalled();
  });

  test('dropping a moved lane calls reorderLanes with the complete new order', async () => {
    mockReorderLanes.mockResolvedValue(reorderedBAC);
    const queryClient = setupClient();
    const { result } = renderReorder(queryClient);

    await act(async () => {
      result.current.onDragStart({ active: { id: LANE_A } } as never);
      result.current.onDragOver({ active: { id: LANE_A }, over: { id: LANE_B } } as never);
      result.current.onDragEnd({ active: { id: LANE_A }, over: { id: LANE_B } } as never);
    });

    await waitFor(() => {
      expect(mockReorderLanes).toHaveBeenCalledOnce();
    });
    expect(mockReorderLanes).toHaveBeenCalledWith(BOARD_ID, [LANE_B, LANE_A, LANE_C]);
    expect(result.current.activeLaneId).toBeNull();
  });

  test('a drop that lands the lane back in place is a no-op (no server call)', async () => {
    const queryClient = setupClient();
    const { result } = renderReorder(queryClient);

    await act(async () => {
      result.current.onDragStart({ active: { id: LANE_A } } as never);
      result.current.onDragEnd({ active: { id: LANE_A }, over: { id: LANE_A } } as never);
    });

    expect(mockReorderLanes).not.toHaveBeenCalled();
    expect(result.current.localLanes.map((l) => l.id)).toEqual([LANE_A, LANE_B, LANE_C]);
  });

  test('a drop with no drop target cancels the optimistic order without a server call', async () => {
    const queryClient = setupClient();
    const { result } = renderReorder(queryClient);

    await act(async () => {
      result.current.onDragStart({ active: { id: LANE_A } } as never);
      result.current.onDragOver({ active: { id: LANE_A }, over: { id: LANE_C } } as never);
      result.current.onDragEnd({ active: { id: LANE_A }, over: null } as never);
    });

    expect(mockReorderLanes).not.toHaveBeenCalled();
    expect(result.current.localLanes.map((l) => l.id)).toEqual([LANE_A, LANE_B, LANE_C]);
  });
});

describe('useLanesReorder cache reconciliation', () => {
  test('onSuccess merges new dense positions into BOTH the lanes-list and board-data caches', async () => {
    const lanesList = [...serverLanes];
    const boardData: BoardData = { lanes: [...serverLanes], cards: [], sizes: [] };
    const queryClient = setupClient({ lanesList, boardData });

    mockReorderLanes.mockResolvedValue(reorderedBAC);
    const { result } = renderReorder(queryClient);

    await act(async () => {
      result.current.onDragStart({ active: { id: LANE_A } } as never);
      result.current.onDragOver({ active: { id: LANE_A }, over: { id: LANE_B } } as never);
      result.current.onDragEnd({ active: { id: LANE_A }, over: { id: LANE_B } } as never);
    });

    await waitFor(() => {
      const list = queryClient.getQueryData<Lane[]>(queryKeys.lanes.all(BOARD_ID));
      expect(list?.map((l) => l.id)).toEqual([LANE_B, LANE_A, LANE_C]);
    });

    // The LanesTab list query is the surface the operator is looking at.
    const list = queryClient.getQueryData<Lane[]>(queryKeys.lanes.all(BOARD_ID));
    expect(list?.map((l) => l.position)).toEqual([0, 1, 2]);

    // The board page (board-data) must reconcile to the same order.
    const cached = queryClient.getQueryData<BoardData>(queryKeys.boards.data(BOARD_ID));
    expect(cached?.lanes.map((l) => l.id)).toEqual([LANE_B, LANE_A, LANE_C]);
    expect(cached?.lanes.map((l) => l.position)).toEqual([0, 1, 2]);
  });

  test('does not introduce or remove any lane in the board-data cache (e.g. an archive lane)', async () => {
    const archiveLane = makeLane('lane-archive', 'Archive', Number.MAX_SAFE_INTEGER);
    const boardData: BoardData = {
      lanes: [...serverLanes, archiveLane],
      cards: [],
      sizes: [],
    };
    const queryClient = setupClient({ boardData });

    // Server returns only the non-archive lanes (matches the real endpoint).
    mockReorderLanes.mockResolvedValue(reorderedBAC);
    const { result } = renderReorder(queryClient, serverLanes);

    await act(async () => {
      result.current.onDragStart({ active: { id: LANE_A } } as never);
      result.current.onDragEnd({ active: { id: LANE_A }, over: { id: LANE_B } } as never);
    });

    await waitFor(() => {
      expect(mockReorderLanes).toHaveBeenCalledOnce();
    });

    const cached = queryClient.getQueryData<BoardData>(queryKeys.boards.data(BOARD_ID));
    const archived = cached?.lanes.find((l) => l.id === 'lane-archive');
    expect(archived?.position).toBe(Number.MAX_SAFE_INTEGER);
    expect(cached?.lanes).toHaveLength(4);
  });

  test('a failed reorder invalidates the lanes list and board data so the optimistic order rolls back', async () => {
    const lanesList = [...serverLanes];
    const boardData: BoardData = { lanes: [...serverLanes], cards: [], sizes: [] };
    const queryClient = setupClient({ lanesList, boardData });
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    mockReorderLanes.mockRejectedValue(new Error('boom'));
    const { result } = renderReorder(queryClient);

    await act(async () => {
      result.current.onDragStart({ active: { id: LANE_A } } as never);
      result.current.onDragEnd({ active: { id: LANE_A }, over: { id: LANE_B } } as never);
    });

    await waitFor(() => {
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: queryKeys.lanes.all(BOARD_ID) });
    });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: queryKeys.boards.data(BOARD_ID) });
  });
});
