import axios from 'axios';
import { z } from 'zod';
import type {
  AttachmentMeta,
  AuthMe,
  Board,
  BoardData,
  BoardUser,
  CardComment,
  CardItem,
  CardSize,
  CardSummary,
  Label,
  Lane,
  PruneFilters,
  PrunePreviewResponse,
  PruneResponse,
  UpdateBoardPatch,
  UpdateCardPatch,
  UpdateCommentPatch,
  UpdateLabelPatch,
  UpdateLanePatch,
  UpdateSizePatch,
  UpdateUserPatch,
  UserDirectoryEntry,
} from '@/types';
import { findUserKey } from '@/lib/auth';
import {
  attachmentMetaSchema,
  authMeSchema,
  boardDataSchema,
  boardSchema,
  boardUserSchema,
  cardCommentSchema,
  cardItemSchema,
  cardSizeSchema,
  cardSummarySchema,
  labelSchema,
  laneSchema,
  prunePreviewResponseSchema,
  pruneResponseSchema,
  reorderResponseSchema,
  uploadAttachmentResponseSchema,
  userDirectoryEntrySchema,
  versionSchema,
} from '@/lib/schemas';

export const api = axios.create({
  baseURL: '/api/v1',
  timeout: 30_000,
});

api.interceptors.request.use((config) => {
  const key = findUserKey();
  if (key) {
    config.headers['X-User-Key'] = key;
  }
  return config;
});

// Version
export async function fetchVersion(): Promise<{ version: string }> {
  const { data } = await api.get('/version');
  return versionSchema.parse(data);
}

// Auth
export async function fetchMe(): Promise<AuthMe> {
  const { data } = await api.get('/auth/me');
  return authMeSchema.parse(data);
}

// Boards
export async function fetchBoards(): Promise<Board[]> {
  const { data } = await api.get('/boards');
  return z.array(boardSchema).parse(data);
}

export async function fetchBoardBySlug(slug: string): Promise<Board> {
  const { data } = await api.get(`/boards/${slug}`);
  return boardSchema.parse(data);
}

export async function createBoard(name: string): Promise<Board> {
  const { data } = await api.post('/boards', { name });
  return boardSchema.parse(data);
}

export async function updateBoard(id: string, patch: UpdateBoardPatch): Promise<Board> {
  const { data } = await api.patch(`/boards/${id}`, patch);
  return boardSchema.parse(data);
}

export async function deleteBoard(id: string): Promise<void> {
  await api.delete(`/boards/${id}`);
}

// Board composite view
export async function fetchBoardData(boardId: string): Promise<BoardData> {
  const { data } = await api.get(`/boards/${boardId}/board`);
  return boardDataSchema.parse(data);
}

// Cards (board-scoped creation/listing, flat by-ID)
export async function fetchCards(boardId: string): Promise<CardSummary[]> {
  const { data } = await api.get(`/boards/${boardId}/cards`);
  return z.array(cardSummarySchema).parse(data);
}

export async function fetchCard(id: string): Promise<CardItem> {
  const { data } = await api.get(`/cards/${id}`);
  return cardItemSchema.parse(data);
}

export async function createCard(boardId: string, card: {
  name: string;
  descriptionMarkdown?: string;
  sizeId?: string;
  laneId: string;
  position: number;
  labelIds?: string[];
}): Promise<CardItem> {
  const { labelIds, ...cardData } = card;
  const { data } = await api.post(`/boards/${boardId}/cards`, cardData);
  const parsed = cardItemSchema.parse(data);

  if (labelIds && labelIds.length > 0) {
    await Promise.all(labelIds.map((labelId) => addCardLabel(parsed.id, labelId)));
  }

  return parsed;
}

export async function updateCard(id: string, patch: UpdateCardPatch): Promise<CardItem> {
  const { data } = await api.patch(`/cards/${id}`, patch);
  return cardItemSchema.parse(data);
}

export async function deleteCard(id: string): Promise<void> {
  await api.delete(`/cards/${id}`);
}

export async function reorderCard(
  id: string,
  laneId: string,
  index: number,
): Promise<{ lanes: Lane[]; cards: CardItem[] }> {
  const { data } = await api.post(`/cards/${id}/reorder`, { laneId, index });
  return reorderResponseSchema.parse(data);
}

// Labels (board-scoped)
export async function fetchLabels(boardId: string): Promise<Label[]> {
  const { data } = await api.get(`/boards/${boardId}/labels`);
  return z.array(labelSchema).parse(data);
}

export async function fetchCardLabels(cardId: string): Promise<Label[]> {
  const { data } = await api.get(`/cards/${cardId}/labels`);
  return z.array(labelSchema).parse(data);
}

export async function addCardLabel(cardId: string, labelId: string): Promise<void> {
  await api.post(`/cards/${cardId}/labels`, { labelId });
}

export async function removeCardLabel(cardId: string, labelId: string): Promise<void> {
  await api.delete(`/cards/${cardId}/labels/${labelId}`);
}

