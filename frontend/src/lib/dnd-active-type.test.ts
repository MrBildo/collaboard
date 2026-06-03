import { describe, test, expect } from 'vitest';
import { isLaneDragEvent } from './dnd-active-type';

describe('isLaneDragEvent', () => {
  test('returns true when the active draggable is tagged type: lane', () => {
    expect(isLaneDragEvent({ active: { data: { current: { type: 'lane' } } } })).toBe(true);
  });

  test('returns false for a card drag (no data.type — the existing card path)', () => {
    expect(isLaneDragEvent({ active: { data: { current: {} } } })).toBe(false);
  });

  test('returns false when data.current is absent', () => {
    expect(isLaneDragEvent({ active: { data: {} } })).toBe(false);
  });

  test('returns false for any non-lane type', () => {
    expect(isLaneDragEvent({ active: { data: { current: { type: 'card' } } } })).toBe(false);
  });
});
