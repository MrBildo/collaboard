import { describe, test, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
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

  test('renders fenced code blocks as code elements', () => {
    const md = '```js\nconst x = 1;\n```';
    render(<MarkdownRenderer>{md}</MarkdownRenderer>);
    expect(screen.getByText('const x = 1;')).toBeInTheDocument();
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
});
