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
  userDirectoryEntrySchema,
  authMeSchema,
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
export type UserDirectoryEntry = z.infer<typeof userDirectoryEntrySchema>;
export type AuthMe = z.infer<typeof authMeSchema>;
