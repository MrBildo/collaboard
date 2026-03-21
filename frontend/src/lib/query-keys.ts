export const queryKeys = {
  boards: {
    all: () => ['boards'] as const,
    detail: (slug: string) => ['boards', slug] as const,
    data: (boardId: string) => ['boards', boardId, 'data'] as const,
    cards: (boardId: string) => ['boards', boardId, 'cards'] as const,
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
    cards: (q: string, archiveBoardId?: string) => ['search', 'cards', q, archiveBoardId] as const,
  },
  version: () => ['version'] as const,
} as const;
