import { useEffect, useId, useRef, useState } from 'react';
import mermaid from 'mermaid';

let mermaidInitialized = false;

function ensureMermaidInitialized() {
  if (!mermaidInitialized) {
    mermaid.initialize({ startOnLoad: false, theme: 'dark' });
    mermaidInitialized = true;
  }
}

type MermaidBlockProps = {
  children: string;
};

export function MermaidBlock({ children }: MermaidBlockProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const reactId = useId();
  const mermaidId = `mermaid-${reactId.replace(/:/g, '')}`;
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;

    async function renderDiagram() {
      ensureMermaidInitialized();
      setIsLoading(true);
      setError(null);

      try {
        const { svg } = await mermaid.render(mermaidId, children);
        if (!cancelled && containerRef.current) {
          containerRef.current.innerHTML = svg;
        }
      } catch (err: unknown) {
        if (!cancelled) {
          const msg = err instanceof Error ? err.message : String(err);
          setError(msg);
        }
      } finally {
        if (!cancelled) {
          setIsLoading(false);
        }
      }
    }

    renderDiagram();

    return () => {
      cancelled = true;
    };
  }, [children, mermaidId]);

  if (error) {
    return (
      <div className="rounded-md border border-destructive/50 bg-destructive/10 p-3 text-sm text-destructive">
        <p className="font-medium">Mermaid diagram error</p>
        <pre className="mt-1 whitespace-pre-wrap text-xs">{error}</pre>
      </div>
    );
  }

  if (isLoading) {
    return (
      <div className="rounded-md border bg-muted/30 p-4 text-sm text-muted-foreground">
        Rendering diagram...
      </div>
    );
  }

  return <div ref={containerRef} className="overflow-x-auto [&>svg]:max-w-full" />;
}
