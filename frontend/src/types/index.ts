import type { z } from 'zod';
import type {
  boardSchema,
  laneSchema,
  cardSizeSchema,
  cardItemSchema,
  labelSchema,
  cardCommentSchema,
  boardUserSchema,
  attachmentMetaSchema,
  cardSummarySchema,
  cardLabelSummarySchema,
  boardDataSchema,
  pagedCardSummarySchema,
  userDirectoryEntrySchema,
  authMeSchema,
  prunePreviewCardSchema,
  prunePreviewResponseSchema,
  pruneResponseSchema,
  searchResultSchema,
  runtimeConfigSchema,
  versionStatusSchema,
  webhookSubscriptionSchema,
  webhookDeliverySchema,
  webhookDeliveriesPageSchema,
  webhookStatusSchema,
  webhookTestResultSchema,
} from '@/lib/schemas';

export type Board = z.infer<typeof boardSchema>;
export type Lane = z.infer<typeof laneSchema>;
export type CardSize = z.infer<typeof cardSizeSchema>;
export type CardItem = z.infer<typeof cardItemSchema>;
export type Label = z.infer<typeof labelSchema>;
export type CardComment = z.infer<typeof cardCommentSchema>;
export type BoardUser = z.infer<typeof boardUserSchema>;
export type AttachmentMeta = z.infer<typeof attachmentMetaSchema>;
export type CardSummary = z.infer<typeof cardSummarySchema>;
export type CardLabelSummary = z.infer<typeof cardLabelSummarySchema>;
export type BoardData = z.infer<typeof boardDataSchema>;
export type PagedCardSummary = z.infer<typeof pagedCardSummarySchema>;
export type UserDirectoryEntry = z.infer<typeof userDirectoryEntrySchema>;
export type AuthMe = z.infer<typeof authMeSchema>;
export type VersionStatus = z.infer<typeof versionStatusSchema>;

export type UpdateBoardPatch = { name?: string };
export type UpdateCardPatch = {
  name?: string;
  descriptionMarkdown?: string;
  sizeId?: string;
  laneId?: string;
  position?: number;
  labelIds?: string[];
};
export type UpdateLanePatch = { name?: string; position?: number };
export type UpdateSizePatch = { name?: string; ordinal?: number };
export type UpdateLabelPatch = { name?: string; color?: string | null };
export type UpdateUserPatch = { name?: string; role?: number };
export type UpdateCommentPatch = { contentMarkdown?: string };

export type PrunePreviewCard = z.infer<typeof prunePreviewCardSchema>;
export type PrunePreviewResponse = z.infer<typeof prunePreviewResponseSchema>;
export type PruneResponse = z.infer<typeof pruneResponseSchema>;
export type PruneFilters = {
  olderThan?: string;
  laneIds?: string[];
  labelIds?: string[];
  action?: string;
  includeArchived?: boolean;
};

export type SearchResult = z.infer<typeof searchResultSchema>;

export type RuntimeConfig = z.infer<typeof runtimeConfigSchema>;

// Webhooks (#326)
export type WebhookSubscription = z.infer<typeof webhookSubscriptionSchema>;
export type WebhookDelivery = z.infer<typeof webhookDeliverySchema>;
export type WebhookDeliveriesPage = z.infer<typeof webhookDeliveriesPageSchema>;
export type WebhookStatus = z.infer<typeof webhookStatusSchema>;
export type WebhookTestResult = z.infer<typeof webhookTestResultSchema>;

// Create payload — `events` is required and non-empty (or `["*"]`). An omitted
// `secret` creates an unsigned subscription. `enabled` defaults to true server-side.
export type CreateWebhookInput = {
  url: string;
  events: string[];
  secret?: string;
  enabled?: boolean;
  name?: string;
};

// Update payload — every field optional; an omitted field is left unchanged.
// The secret follows set / keep / clear: omit `secret` → keep; a `secret` value
// → replace; `clearSecret: true` → remove (go unsigned). Never send both.
export type UpdateWebhookPatch = {
  url?: string;
  events?: string[];
  secret?: string;
  clearSecret?: boolean;
  enabled?: boolean;
  name?: string;
};