// Comments
export async function fetchComments(cardId: string): Promise<CardComment[]> {
  const { data } = await api.get(`/cards/${cardId}/comments`);
  return z.array(cardCommentSchema).parse(data);
}

export async function createComment(cardId: string, contentMarkdown: string): Promise<CardComment> {
  const { data } = await api.post(`/cards/${cardId}/comments`, { contentMarkdown });
  return cardCommentSchema.parse(data);
}

export async function updateComment(id: string, patch: UpdateCommentPatch): Promise<CardComment> {
  const { data } = await api.patch(`/comments/${id}`, patch);
  return cardCommentSchema.parse(data);
}

export async function deleteComment(id: string): Promise<void> {
  await api.delete(`/comments/${id}`);
}

// Attachments
export async function fetchCardAttachments(cardId: string): Promise<AttachmentMeta[]> {
  const { data } = await api.get(`/cards/${cardId}/attachments`);
  return z.array(attachmentMetaSchema).parse(data);
}

export async function uploadAttachment(
  cardId: string,
  file: File,
): Promise<{ id: string; fileName: string }> {
  const form = new FormData();
  form.append('file', file);
  const { data } = await api.post(`/cards/${cardId}/attachments`, form);
  return uploadAttachmentResponseSchema.parse(data);
}

export async function deleteAttachment(id: string): Promise<void> {
  await api.delete(`/attachments/${id}`);
}

// Users (admin)
export async function fetchUsers(): Promise<BoardUser[]> {
  const { data } = await api.get('/users');
  return z.array(boardUserSchema).parse(data);
}

export async function fetchUserDirectory(): Promise<UserDirectoryEntry[]> {
  const { data } = await api.get('/users/directory');
  return z.array(userDirectoryEntrySchema).parse(data);
}

export async function createUser(name: string, role: number): Promise<BoardUser> {
  const { data } = await api.post('/users', { name, role });
  return boardUserSchema.parse(data);
}

export async function updateUser(id: string, patch: UpdateUserPatch): Promise<BoardUser> {
  const { data } = await api.patch(`/users/${id}`, patch);
  return boardUserSchema.parse(data);
}

export async function deactivateUser(id: string): Promise<void> {
  await api.patch(`/users/${id}/deactivate`);
}

// Lanes (board-scoped creation/listing, flat by-ID)
export async function fetchLanes(boardId: string): Promise<Lane[]> {
  const { data } = await api.get(`/boards/${boardId}/lanes`);
  return z.array(laneSchema).parse(data);
}

export async function createLane(boardId: string, name: string, position: number): Promise<Lane> {
  const { data } = await api.post(`/boards/${boardId}/lanes`, { name, position });
  return laneSchema.parse(data);
}

export async function updateLane(id: string, patch: UpdateLanePatch): Promise<Lane> {
  const { data } = await api.patch(`/lanes/${id}`, patch);
  return laneSchema.parse(data);
}

export async function deleteLane(id: string): Promise<void> {
  await api.delete(`/lanes/${id}`);
}

// Sizes (board-scoped admin)
export async function fetchSizes(boardId: string): Promise<CardSize[]> {
  const { data } = await api.get(`/boards/${boardId}/sizes`);
  return z.array(cardSizeSchema).parse(data);
}

export async function createSize(boardId: string, name: string, ordinal?: number): Promise<CardSize> {
  const { data } = await api.post(`/boards/${boardId}/sizes`, { name, ordinal: ordinal ?? 0 });
  return cardSizeSchema.parse(data);
}

export async function updateSize(id: string, patch: UpdateSizePatch): Promise<CardSize> {
  const { data } = await api.patch(`/sizes/${id}`, patch);
  return cardSizeSchema.parse(data);
}

export async function deleteSize(id: string): Promise<void> {
  await api.delete(`/sizes/${id}`);
}

// Labels (board-scoped admin)
export async function createLabel(boardId: string, name: string, color?: string): Promise<Label> {
  const { data } = await api.post(`/boards/${boardId}/labels`, { name, color });
  return labelSchema.parse(data);
}

export async function updateLabel(boardId: string, id: string, patch: UpdateLabelPatch): Promise<Label> {
  const { data } = await api.patch(`/boards/${boardId}/labels/${id}`, patch);
  return labelSchema.parse(data);
}

export async function deleteLabel(boardId: string, id: string): Promise<void> {
  await api.delete(`/boards/${boardId}/labels/${id}`);
}

// Prune (board-scoped admin)
export async function prunePreview(boardId: string, filters: PruneFilters): Promise<PrunePreviewResponse> {
  const { data } = await api.post(`/boards/${boardId}/prune/preview`, filters);
  return prunePreviewResponseSchema.parse(data);
}

export async function pruneCards(boardId: string, filters: PruneFilters): Promise<PruneResponse> {
  const { data } = await api.post(`/boards/${boardId}/prune`, filters);
  return pruneResponseSchema.parse(data);
}
