import axios from 'axios';
import type { CardComment, CardItem, Label, Lane } from '@/types';

export type { CardComment, CardItem, Label, Lane };

export const api = axios.create({
  baseURL: '/api/v1',
  headers: {
    'X-User-Key': import.meta.env.VITE_USER_KEY ?? '',
  },
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

export async function updateCard(
  id: string,
  patch: Record<string, unknown>,
): Promise<CardItem> {
  const { data } = await api.patch(`/cards/${id}`, patch);
  return data;
}

export async function deleteCard(id: string): Promise<void> {
  await api.delete(`/cards/${id}`);
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

// Comments
export async function fetchComments(cardId: string): Promise<CardComment[]> {
  const { data } = await api.get(`/cards/${cardId}/comments`);
  return data;
}

export async function createComment(
  cardId: string,
  contentMarkdown: string,
): Promise<CardComment> {
  const { data } = await api.post(`/cards/${cardId}/comments`, { contentMarkdown });
  return data;
}

// Lanes
export async function fetchLanes(): Promise<Lane[]> {
  const { data } = await api.get('/lanes');
  return data;
}
