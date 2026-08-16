import { Toaster as Sonner, type ToasterProps } from 'sonner';
import { CircleCheck, Info, TriangleAlert, OctagonX, Loader2 } from 'lucide-react';

// Themed off the project's CSS custom properties rather than next-themes.
// Collaboard switches light/dark via the `data-theme` attribute on the root
// element (see styles.css), and the --popover / --border / --foreground tokens
// already cascade through that selector — so sonner inherits the correct theme
// for free without a theme prop. This keeps the toast surface on the same
// semantic-token model as the rest of the design system.
//
// Accessibility floor: sonner renders an
// aria-live region. Error toasts are announced assertively (role="alert");
// success/info toasts are announced politely. Every toast pairs an icon with
// its text so the channel is never color-alone. Sonner's own stylesheet damps
// toast enter/exit transitions under prefers-reduced-motion; the floor extends
// toast dwell time under the same query (see lib/mutation-floor.ts).
function Toaster({ ...props }: ToasterProps) {
  return (
    <Sonner
      className="toaster group"
      icons={{
        success: <CircleCheck className="size-4 text-primary" />,
        info: <Info className="size-4 text-muted-foreground" />,
        warning: <TriangleAlert className="size-4 text-accent-foreground" />,
        error: <OctagonX className="size-4 text-destructive" />,
        loading: <Loader2 className="size-4 animate-spin" />,
      }}
      style={
        {
          '--normal-bg': 'var(--popover)',
          '--normal-text': 'var(--popover-foreground)',
          '--normal-border': 'var(--border)',
          '--border-radius': 'var(--radius)',
        } as React.CSSProperties
      }
      {...props}
    />
  );
}

export { Toaster };
