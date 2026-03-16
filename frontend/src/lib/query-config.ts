export const QUERY_DEFAULTS = {
  boardData: { staleTime: 30_000, retry: 2, refetchOnWindowFocus: false },
  userDirectory: { staleTime: 60_000, retry: 1 },
  labels: { staleTime: 60_000, retry: 1 },
  comments: { staleTime: 30_000, retry: 1 },
  attachments: { staleTime: 30_000, retry: 1 },
  boards: { staleTime: 30_000, retry: 2 },
} as const;
