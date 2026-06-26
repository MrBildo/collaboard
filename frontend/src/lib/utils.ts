import { clsx, type ClassValue } from 'clsx';
import { twMerge } from 'tailwind-merge';

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

export function isTextInputFocused(): boolean {
  const el = document.activeElement;
  if (!el) return false;
  const tag = el.tagName.toLowerCase();
  if (tag === 'textarea') return true;
  if (tag === 'input' && (el as HTMLInputElement).type !== 'file') return true;
  if ((el as HTMLElement).isContentEditable) return true;
  return false;
}

export function buildPasteFileName(mimeType: string): string {
  const ext = mimeType.split('/')[1]?.replace('jpeg', 'jpg') ?? 'bin';
  const now = new Date();
  const ts = now.toISOString().replace(/[-:T]/g, '').replace(/\..+/, '').slice(0, 15);
  return `pasted-image-${ts}.${ext}`;
}

function parseLuminance(hex: string): { r: number; g: number; b: number; luminance: number } {
  const h = hex.replace('#', '');
  const r = parseInt(h.substring(0, 2), 16);
  const g = parseInt(h.substring(2, 4), 16);
  const b = parseInt(h.substring(4, 6), 16);
  const linearize = (c: number) => {
    const s = c / 255;
    return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  };
  const luminance = 0.2126 * linearize(r) + 0.7152 * linearize(g) + 0.0722 * linearize(b);
  return { r, g, b, luminance };
}

export function getContrastColor(hex: string | null | undefined): string {
  if (!hex) return '#fff';
  return parseLuminance(hex).luminance > 0.179 ? '#000' : '#fff';
}

export function arraysEqual(a: string[], b: string[]): boolean {
  if (a.length !== b.length) return false;
  const sorted1 = [...a].sort();
  const sorted2 = [...b].sort();
  return sorted1.every((v, i) => v === sorted2[i]);
}

const DATE_FORMAT: Intl.DateTimeFormatOptions = {
  year: 'numeric',
  month: 'short',
  day: 'numeric',
  hour: 'numeric',
  minute: '2-digit',
};

export function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString('en-US', DATE_FORMAT);
}

// Compact relative time ("just now", "2m ago", "3h ago", "5d ago") for delivery
// timestamps. Falls back to an absolute date past a week, where "Nd ago" stops
// being useful. The frontend owns display formatting (the API sends ISO UTC).
export function formatRelativeTime(iso: string, now: Date = new Date()): string {
  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) return '';
  const seconds = Math.round((now.getTime() - then) / 1000);
  if (seconds < 0) return 'just now';
  if (seconds < 45) return 'just now';
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.round(hours / 24);
  if (days <= 7) return `${days}d ago`;
  return formatDateTime(iso);
}

export function formatFileSize(bytes: number): string {
  if (bytes === 0) return '0 B';
  const units = ['B', 'KB', 'MB'];
  const i = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1);
  const value = bytes / Math.pow(1024, i);
  return `${value.toFixed(i === 0 ? 0 : 1)} ${units[i]}`;
}

export function getReadableColor(hex: string | null | undefined): string {
  if (!hex) return '#6b7280';
  const { r, g, b, luminance } = parseLuminance(hex);
  if (luminance <= 0.3) return hex;
  const factor = Math.min(0.55, 0.3 / luminance);
  const darken = (v: number) =>
    Math.round(v * factor)
      .toString(16)
      .padStart(2, '0');
  return `#${darken(r)}${darken(g)}${darken(b)}`;
}
