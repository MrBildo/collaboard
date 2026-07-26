import { describe, test, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { MarkdownRenderer } from './MarkdownRenderer';
import type { CardLinkPreviewData } from './CardLinkPreview';
import type { CardSummary } from '@/types';

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

describe('MarkdownRenderer link origin handling', () => {
  // A link in a card description is followed inside the app only when it
  // genuinely points back at this origin. Anything else is an external link and
  // has to carry the external-link protections, however innocuous its shape.
  //
  // Descriptions can contain HTML as well as markdown, and the two reach the
  // renderer differently: markdown's own link syntax percent-encodes anything
  // unusual in a destination, while an HTML anchor's href arrives verbatim. So
  // the shapes below are written as HTML — that is the authoring path on which
  // they survive, and testing them as markdown links would have quietly tested
  // nothing (see the markdown-encoding case further down).
  const offOriginAnchors: Array<[string, string]> = [
    ['//elsewhere.example/x', 'scheme-relative'],
    ['/\\elsewhere.example/x', 'backslash reads as a second slash'],
    ['/\\\\elsewhere.example', 'two backslashes'],
    ['/&#9;/elsewhere.example', 'tab is dropped before parsing, leaving a scheme-relative link'],
    ['/&#10;/elsewhere.example', 'newline is dropped before parsing, same result'],
  ];

  function CurrentPath() {
    return <span data-testid="current-path">{useLocation().pathname}</span>;
  }

  function renderAt(markdown: string, path = '/boards/demo') {
    return render(
      <MemoryRouter initialEntries={[path]}>
        <CurrentPath />
        <MarkdownRenderer boardSlug="demo" cardNumbers={new Set([28])}>
          {markdown}
        </MarkdownRenderer>
      </MemoryRouter>,
    );
  }

  test.each(offOriginAnchors)('treats %j as external — %s', (href) => {
    renderAt(`<a href="${href}">somewhere</a>`);
    const link = screen.getByRole('link', { name: 'somewhere' });

    expect(link).toHaveAttribute('target', '_blank');
    expect(link.getAttribute('rel')).toContain('noopener');
    expect(link.getAttribute('rel')).toContain('noreferrer');
  });

  test.each(offOriginAnchors)('does not route %j through in-app navigation — %s', async (href) => {
    const user = userEvent.setup();
    renderAt(`<a href="${href}">somewhere</a>`);

    await user.click(screen.getByRole('link', { name: 'somewhere' }));

    // An in-app link would have moved the router here. These must not.
    expect(screen.getByTestId('current-path')).toHaveTextContent('/boards/demo');
  });

  test('a backslash written in markdown link syntax stays on this origin', () => {
    // Markdown encodes the backslash in a link destination, so this shape never
    // reaches the renderer intact by that route. Pinned because it is the
    // reason the cases above are written as HTML: if this encoding ever stops
    // happening, markdown link syntax becomes another way in and this test is
    // where that shows up.
    renderAt('[somewhere](/\\elsewhere.example/x)');
    const href = screen.getByRole('link', { name: 'somewhere' }).getAttribute('href') ?? '';
    expect(new URL(href, window.location.href).origin).toBe(window.location.origin);
  });

  test('still routes a genuine in-app link through the router', async () => {
    const user = userEvent.setup();
    renderAt('See #28');

    const link = screen.getByRole('link', { name: '#28' });
    expect(link).not.toHaveAttribute('target');

    await user.click(link);
    expect(screen.getByTestId('current-path')).toHaveTextContent('/boards/demo/cards/28');
  });

  test('an in-app link keeps the attributes the markdown gave it', () => {
    // The in-app branch used to drop everything except the destination, so a
    // link title written in the markdown never reached the rendered anchor.
    renderAt('[home](/boards/demo "Back to the board")');
    expect(screen.getByRole('link', { name: 'home' })).toHaveAttribute(
      'title',
      'Back to the board',
    );
  });

  test('routes a scheme-relative link back to this origin through the router', async () => {
    // `//this-origin/path` resolves to our own origin, so it is an in-app link —
    // but it reads as absolute to the plugin that decorates external links, and
    // a router link carrying `target` is one the router declines to intercept.
    // The decoration has to be dropped, or an in-app link full-page-reloads.
    const user = userEvent.setup();
    renderAt(`<a href="//${window.location.host}/boards/demo/cards/28">sneaky</a>`);

    const link = screen.getByRole('link', { name: 'sneaky' });
    // The resolved path, which is what says the in-app branch handled it.
    expect(link).toHaveAttribute('href', '/boards/demo/cards/28');
    expect(link).not.toHaveAttribute('target');
    expect(link).not.toHaveAttribute('rel');

    await user.click(link);
    expect(screen.getByTestId('current-path')).toHaveTextContent('/boards/demo/cards/28');
  });

  test('leaves a link to another site working as it always has', () => {
    renderAt('[docs](https://example.com/docs)');
    const link = screen.getByRole('link', { name: 'docs' });
    expect(link).toHaveAttribute('href', 'https://example.com/docs');
    expect(link).toHaveAttribute('target', '_blank');
    expect(link.getAttribute('rel')).toContain('noopener');
  });

  test('does not open a blank tab for a mail link', () => {
    renderAt('[write](mailto:someone@example.com)');
    const link = screen.getByRole('link', { name: 'write' });
    expect(link).toHaveAttribute('href', 'mailto:someone@example.com');
    expect(link).not.toHaveAttribute('target');
  });

  test('never renders a script-scheme link as a working destination', () => {
    // The sanitiser drops these, and has all along — pinned here because it is
    // the other half of what makes a link in someone else's markdown safe to
    // render, and nothing else in this suite was holding it.
    const trap = renderAt('[trap](javascript:alert(1))');
    expect(screen.getByText('trap')).toBeInTheDocument();
    expect(trap.container.querySelectorAll('a[href]')).toHaveLength(0);
    trap.unmount();

    // Control: the same assertion does find a destination for an ordinary link,
    // so the one above is measuring something.
    const ordinary = renderAt('[fine](https://example.com)');
    expect(ordinary.container.querySelectorAll('a[href]')).toHaveLength(1);
  });
});

describe('MarkdownRenderer anchor attributes', () => {
  // react-markdown hands every component the parsed markdown node alongside the
  // element's own attributes. It is not an HTML attribute, and forwarding the
  // props object wholesale writes it into the DOM as `node="[object Object]"` —
  // silently, because React passes an unknown lowercase attribute straight
  // through without a warning.
  const withAnchors: Array<[string, string]> = [
    ['an in-app card link', 'See #28'],
    ['an in-app link with a title', '[home](/boards/demo "Back to the board")'],
    ['a link to another site', '[docs](https://example.com/docs)'],
    ['a mail link', '[write](mailto:someone@example.com)'],
    ['the anchors GFM generates for a footnote', 'Text with a note[^1]\n\n[^1]: the note body\n'],
  ];

  test.each(withAnchors)('renders no react-markdown internals on %s', (_label, markdown) => {
    const { container } = render(
      <MemoryRouter>
        <MarkdownRenderer boardSlug="demo" cardNumbers={new Set([28])}>
          {markdown}
        </MarkdownRenderer>
      </MemoryRouter>,
    );

    // Control: this case does render an anchor, so the assertion below is
    // measuring something rather than passing over an empty document.
    expect(container.querySelectorAll('a').length).toBeGreaterThan(0);
    expect(container.querySelectorAll('[node]')).toHaveLength(0);
  });

  test('renders no react-markdown internals on a fenced code block', () => {
    // The other element this renderer takes over. Same defect, same fix — worth
    // its own case because the anchors above would not have caught it.
    const { container } = render(
      <MemoryRouter>
        <MarkdownRenderer>{'```js\nconst x = 1;\n```'}</MarkdownRenderer>
      </MemoryRouter>,
    );

    // Control: the block rendered, so the assertion below has something to see.
    expect(container.querySelector('pre')).not.toBeNull();
    expect(container.querySelectorAll('[node]')).toHaveLength(0);
  });

  test('still renders the attributes an author or a plugin put on the anchor', () => {
    // The other half of the same change: dropping react-markdown's own prop
    // must not take the anchor's real attributes with it.
    const { container } = render(
      <MemoryRouter>
        <MarkdownRenderer>{'Text with a note[^1]\n\n[^1]: the note body\n'}</MarkdownRenderer>
      </MemoryRouter>,
    );

    const backref = container.querySelector('a[data-footnote-backref]');
    expect(backref).not.toBeNull();
    expect(backref).toHaveAttribute('aria-label');
    expect(backref).toHaveClass('data-footnote-backref');
  });
});

describe('MarkdownRenderer card-link hover preview (#283)', () => {
  // The preview reads from cache (no fetch-on-hover) via a cardPreviews map keyed
  // by card number. The link still renders as a router <Link>; hovering/focusing
  // it opens a Base UI PreviewCard with the card summary.
  const slug = 'demo';
  const liveCards = new Set([28]);

  function makeCard(overrides: Partial<CardSummary> = {}): CardSummary {
    return {
      id: 'card-28',
      number: 28,
      name: 'A previewed card',
      descriptionMarkdown: 'desc',
      sizeId: 'size-1',
      sizeName: 'L',
      laneId: 'lane-1',
      position: 0,
      isArchived: false,
      createdByUserId: 'u1',
      createdAtUtc: '2026-01-01T00:00:00Z',
      lastUpdatedByUserId: 'u1',
      lastUpdatedAtUtc: '2026-01-01T00:00:00Z',
      labels: [],
      commentCount: 0,
      attachmentCount: 0,
      ...overrides,
    };
  }

  function renderWithPreview(
    markdown: string,
    previews: Map<number, CardLinkPreviewData> = new Map([
      [28, { card: makeCard(), laneName: 'Backlog' }],
    ]),
  ) {
    return render(
      <MemoryRouter>
        <MarkdownRenderer boardSlug={slug} cardNumbers={liveCards} cardPreviews={previews}>
          {markdown}
        </MarkdownRenderer>
      </MemoryRouter>,
    );
  }

  test('still renders the card reference as a navigable router link', () => {
    renderWithPreview('See #28 for context');
    const link = screen.getByRole('link', { name: '#28' });
    expect(link).toHaveAttribute('href', '/boards/demo/cards/28');
    expect(link).not.toHaveAttribute('target');
  });

  test('opens a card preview on hover', async () => {
    const user = userEvent.setup();
    renderWithPreview('See #28 for context');

    await user.hover(screen.getByRole('link', { name: '#28' }));

    await waitFor(() => {
      expect(screen.getByText('A previewed card')).toBeInTheDocument();
    });
    expect(screen.getByText('Backlog')).toBeInTheDocument();
    expect(screen.getByText('L')).toBeInTheDocument();
  });

  test('opens the preview on keyboard focus (accessibility)', async () => {
    const user = userEvent.setup();
    renderWithPreview('See #28 for context');

    await user.tab();
    expect(screen.getByRole('link', { name: '#28' })).toHaveFocus();

    await waitFor(() => {
      expect(screen.getByText('A previewed card')).toBeInTheDocument();
    });
  });

  test('renders a plain link with no preview when preview data is absent', () => {
    // Empty map — the #28 link still renders, but no PreviewCard wraps it.
    renderWithPreview('See #28', new Map());
    const link = screen.getByRole('link', { name: '#28' });
    expect(link).toHaveAttribute('href', '/boards/demo/cards/28');
    // No preview content present.
    expect(screen.queryByText('A previewed card')).toBeNull();
  });
});
