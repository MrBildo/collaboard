import { describe, test, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, createElement } from 'react';
import { useSizesReorder } from './use-sizes-reorder';
import { queryKeys } from '@/lib/query-keys';
import type { BoardData, CardSize } from '@/types';

vi.mock('@/lib/api', () => ({
  reorderSizes: vi.fn(),
}));

import { reorderSizes } from '@/lib/api';

const mockReorderSizes = vi.mocked(reorderSizes);

const BOARD_ID = 'board-1';
const SIZE_S = 'size-s';
const SIZE_M = 'size-m';
const SIZE_L = 'size-l';

function makeSize(id: string, name: string, ordinal: number): CardSize {
  return { id, boardId: BOARD_ID, name, ordinal };
}

const serverSizes = [makeSize(SIZE_S, 'S', 0), makeSize(SIZE_M, 'M', 1), makeSize(SIZE_L, 'L', 2)];

// The server returns the full board size set with new dense ordinals.
const reorderedMSL = [makeSize(SIZE_M, 'M', 0), makeSize(SIZE_S, 'S', 1), makeSize(SIZE_L, 'L', 2)];

function setupClient(opts?: { sizesList?: CardSize[]; boardData?: BoardData }) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  if (opts?.sizesList) {
    queryClient.setQueryData(queryKeys.sizes.all(BOARD_ID), opts.sizesList);
  }
  if (opts?.boardData) {
    queryClient.setQueryData(queryKeys.boards.data(BOARD_ID), opts.boardData);
  }
  return queryClient;
}

