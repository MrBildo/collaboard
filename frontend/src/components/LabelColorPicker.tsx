import { useCallback, useState } from 'react';
import {
  ColorPicker,
  ColorPickerArea,
  ColorPickerContent,
  ColorPickerEyeDropper,
  ColorPickerHueSlider,
  ColorPickerInput,
  ColorPickerSwatch,
  ColorPickerTrigger,
} from '@/components/ui/color-picker';
import { cn } from '@/lib/utils';

type LabelColorPickerProps = {
  value: string;
  onValueChange: (value: string) => void;
  className?: string;
};

function normalizeHex(raw: string): string | null {
  const stripped = raw.trim().replace(/^#/, '');
  if (/^[0-9a-fA-F]{6}$/.test(stripped)) {
    return `#${stripped.toLowerCase()}`;
  }
  return null;
}

function LabelColorPicker({ value, onValueChange, className }: LabelColorPickerProps) {
  const [open, setOpen] = useState(false);

  const handlePaste = useCallback(
    (e: React.ClipboardEvent) => {
      const text = e.clipboardData.getData('text/plain');
      const hex = normalizeHex(text);
      if (hex) {
        e.preventDefault();
        onValueChange(hex);
      }
    },
    [onValueChange],
  );

  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === 'Enter') {
        e.preventDefault();
        setOpen(false);
      }
    },
    [],
  );

  return (
    <ColorPicker
      value={value}
      onValueChange={onValueChange}
      open={open}
      onOpenChange={setOpen}
      format="hex"
      className="inline-flex"
    >
      <ColorPickerTrigger className="shrink-0 self-center">
        <ColorPickerSwatch className={cn("cursor-pointer rounded-md", className)} />
      </ColorPickerTrigger>
      <ColorPickerContent onPaste={handlePaste} onKeyDown={handleKeyDown}>
        <ColorPickerArea />
        <ColorPickerHueSlider />
        <div className="flex items-center gap-2">
          <ColorPickerEyeDropper />
          <ColorPickerSwatch className="size-8 shrink-0 rounded-md" />
          <ColorPickerInput withoutAlpha />
        </div>
        <p className="text-center text-xs text-muted-foreground">
          Paste hex color anywhere
        </p>
      </ColorPickerContent>
    </ColorPicker>
  );
}

export { LabelColorPicker };
