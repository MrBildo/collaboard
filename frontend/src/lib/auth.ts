const AUTH_KEY = 'collaboard-user-key';
const LAST_BOARD_KEY = 'collaboard-last-board';

export function getUserKey(): string | null {
  return localStorage.getItem(AUTH_KEY);
}

export function setUserKey(key: string): void {
  localStorage.setItem(AUTH_KEY, key);
}

export function clearUserKey(): void {
  localStorage.removeItem(AUTH_KEY);
}

export function isLoggedIn(): boolean {
  const key = getUserKey();
  return key !== null && key.trim() !== '';
}

export function getLastBoardSlug(): string | null {
  return localStorage.getItem(LAST_BOARD_KEY);
}

export function setLastBoardSlug(slug: string): void {
  localStorage.setItem(LAST_BOARD_KEY, slug);
}