function renderReorder(queryClient: QueryClient, sizes: CardSize[] = serverSizes) {
  const wrapper = ({ children }: { children: React.ReactNode }) =>
    createElement(QueryClientProvider, { client: queryClient }, children);
  return renderHook(() => useSizesReorder(BOARD_ID, sizes), { wrapper });
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe('useSizesReorder optimistic reorder', () => {
  test('exposes configured sensors for mouse and touch', () => {
    const queryClient = setupClient();
    const { result } = renderReorder(queryClient);
    // Mouse + touch sensor descriptors registered (the touch path is the point).
    expect(result.current.sensors).toHaveLength(2);
  });

  test('localSizes reflects the server order at rest, sorted by ordinal', () => {
    const queryClient = setupClient();
    const { result } = renderReorder(queryClient, [
      makeSize(SIZE_L, 'L', 2),
      makeSize(SIZE_S, 'S', 0),
      makeSize(SIZE_M, 'M', 1),
    ]);

    expect(result.current.localSizes.map((s) => s.id)).toEqual([SIZE_S, SIZE_M, SIZE_L]);
    expect(result.current.activeSizeId).toBeNull();
  });

  test('dragging a size over another reorders localSizes optimistically before drop', () => {
    const queryClient = setupClient();
    const { result } = renderReorder(queryClient);

    act(() => {
      result.current.onDragStart({ active: { id: SIZE_S } } as never);
      result.current.onDragOver({ active: { id: SIZE_S }, over: { id: SIZE_L } } as never);
    });

    // S moved to where L was → M, L, S.
    expect(result.current.localSizes.map((s) => s.id)).toEqual([SIZE_M, SIZE_L, SIZE_S]);
    expect(result.current.activeSizeId).toBe(SIZE_S);
    expect(mockReorderSizes).not.toHaveBeenCalled();
  });

  test('dropping a moved size calls reorderSizes with the complete new order', async () => {
    mockReorderSizes.mockResolvedValue(reorderedMSL);
    const queryClient = setupClient();
    const { result } = renderReorder(queryClient);

    await act(async () => {
      result.current.onDragStart({ active: { id: SIZE_S } } as never);
      result.current.onDragOver({ active: { id: SIZE_S }, over: { id: SIZE_M } } as never);
      result.current.onDragEnd({ active: { id: SIZE_S }, over: { id: SIZE_M } } as never);
    });

    await waitFor(() => {
      expect(mockReorderSizes).toHaveBeenCalledOnce();
    });
    expect(mockReorderSizes).toHaveBeenCalledWith(BOARD_ID, [SIZE_M, SIZE_S, SIZE_L]);
    expect(result.current.activeSizeId).toBeNull();
  });

  test('a drop that lands the size back in place is a no-op (no server call)', async () => {
    const queryClient = setupClient();
    const { result } = renderReorder(queryClient);

    await act(async () => {
      result.current.onDragStart({ active: { id: SIZE_S } } as never);
      result.current.onDragEnd({ active: { id: SIZE_S }, over: { id: SIZE_S } } as never);
    });

    expect(mockReorderSizes).not.toHaveBeenCalled();
    expect(result.current.localSizes.map((s) => s.id)).toEqual([SIZE_S, SIZE_M, SIZE_L]);
  });

  test('a drop with no drop target cancels the optimistic order without a server call', async () => {
    const queryClient = setupClient();
    const { result } = renderReorder(queryClient);

    await act(async () => {
      result.current.onDragStart({ active: { id: SIZE_S } } as never);
      result.current.onDragOver({ active: { id: SIZE_S }, over: { id: SIZE_L } } as never);
      result.current.onDragEnd({ active: { id: SIZE_S }, over: null } as never);
    });

    expect(mockReorderSizes).not.toHaveBeenCalled();
    expect(result.current.localSizes.map((s) => s.id)).toEqual([SIZE_S, SIZE_M, SIZE_L]);
  });
});

describe('useSizesReorder cache reconciliation', () => {
  test('onSuccess merges new dense ordinals into BOTH the sizes-list and board-data caches', async () => {
    const sizesList = [...serverSizes];
    const boardData: BoardData = { lanes: [], cards: [], sizes: [...serverSizes] };
    const queryClient = setupClient({ sizesList, boardData });

    mockReorderSizes.mockResolvedValue(reorderedMSL);
    const { result } = renderReorder(queryClient);

    await act(async () => {
      result.current.onDragStart({ active: { id: SIZE_S } } as never);
      result.current.onDragOver({ active: { id: SIZE_S }, over: { id: SIZE_M } } as never);
      result.current.onDragEnd({ active: { id: SIZE_S }, over: { id: SIZE_M } } as never);
    });

    await waitFor(() => {
      const list = queryClient.getQueryData<CardSize[]>(queryKeys.sizes.all(BOARD_ID));
      expect(list?.map((s) => s.id)).toEqual([SIZE_M, SIZE_S, SIZE_L]);
    });

    // The SizesTab list query is the surface the operator is looking at.
    const list = queryClient.getQueryData<CardSize[]>(queryKeys.sizes.all(BOARD_ID));
    expect(list?.map((s) => s.ordinal)).toEqual([0, 1, 2]);

    // The board page (board-data) must reconcile to the same order.
    const cached = queryClient.getQueryData<BoardData>(queryKeys.boards.data(BOARD_ID));
    expect(cached?.sizes.map((s) => s.id)).toEqual([SIZE_M, SIZE_S, SIZE_L]);
    expect(cached?.sizes.map((s) => s.ordinal)).toEqual([0, 1, 2]);
  });

  test('does not touch lanes or cards in the board-data cache', async () => {
    const lane = { id: 'lane-1', boardId: BOARD_ID, name: 'Backlog', position: 0 };
    const boardData: BoardData = {
      lanes: [lane],
      cards: [],
      sizes: [...serverSizes],
    };
    const queryClient = setupClient({ boardData });

    mockReorderSizes.mockResolvedValue(reorderedMSL);
    const { result } = renderReorder(queryClient, serverSizes);

    await act(async () => {
      result.current.onDragStart({ active: { id: SIZE_S } } as never);
      result.current.onDragEnd({ active: { id: SIZE_S }, over: { id: SIZE_M } } as never);
    });

    await waitFor(() => {
      expect(mockReorderSizes).toHaveBeenCalledOnce();
    });

    const cached = queryClient.getQueryData<BoardData>(queryKeys.boards.data(BOARD_ID));
    expect(cached?.lanes).toEqual([lane]);
    expect(cached?.cards).toEqual([]);
    expect(cached?.sizes).toHaveLength(3);
  });

  test('a failed reorder invalidates the sizes list and board data so the optimistic order rolls back', async () => {
    const sizesList = [...serverSizes];
    const boardData: BoardData = { lanes: [], cards: [], sizes: [...serverSizes] };
    const queryClient = setupClient({ sizesList, boardData });
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    mockReorderSizes.mockRejectedValue(new Error('boom'));
    const { result } = renderReorder(queryClient);

    await act(async () => {
      result.current.onDragStart({ active: { id: SIZE_S } } as never);
      result.current.onDragEnd({ active: { id: SIZE_S }, over: { id: SIZE_M } } as never);
    });

    await waitFor(() => {
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: queryKeys.sizes.all(BOARD_ID) });
    });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: queryKeys.boards.data(BOARD_ID) });
  });
});
