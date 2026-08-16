import type { LucideIcon } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';

type EmptyStateAction = {
  label: string;
  onClick: () => void;
};

type EmptyStateProps = {
  icon: LucideIcon;
  title: string;
  description?: string;
  action?: EmptyStateAction;
  className?: string;
};

// Teaching empty-state primitive. Every "nothing here
// yet" surface — empty lane, board with no lanes, zero boards, empty labels —
// renders this instead of a blank <div> or a bare line of text, so a first-run
// user meets a "here's what this is and here's the one button to start" cue
// where their eye already is.
//
// Self-disposing by construction: it's rendered only while the underlying
// collection is empty and vanishes the moment the first item is added — there
// is no persisted "seen" flag and no dismissal state. Role-awareness
// lives at the call site: an admin passes an `action`, a non-admin passes only
// explanatory `description` text and no dead button.
function EmptyState({ icon: Icon, title, description, action, className }: EmptyStateProps) {
  return (
    <div
      className={cn(
        'flex flex-col items-center justify-center gap-2 px-4 py-8 text-center',
        className,
      )}
    >
      <Icon className="h-8 w-8 text-muted-foreground/60" aria-hidden="true" />
      <p className="text-sm font-medium text-foreground">{title}</p>
      {description && <p className="max-w-xs text-sm text-muted-foreground">{description}</p>}
      {action && (
        <Button variant="outline" size="sm" className="mt-1" onClick={action.onClick}>
          {action.label}
        </Button>
      )}
    </div>
  );
}

export { EmptyState };
export type { EmptyStateProps };
