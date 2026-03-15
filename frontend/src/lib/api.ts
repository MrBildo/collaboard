import axios from 'axios';
import type { AttachmentMeta, Board, BoardUser, CardComment, CardItem, Label, Lane } from '@/types';
import { getUserKey } from '@/lib/auth';

export type { AttachmentMeta, Board, BoardUser, CardComment, CardItem, Label, Lane };

export const api = axios.create({
  baseURL: '/api/v1',
});

api.interceptors.request.use((config) => {
  const key = getUserKey();
  if (key) {
    config.headers['X-User-Key'] = key;
  }
  return config;
});

// Version
export async function fetchVersion(): Promise<{ version: string }> {
  const { data } = await api.get('/version');
  return data;
}

// Auth
export async function fetchMe(): Promise<{ id: string; name: string; role: number }> {
  const { data } = await api.get('/auth/me');
  return data;
}

// Boards
export async function fetchBoards(): Promise<Board[]> {
  const { data } = await api.get('/boards');
  return data;
}

export async function fetchBoardBySlug(slug: string): Promise<Board> {
  const { data } = await api.get(`/boards/${slug}`);
  return data;
}

export async function createBoard(name: string): Promise<Board> {
  const { data } = await api.post('/boards', { name });
  return data;
}

export async function updateBoard(id: string, patch: Record<string, unknown>): Promise<Board> {
  const { data } = await api.patch(`/boards/${id}`, patch);
  return data;
}

export async function deleteBoard(id: string): Promise<void> {
  await api.delete(`/boards/${id}`);
}

// Board composite view
export async function fetchBoardData(boardId: string): Promise<{ lanes: Lane[]; cards: CardItem[] }> {
  const { data } = await api.get(`/boards/${boardId}/board`);
  return data;
}

// Cards (board-scoped creation/listing, flat by-ID)
export async function fetchCards(boardId: string): Promise<CardItem[]> {
  const { data } = await api.get(`/boards/${boardId}/cards`);
  return data;
}

export async function fetchCard(id: string): Promise<CardItem> {
  const { data } = await api.get(`/cards/${id}`);
  return data;
}

export async function createCard(boardId: string, card: {
  name: string;
  descriptionMarkdown?: string;
  size?: string;
  laneId: string;
  position: number;
}): Promise<CardItem> {
  const { data } = await api.post(`/boards/${boardId}/cards`, card);
  return data;
}

export async function updateCard(id: string, patch: Record<string, unknown>): Promise<CardItem> {
  const { data } = await api.patch(`/cards/${id}`, patch);
  return data;
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
  return data;
}

// Labels (global)
export async function fetchLabels(): Promise<Label[]> {
  const { data } = await api.get('/labels');
  return data;
}

export async function fetchCardLabels(cardId: string): Promise<Label[]> {
  const { data } = await api.get(`/cards/${cardId}/labels`);
  return data;
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
  return data;
}

export async function createComment(cardId: string, contentMarkdown: string): Promise<CardComment> {
  const { data } = await api.post(`/cards/${cardId}/comments`, { contentMarkdown });
  return data;
}

export async function updateComment(id: string, contentMarkdown: string): Promise<CardComment> {
  const { data } = await api.patch(`/comments/${id}`, { contentMarkdown });
  return data;
}

export async function deleteComment(id: string): Promise<void> {
  await api.delete(`/comments/${id}`);
}

// Attachments
export async function fetchCardAttachments(cardId: string): Promise<AttachmentMeta[]> {
  const { data } = await api.get(`/cards/${cardId}/attachments`);
  return data;
}

export async function uploadAttachment(
  cardId: string,
  file: File,
): Promise<{ id: string; fileName: string }> {
  const form = new FormData();
  form.append('file', file);
  const { data } = await api.post(`/cards/${cardId}/attachments`, form);
  return data;
}

export async function deleteAttachment(id: string): Promise<void> {
  await api.delete(`/attachments/${id}`);
}

// Users (admin)
export async function fetchUsers(): Promise<BoardUser[]> {
  const { data } = await api.get('/users');
  return data;
}

export async function fetchUserDirectory(): Promise<{ id: string; name: string }[]> {
  const { data } = await api.get('/users/directory');
  return data;
}

export async function createUser(name: string, role: number): Promise<BoardUser> {
  const { data } = await api.post('/users', { name, role });
  return data;
}

export async function updateUser(id: string, patch: Record<string, unknown>): Promise<BoardUser> {
  const { data } = await api.patch(`/users/${id}`, patch);
  return data;
}

export async function deactivateUser(id: string): Promise<void> {
  await api.patch(`/users/${id}/deactivate`);
}

// Lanes (board-scoped creation/listing, flat by-ID)
export async function fetchLanes(boardId: string): Promise<Lane[]> {
  const { data } = await api.get(`/boards/${boardId}/lanes`);
  return data;
}

export async function createLane(boardId: string, name: string, position: number): Promise<Lane> {
  const { data } = await api.post(`/boards/${boardId}/lanes`, { name, position });
  return data;
}

export async function updateLane(id: string, patch: Record<string, unknown>): Promise<Lane> {
  const { data } = await api.patch(`/lanes/${id}`, patch);
  return data;
}

export async function deleteLane(id: string): Promise<void> {
  await api.delete(`/lanes/${id}`);
}

// Labels (admin)
export async function createLabel(name: string, color?: string): Promise<Label> {
  const { data } = await api.post('/labels', { name, color });
  return data;
}

export async function updateLabel(id: string, patch: Record<string, unknown>): Promise<Label> {
  const { data } = await api.patch(`/labels/${id}`, patch);
  return data;
}

export async function deleteLabel(id: string): Promise<void> {
  await api.delete(`/labels/${id}`);
}
