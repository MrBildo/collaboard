import { describe, test, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { MarkdownRenderer } from './MarkdownRenderer';

vi.mock('mermaid', () => ({
  default: {
    initialize: vi.fn(),
    render: vi.fn(),
  },
}));

import mermaid from 'mermaid';

const mockedRender = vi.mocked(mermaid.render);

beforeEach(() => {
  vi.clearAllMocks();
});

describe('MarkdownRenderer', () => {
  test('renders plain text as markdown', () => {
    render(<MarkdownRenderer>{'Hello world'}</MarkdownRenderer>);
    expect(screen.getByText('Hello world')).toBeInTheDocument();
  });

  test('renders inline code as a code element', () => {
    render(<MarkdownRenderer>{'Use `console.log` here'}</MarkdownRenderer>);
    expect(screen.getByText('console.log')).toBeInTheDocument();
    expect(screen.getByText('console.log').tagName).toBe('CODE');
  });

  test('renders fenced code blocks with syntax highlighting', () => {
    const md = '```js\nconst x = 1;\n```';
    const { container } = render(<MarkdownRenderer>{md}</MarkdownRenderer>);
    const codeEl = container.querySelector('code.hljs');
    expect(codeEl).not.toBeNull();
    expect(codeEl!.textContent).toContain('const x = 1;');
  });

  test('renders GFM tables', () => {
    const md = '| A | B |\n|---|---|\n| 1 | 2 |';
    render(<MarkdownRenderer>{md}</MarkdownRenderer>);
    expect(screen.getByText('A')).toBeInTheDocument();
    expect(screen.getByText('1')).toBeInTheDocument();
  });

  test('renders mermaid fenced blocks via MermaidBlock', async () => {
    mockedRender.mockResolvedValue({
      svg: '<svg data-testid="mermaid-svg">diagram</svg>',
      diagramType: 'flowchart-v2',
      bindFunctions: undefined,
    });

    const md = '```mermaid\ngraph TD;\n  A-->B;\n```';
    render(<MarkdownRenderer>{md}</MarkdownRenderer>);

    await waitFor(() => {
      expect(mockedRender).toHaveBeenCalledWith(
        expect.stringContaining('mermaid'),
        'graph TD;\n  A-->B;',
      );
    });
  });

  test('shows error state when mermaid render fails', async () => {
    mockedRender.mockRejectedValue(new Error('Parse error'));

    const md = '```mermaid\ninvalid diagram\n```';
    render(<MarkdownRenderer>{md}</MarkdownRenderer>);

    await waitFor(() => {
      expect(screen.getByText('Mermaid diagram error')).toBeInTheDocument();
      expect(screen.getByText('Parse error')).toBeInTheDocument();
    });
  });

  test('shows loading state while mermaid renders', () => {
    mockedRender.mockReturnValue(new Promise(() => {})); // never resolves

    const md = '```mermaid\ngraph TD;\n```';
    render(<MarkdownRenderer>{md}</MarkdownRenderer>);

    expect(screen.getByText('Rendering diagram...')).toBeInTheDocument();
  });

  test('renders HTML ins tag as inserted text', () => {
    render(<MarkdownRenderer>{'<ins>inserted</ins>'}</MarkdownRenderer>);
    const el = screen.getByText('inserted');
    expect(el.tagName).toBe('INS');
  });

  test('renders HTML del tag as deleted text', () => {
    render(<MarkdownRenderer>{'<del>removed</del>'}</MarkdownRenderer>);
    const el = screen.getByText('removed');
    expect(el.tagName).toBe('DEL');
  });

  test('renders HTML sup and sub tags', () => {
    render(<MarkdownRenderer>{'H<sub>2</sub>O is 10<sup>3</sup>'}</MarkdownRenderer>);
    expect(screen.getByText('2').tagName).toBe('SUB');
    expect(screen.getByText('3').tagName).toBe('SUP');
  });

  test('strips script tags for security', () => {
    render(<MarkdownRenderer>{'<script>alert("xss")</script>safe text'}</MarkdownRenderer>);
    expect(screen.getByText('safe text')).toBeInTheDocument();
    expect(document.querySelector('script')).toBeNull();
  });

  test('adds syntax highlighting classes to fenced code blocks', () => {
    const md = '```typescript\nconst x: number = 1;\n```';
    const { container } = render(<MarkdownRenderer>{md}</MarkdownRenderer>);
    const codeEl = container.querySelector('code.hljs');
    expect(codeEl).not.toBeNull();
  });

  test('converts emoji shortcodes to unicode emoji', () => {
    render(<MarkdownRenderer>{':rocket: launch'}</MarkdownRenderer>);
    expect(screen.getByText(/\u{1F680}/u)).toBeInTheDocument();
  });

  test('adds target _blank to external links', () => {
    render(<MarkdownRenderer>{'[example](https://example.com)'}</MarkdownRenderer>);
    const link = screen.getByRole('link', { name: 'example' });
    expect(link).toHaveAttribute('target', '_blank');
    expect(link).toHaveAttribute('rel', expect.stringContaining('noopener'));
  });
});

describe('MarkdownRenderer card-link autolinking (#273)', () => {
  // Card-link autolinking emits relative `/boards/...` hrefs that render as a
  // React Router <Link>, so these cases mount under a router. Fork 3a: a `#N`
  // is linkified only when N is in the live (non-archived) card-number set.
  const slug = 'demo';
  const liveCards = new Set([28, 273]);

  function renderWithLinks(markdown: string, cardNumbers: Set<number> = liveCards) {
    return render(
      <MemoryRouter>
        <MarkdownRenderer boardSlug={slug} cardNumbers={cardNumbers}>
          {markdown}
        </MarkdownRenderer>
      </MemoryRouter>,
    );
  }

  test('linkifies a live card reference to its card route', () => {
    renderWithLinks('See #28 for context');
    const link = screen.getByRole('link', { name: '#28' });
    expect(link).toHaveAttribute('href', '/boards/demo/cards/28');
  });

  test('renders an internal card link as a router link, not a hard reload anchor', () => {
    renderWithLinks('See #28');
    const link = screen.getByRole('link', { name: '#28' });
    // A relative href left untouched by rehype-external-links — no new tab.
    expect(link).not.toHaveAttribute('target');
    expect(link.getAttribute('href')).toBe('/boards/demo/cards/28');
  });

  test('leaves a reference to a non-live (archived/unknown) card as plain text', () => {
    renderWithLinks('See #9999 and #28');
    // #9999 is not in the live set — plain text, no link.
    expect(screen.queryByRole('link', { name: '#9999' })).toBeNull();
    expect(screen.getByText(/#9999/)).toBeInTheDocument();
    // #28 still linkifies alongside it.
    expect(screen.getByRole('link', { name: '#28' })).toBeInTheDocument();
  });

  test('does not autolink without board context (props omitted)', () => {
    render(
      <MemoryRouter>
        <MarkdownRenderer>{'See #28'}</MarkdownRenderer>
      </MemoryRouter>,
    );
    expect(screen.queryByRole('link', { name: '#28' })).toBeNull();
    expect(screen.getByText(/#28/)).toBeInTheDocument();
  });

  test('does not linkify inside a fenced code block', () => {
    const md = '```\n#28 in a code fence\n```';
    const { container } = renderWithLinks(md);
    expect(screen.queryByRole('link')).toBeNull();
    expect(container.querySelector('code')?.textContent).toContain('#28');
  });

  test('does not linkify inside inline code', () => {
    renderWithLinks('Use `#28` literally');
    expect(screen.queryByRole('link')).toBeNull();
    expect(screen.getByText('#28').tagName).toBe('CODE');
  });

  test('does not re-linkify a reference inside an existing markdown link', () => {
    renderWithLinks('[go here](https://example.com/issues/28#28)');
    // Exactly one link — the authored one — and it is not rewritten to a card route.
    const links = screen.getAllByRole('link');
    expect(links).toHaveLength(1);
    expect(links[0].getAttribute('href')).toBe('https://example.com/issues/28#28');
  });

  test('does not linkify a hex color (digits followed by letters)', () => {
    renderWithLinks('Background #28a745 looks good');
    expect(screen.queryByRole('link')).toBeNull();
    expect(screen.getByText(/#28a745/)).toBeInTheDocument();
  });

  test('does not linkify a fragment-style ref with a trailing hyphen', () => {
    renderWithLinks('anchor #28-foo here');
    expect(screen.queryByRole('link')).toBeNull();
    expect(screen.getByText(/#28-foo/)).toBeInTheDocument();
  });
});
