import { AlertCircle } from 'lucide-react';
import { cn } from '@/lib/utils';

type InlineErrorProps = {
  message: string;
  className?: string;
};

// Inline form/field error surface. Formalizes
// the bare `text-destructive` paragraph the admin tabs hand-roll today
// (editable-list.tsx) into a single accessible primitive.
//
// Accessibility:
//   - role="alert" + aria-live="assertive": this is a true error the operator
//     must act on, attached to the form they're looking at, so it interrupts.
//   - color-not-alone: an icon pairs with the text, so the error is legible
//     without relying on the destructive color (color-blindness floor).
export function InlineError({ message, className }: InlineErrorProps) {
  return (
    <div
      role="alert"
      aria-live="assertive"
      className={cn(
        'flex items-start gap-2 rounded-md border border-destructive/30 bg-destructive/5 px-3 py-2 text-sm text-destructive',
        className,
      )}
    >
      <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
      <span>{message}</span>
    </div>
  );
}
