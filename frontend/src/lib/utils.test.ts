import { describe, test, expect } from 'vitest';
import { cn, getContrastColor, getReadableColor } from './utils';

describe('cn', () => {
  test('merges class names', () => {
    expect(cn('foo', 'bar')).toBe('foo bar');
  });

  test('handles conditional classes', () => {
    const isHidden = false;
    expect(cn('base', isHidden && 'hidden', 'visible')).toBe('base visible');
  });

  test('resolves tailwind conflicts', () => {
    expect(cn('px-2', 'px-4')).toBe('px-4');
  });
});

describe('getContrastColor', () => {
  test('returns white for dark backgrounds', () => {
    expect(getContrastColor('#000000')).toBe('#fff');
    expect(getContrastColor('#333333')).toBe('#fff');
    expect(getContrastColor('#1a1a2e')).toBe('#fff');
  });

  test('returns dark color for light backgrounds', () => {
    expect(getContrastColor('#ffffff')).toBe('#000');
    expect(getContrastColor('#ffff00')).toBe('#000');
    expect(getContrastColor('#f0f0f0')).toBe('#000');
  });

  test('handles null input', () => {
    expect(getContrastColor(null)).toBe('#fff');
  });

  test('handles undefined input', () => {
    expect(getContrastColor(undefined)).toBe('#fff');
  });

  test('handles hex with hash prefix', () => {
    const result = getContrastColor('#ff0000');
    expect(result === '#000' || result === '#fff').toBe(true);
  });

  test('handles hex without hash prefix', () => {
    const result = getContrastColor('ff0000');
    expect(result === '#000' || result === '#fff').toBe(true);
  });

  test('returns correct contrast for mid-luminance colors', () => {
    // Red has luminance ~0.2126 which is above 0.179 threshold, so it returns dark
    expect(getContrastColor('#ff0000')).toBe('#000');
    // Pure green is bright
    expect(getContrastColor('#00ff00')).toBe('#000');
    // Pure blue is very dark (luminance ~0.0722)
    expect(getContrastColor('#0000ff')).toBe('#fff');
  });
});

describe('getReadableColor', () => {
  test('returns a fallback gray for null input', () => {
    expect(getReadableColor(null)).toBe('#6b7280');
  });

  test('returns a fallback gray for undefined input', () => {
    expect(getReadableColor(undefined)).toBe('#6b7280');
  });

  test('returns the original color for dark inputs', () => {
    // Dark colors with luminance <= 0.3 are returned as-is
    expect(getReadableColor('#000000')).toBe('#000000');
    expect(getReadableColor('#333333')).toBe('#333333');
  });

  test('darkens bright colors for readability', () => {
    const result = getReadableColor('#ffffff');
    // Should be darkened, not the original white
    expect(result).not.toBe('#ffffff');
    expect(result).toMatch(/^#[0-9a-f]{6}$/);
  });

  test('returns a valid hex color for various inputs', () => {
    expect(getReadableColor('#ff0000')).toMatch(/^#[0-9a-f]{6}$/);
    expect(getReadableColor('#00ff00')).toMatch(/^#[0-9a-f]{6}$/);
    expect(getReadableColor('#0000ff')).toMatch(/^#[0-9a-f]{6}$/);
  });

  test('darkens yellow for readability', () => {
    // Yellow (#ffff00) has high luminance, should be darkened
    const result = getReadableColor('#ffff00');
    expect(result).not.toBe('#ffff00');
    expect(result).toMatch(/^#[0-9a-f]{6}$/);
  });
});
