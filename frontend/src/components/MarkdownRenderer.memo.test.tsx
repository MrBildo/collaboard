import { describe, test, expect, vi, beforeEach } from 'vitest';
import { useState } from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MarkdownRenderer } from './MarkdownRenderer';
import type { CardLinkPreviewData } from './CardLinkPreview';

// Stand in for the real markdown pipeline with a render counter. react-markdown
// re-parses on every render, so counting how often this stub renders is a direct
// measure of how often MarkdownRenderer re-runs the parse — which is exactly what
// the memoization is there to avoid. Scoped to this file so the real-pipeline
// tests keep the genuine renderer.
const { markdownRenders } = vi.hoisted(() => ({ markdownRenders: vi.fn() }));

vi.mock('react-markdown', () => ({
  default: ({ children }: { children: string }) => {
    markdownRenders();
    return <div data-testid="markdown-body">{children}</div>;
  },
}));

// MarkdownRenderer imports MermaidBlock, which imports mermaid; keep it inert.
vi.mock('mermaid', () => ({
  default: { initialize: vi.fn(), render: vi.fn() },
}));

beforeEach(() => {
  vi.clearAllMocks();
});

// A parent that can be forced to re-render without changing the props it passes
// down — the shape that produces the per-keystroke re-render in the real card
// detail view (a controlled input elsewhere in the same tree updates state).
function ForcedReRenderParent({ text }: { text: string }) {
  const [tick, setTick] = useState(0);
  return (
    <>
      <button onClick={() => setTick((n) => n + 1)}>force</button>
      <span data-testid="tick">{tick}</span>
      <MarkdownRenderer>{text}</MarkdownRenderer>
    </>
  );
}

describe('MarkdownRenderer memoization', () => {
  test('skips the markdown re-render when the parent re-renders with unchanged props', async () => {
    const user = userEvent.setup();
    render(<ForcedReRenderParent text="unchanging content" />);

    expect(markdownRenders).toHaveBeenCalledTimes(1);

    await user.click(screen.getByText('force'));

    // Control: the parent really did re-render (otherwise the count below proves
    // nothing) — the tick advanced.
    expect(screen.getByTestId('tick')).toHaveTextContent('1');
    // The memo bailed out: the parse did not run a second time.
    expect(markdownRenders).toHaveBeenCalledTimes(1);
  });

  test('re-renders when the markdown content itself changes', () => {
    const { rerender } = render(<MarkdownRenderer>{'first'}</MarkdownRenderer>);
    expect(markdownRenders).toHaveBeenCalledTimes(1);

    rerender(<MarkdownRenderer>{'second'}</MarkdownRenderer>);
    expect(markdownRenders).toHaveBeenCalledTimes(2);
  });

  test('re-renders when the preview map changes, as it does when board data refreshes', () => {
    // useCardLinkContext hands a fresh Map identity whenever the board-data cache
    // updates; the memo must not swallow that, or a #NNN link preview would show
    // stale card data after the board changes.
    const previewsA = new Map<number, CardLinkPreviewData>();
    const previewsB = new Map<number, CardLinkPreviewData>();

    const { rerender } = render(
      <MarkdownRenderer cardPreviews={previewsA}>{'stable text'}</MarkdownRenderer>,
    );
    expect(markdownRenders).toHaveBeenCalledTimes(1);

    rerender(<MarkdownRenderer cardPreviews={previewsB}>{'stable text'}</MarkdownRenderer>);
    expect(markdownRenders).toHaveBeenCalledTimes(2);
  });
});
