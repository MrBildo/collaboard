import { Switch as SwitchPrimitive } from '@base-ui/react/switch';

import { cn } from '@/lib/utils';

// Toggle switch primitive (base-ui), house style. base-ui's Switch.Root renders
// a <span> plus a hidden <input> and emits data-checked / data-unchecked state
// attributes — the same v4 bare-data-attribute idiom the Checkbox uses (the on
// fill must be `data-checked:`, not the v4-dead bare form).
// Reads by thumb POSITION (left/right) and track COLOR (muted/primary) so it
// stays legible in both themes without a `dark:` variant.
function Switch({ className, ...props }: SwitchPrimitive.Root.Props) {
  return (
    <SwitchPrimitive.Root
      data-slot="switch"
      className={cn(
        'peer inline-flex h-5 w-9 shrink-0 cursor-pointer items-center rounded-full border border-border px-0.5 shadow-xs outline-none transition-colors focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50 data-checked:border-primary data-checked:bg-primary data-unchecked:bg-muted',
        className,
      )}
      {...props}
    >
      <SwitchPrimitive.Thumb
        data-slot="switch-thumb"
        className="pointer-events-none block size-3.5 rounded-full bg-card shadow-sm ring-0 transition-transform data-checked:translate-x-3.5 data-unchecked:translate-x-0"
      />
    </SwitchPrimitive.Root>
  );
}

export { Switch };
