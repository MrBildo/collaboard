import axios from 'axios';

export const api = axios.create({
  baseURL: '/api/v1',
  headers: {
    'X-Api-Key': import.meta.env.VITE_API_KEY ?? '',
    'X-User-Key': import.meta.env.VITE_USER_KEY ?? '',
  },
});

export type Lane = { id: string; name: string; position: number };
export type CardItem = { id: string; number: number; name: string; descriptionMarkdown: string; laneId: string; position: number; status?: string; size: string };

export async function fetchBoard(): Promise<{ lanes: Lane[]; cards: CardItem[] }> {
  const { data } = await api.get('/board');
  return data;
}
