import {
  ColorPicker,
  ColorPickerAlphaSlider,
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

function LabelColorPicker({ value, onValueChange, className }: LabelColorPickerProps) {
  return (
    <ColorPicker value={value} onValueChange={onValueChange} format="hex" className="inline-flex">
      <ColorPickerTrigger className="shrink-0 self-center">
        <ColorPickerSwatch className={cn("cursor-pointer rounded-md", className)} />
      </ColorPickerTrigger>
      <ColorPickerContent>
        <ColorPickerArea />
        <ColorPickerHueSlider />
        <ColorPickerAlphaSlider />
        <div className="flex items-center gap-2">
          <ColorPickerEyeDropper />
          <ColorPickerSwatch className="size-8 shrink-0 rounded-md" />
          <ColorPickerInput />
        </div>
      </ColorPickerContent>
    </ColorPicker>
  );
}

export { LabelColorPicker };
