const AUTH_KEY = 'collaboard-user-key';
const LAST_BOARD_KEY = 'collaboard-last-board';

export function findUserKey(): string | null {
  return localStorage.getItem(AUTH_KEY);
}

export function setUserKey(key: string): void {
  localStorage.setItem(AUTH_KEY, key);
}

export function clearUserKey(): void {
  localStorage.removeItem(AUTH_KEY);
}

export function isLoggedIn(): boolean {
  const key = findUserKey();
  return key !== null && key.trim() !== '';
}

export function findLastBoardSlug(): string | null {
  return localStorage.getItem(LAST_BOARD_KEY);
}

export function setLastBoardSlug(slug: string): void {
  localStorage.setItem(LAST_BOARD_KEY, slug);
}
