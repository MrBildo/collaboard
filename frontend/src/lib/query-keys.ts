export const queryKeys = {
  boards: {
    all: () => ['boards'] as const,
    detail: (slug: string) => ['boards', slug] as const,
    data: (boardId: string) => ['boards', boardId, 'data'] as const,
    cards: (boardId: string) => ['boards', boardId, 'cards'] as const,
    archivedCard: (boardId: string, cardNum: number) =>
      ['boards', boardId, 'cards', 'archived', cardNum] as const,
  },
  cards: {
    labels: (cardId: string) => ['cards', cardId, 'labels'] as const,
    comments: (cardId: string) => ['cards', cardId, 'comments'] as const,
    attachments: (cardId: string) => ['cards', cardId, 'attachments'] as const,
  },
  lanes: {
    all: (boardId: string) => ['lanes', boardId] as const,
  },
  sizes: {
    all: (boardId: string) => ['sizes', boardId] as const,
  },
  labels: {
    all: (boardId: string) => ['labels', boardId] as const,
  },
  users: {
    me: () => ['users', 'me'] as const,
    adminCheck: () => ['users', 'adminCheck'] as const,
    directory: () => ['users', 'directory'] as const,
    all: () => ['users'] as const,
  },
  search: {
    cards: (q: string, boardId?: string, archiveBoardId?: string) =>
      ['search', 'cards', q, boardId, archiveBoardId] as const,
  },
  version: () => ['version'] as const,
  versionStatus: () => ['version', 'status'] as const,
} as const;
