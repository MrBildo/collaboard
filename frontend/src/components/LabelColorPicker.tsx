import {
  ColorPicker,
  ColorPickerArea,
  ColorPickerContent,
  ColorPickerEyeDropper,
  ColorPickerFormatSelect,
  ColorPickerHueSlider,
  ColorPickerInput,
  ColorPickerSwatch,
  ColorPickerTrigger,
} from '@/components/ui/color-picker';

type LabelColorPickerProps = {
  value: string;
  onValueChange: (value: string) => void;
  className?: string;
};

function LabelColorPicker({ value, onValueChange, className }: LabelColorPickerProps) {
  return (
    <ColorPicker value={value} onValueChange={onValueChange}>
      <ColorPickerTrigger>
        <ColorPickerSwatch className={className ?? "size-7 cursor-pointer rounded-md"} />
      </ColorPickerTrigger>
      <ColorPickerContent>
        <ColorPickerArea />
        <ColorPickerHueSlider />
        <div className="flex items-center gap-2">
          <ColorPickerEyeDropper />
          <ColorPickerSwatch className="size-8 shrink-0 rounded-md" />
          <ColorPickerInput />
          <ColorPickerFormatSelect />
        </div>
      </ColorPickerContent>
    </ColorPicker>
  );
}

export { LabelColorPicker };
