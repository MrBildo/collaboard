import axios from 'axios';
import type { AttachmentMeta, BoardUser, CardComment, CardItem, Label, Lane } from '@/types';
import { getUserKey } from '@/lib/auth';

export type { AttachmentMeta, BoardUser, CardComment, CardItem, Label, Lane };

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

// Board
export async function fetchBoard(): Promise<{ lanes: Lane[]; cards: CardItem[] }> {
  const { data } = await api.get('/board');
  return data;
}

// Cards
export async function fetchCards(): Promise<CardItem[]> {
  const { data } = await api.get('/cards');
  return data;
}

export async function fetchCard(id: string): Promise<CardItem> {
  const { data } = await api.get(`/cards/${id}`);
  return data;
}

export async function createCard(card: {
  name: string;
  descriptionMarkdown?: string;
  size?: string;
  blocked?: string | null;
  laneId: string;
  position: number;
}): Promise<CardItem> {
  const { data } = await api.post('/cards', card);
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

// Labels
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

// Comments (update/delete)
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

// Lanes
export async function fetchLanes(): Promise<Lane[]> {
  const { data } = await api.get('/lanes');
  return data;
}

export async function createLane(name: string, position: number): Promise<Lane> {
  const { data } = await api.post('/lanes', { name, position });
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
