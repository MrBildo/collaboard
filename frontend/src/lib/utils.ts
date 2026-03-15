import { clsx, type ClassValue } from 'clsx';
import { twMerge } from 'tailwind-merge';

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

function parseLuminance(hex: string): { r: number; g: number; b: number; luminance: number } {
  const h = hex.replace('#', '');
  const r = parseInt(h.substring(0, 2), 16);
  const g = parseInt(h.substring(2, 4), 16);
  const b = parseInt(h.substring(4, 6), 16);
  const linearize = (c: number) => { const s = c / 255; return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4; };
  const luminance = 0.2126 * linearize(r) + 0.7152 * linearize(g) + 0.0722 * linearize(b);
  return { r, g, b, luminance };
}

export function getContrastColor(hex: string | null | undefined): string {
  if (!hex) return '#fff';
  return parseLuminance(hex).luminance > 0.179 ? '#000' : '#fff';
}

export function getReadableColor(hex: string | null | undefined): string {
  if (!hex) return '#6b7280';
  const { r, g, b, luminance } = parseLuminance(hex);
  if (luminance <= 0.3) return hex;
  const factor = Math.min(0.55, 0.3 / luminance);
  const darken = (v: number) => Math.round(v * factor).toString(16).padStart(2, '0');
  return `#${darken(r)}${darken(g)}${darken(b)}`;
}
