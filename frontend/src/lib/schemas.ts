import { z } from 'zod';

export const boardSchema = z.object({
  id: z.string(),
  name: z.string(),
  slug: z.string(),
  createdAtUtc: z.string(),
});

export const laneSchema = z.object({
  id: z.string(),
  boardId: z.string(),
  name: z.string(),
  position: z.number(),
});

export const cardSizeSchema = z.object({
  id: z.string(),
  boardId: z.string(),
  name: z.string(),
  ordinal: z.number(),
});

export const cardItemSchema = z.object({
  id: z.string(),
  number: z.number(),
  name: z.string(),
  descriptionMarkdown: z.string(),
  laneId: z.string(),
  position: z.number(),
  sizeId: z.string(),
  isArchived: z.boolean().optional().default(false),
  createdByUserId: z.string(),
  createdAtUtc: z.string(),
  lastUpdatedByUserId: z.string(),
  lastUpdatedAtUtc: z.string(),
});

export const labelSchema = z.object({
  id: z.string(),
  boardId: z.string(),
  name: z.string(),
  color: z.string().nullable().optional(),
});

export const cardCommentSchema = z.object({
  id: z.string(),
  cardId: z.string(),
  userId: z.string(),
  contentMarkdown: z.string(),
  lastUpdatedAtUtc: z.string(),
});

export const boardUserSchema = z.object({
  id: z.string(),
  name: z.string(),
  role: z.number(),
  authKey: z.string(),
  isActive: z.boolean(),
});

export const attachmentMetaSchema = z.object({
  id: z.string(),
  fileName: z.string(),
  contentType: z.string(),
  fileSize: z.number(),
  addedByUserId: z.string(),
  addedAtUtc: z.string(),
});

export const cardLabelSummarySchema = z.object({
  id: z.string(),
  name: z.string(),
  color: z.string().nullable().optional(),
});

export const cardSummarySchema = z.object({
  id: z.string(),
  number: z.number(),
  name: z.string(),
  descriptionMarkdown: z.string(),
  sizeId: z.string(),
  sizeName: z.string(),
  laneId: z.string(),
  position: z.number(),
  isArchived: z.boolean(),
  createdByUserId: z.string(),
  createdAtUtc: z.string(),
  lastUpdatedByUserId: z.string(),
  lastUpdatedAtUtc: z.string(),
  labels: z.array(cardLabelSummarySchema),
  commentCount: z.number(),
  attachmentCount: z.number(),
});

export const boardDataSchema = z.object({
  lanes: z.array(laneSchema),
  cards: z.array(cardSummarySchema),
  sizes: z.array(cardSizeSchema),
});

export const pagedCardSummarySchema = z.object({
  items: z.array(cardSummarySchema),
  totalCount: z.number(),
  offset: z.number(),
  limit: z.number().nullable(),
});

export const userDirectoryEntrySchema = z.object({
  id: z.string(),
  name: z.string(),
});

export const authMeSchema = z.object({
  id: z.string(),
  name: z.string(),
  role: z.number(),
});

export const versionSchema = z.object({
  version: z.string(),
});

// #303: current-vs-latest update status from GET /version/status. latest/releaseUrl are null
// until the backend's first successful poll (offline/air-gap/disabled stays null forever, the
// honest degraded state). lastChecked is the timestamp of the last successful poll.
export const versionStatusSchema = z.object({
  current: z.string(),
  latest: z.string().nullable(),
  updateAvailable: z.boolean(),
  releaseUrl: z.string().nullable(),
  lastChecked: z.string().nullable(),
});

export const reorderResponseSchema = z.object({
  lanes: z.array(laneSchema),
  cards: z.array(cardItemSchema),
});

export const uploadAttachmentResponseSchema = z.object({
  id: z.string(),
  fileName: z.string(),
});

export const prunePreviewCardSchema = z.object({
  id: z.string(),
  number: z.number(),
  name: z.string(),
  laneName: z.string(),
  lastUpdatedAtUtc: z.string(),
});

export const prunePreviewResponseSchema = z.object({
  matchCount: z.number(),
  cards: z.array(prunePreviewCardSchema),
});

export const pruneResponseSchema = z.object({
  deletedCount: z.number().optional(),
  archivedCount: z.number().optional(),
});

export const searchResultSchema = z.object({
  boardId: z.string(),
  boardName: z.string(),
  boardSlug: z.string(),
  cards: z.array(cardSummarySchema),
});

export const createTempCardResponseSchema = z.object({
  id: z.string(),
});

export const finalizeCardResponseSchema = z.object({
  id: z.string(),
  number: z.number(),
});

// Runtime config — fetched from /config.json at app boot (not a build-time env var).
// Default object behavior strips unknown keys, so a deployed config.json with
// future fields an older Portal doesn't recognize will not fail validation.
// (Do not add .strict().)
export const runtimeConfigSchema = z.object({
  apiBaseUrl: z.string().min(1),
});
