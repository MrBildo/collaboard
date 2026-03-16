import { useEffect, useRef } from 'react';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { cn } from '@/lib/utils';

const PRESET_COLORS = [
  '#ef4444', '#f97316', '#f59e0b', '#eab308', '#84cc16', '#22c55e',
  '#14b8a6', '#06b6d4', '#3b82f6', '#6366f1', '#8b5cf6', '#a855f7',
  '#d946ef', '#ec4899', '#f43f5e', '#6b7280',
];

function isValidHex(value: string): boolean {
  return /^#[0-9a-fA-F]{6}$/.test(value);
}

function normalizeHex(raw: string): string | null {
  const stripped = raw.trim().replace(/^#/, '');
  if (/^[0-9a-fA-F]{6}$/.test(stripped)) {
    return `#${stripped.toLowerCase()}`;
  }
  return null;
}

type ColorPickerProps = {
  value: string;
  onChange: (color: string) => void;
  id?: string;
  label?: string;
  /** Enable global paste interception within the nearest dialog */
  globalPaste?: boolean;
};

export function ColorPicker({ value, onChange, id, label, globalPaste }: ColorPickerProps) {
  const containerRef = useRef<HTMLDivElement>(null);

  // Global paste handler — intercepts paste on the nearest dialog ancestor
  useEffect(() => {
    if (!globalPaste) return;

    const handler = (e: ClipboardEvent) => {
      const text = e.clipboardData?.getData('text/plain');
      if (!text) return;
      const hex = normalizeHex(text);
      if (hex) {
        e.preventDefault();
        onChange(hex);
      }
    };

    // Walk up to find the dialog content container
    const dialog = containerRef.current?.closest('[role="dialog"]');
    const target = (dialog ?? document) as HTMLElement;
    target.addEventListener('paste', handler as EventListener);
    return () => target.removeEventListener('paste', handler as EventListener);
  }, [globalPaste, onChange]);

  const handleInputChange = (raw: string) => {
    // Always keep what the user is typing (even partial)
    // but only commit valid hex values
    const withHash = raw.startsWith('#') ? raw : `#${raw}`;
    if (isValidHex(withHash)) {
      onChange(withHash.toLowerCase());
    } else {
      // Still update the display so user can keep typing
      onChange(withHash);
    }
  };

  const handlePaste = (e: React.ClipboardEvent<HTMLInputElement>) => {
    const text = e.clipboardData.getData('text/plain');
    const hex = normalizeHex(text);
    if (hex) {
      e.preventDefault();
      onChange(hex);
    }
  };

  return (
    <div ref={containerRef} className="flex flex-col gap-1.5">
      {label && <Label htmlFor={id}>{label}</Label>}

      <div className="flex items-center gap-2">
        <span
          className="inline-block h-8 w-8 shrink-0 rounded-md border border-input"
          style={{ backgroundColor: isValidHex(value) ? value : '#ffffff' }}
        />
        <Input
          id={id}
          value={value}
          onChange={(e) => handleInputChange(e.target.value)}
          onPaste={handlePaste}
          placeholder="#3b82f6"
          className={cn('w-28 font-mono', !isValidHex(value) && value !== '' && 'border-destructive')}
          maxLength={7}
        />
      </div>

      <div className="grid grid-cols-8 gap-1">
        {PRESET_COLORS.map((color) => (
          <button
            key={color}
            type="button"
            className={cn(
              'h-6 w-6 rounded-md border-2 transition-transform hover:scale-110 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring',
              value === color ? 'border-foreground' : 'border-transparent',
            )}
            style={{ backgroundColor: color }}
            onClick={() => onChange(color)}
            title={color}
          />
        ))}
      </div>
    </div>
  );
}
