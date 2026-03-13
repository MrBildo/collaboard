import axios from 'axios';
import type { CardItem, Lane } from '@/types';

export type { CardItem, Lane };

export const api = axios.create({
  baseURL: '/api/v1',
  headers: {
    'X-Api-Key': import.meta.env.VITE_API_KEY ?? '',
    'X-User-Key': import.meta.env.VITE_USER_KEY ?? '',
  },
});

export async function fetchBoard(): Promise<{ lanes: Lane[]; cards: CardItem[] }> {
  const { data } = await api.get('/board');
  return data;
}
