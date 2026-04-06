import { isValidElement, type ReactNode } from 'react';
import ReactMarkdown, { type Components } from 'react-markdown';
import rehypeExternalLinks from 'rehype-external-links';
import rehypeHighlight from 'rehype-highlight';
import rehypeRaw from 'rehype-raw';
import rehypeSanitize from 'rehype-sanitize';
import remarkEmoji from 'remark-emoji';
import remarkGfm from 'remark-gfm';
import { MermaidBlock } from '@/components/MermaidBlock';
import '@/styles/highlight.css';

type MarkdownRendererProps = {
  children: string;
};

function getMermaidCode(children: ReactNode): string | null {
  if (!isValidElement(children)) return null;
  const props = children.props as { className?: string; children?: ReactNode };
  if (props.className === 'language-mermaid') {
    return String(props.children).replace(/\n$/, '');
  }
  return null;
}

const markdownComponents: Components = {
  pre({ children: preChildren, ...props }) {
    const mermaidCode = getMermaidCode(preChildren);
    if (mermaidCode !== null) {
      return <MermaidBlock>{mermaidCode}</MermaidBlock>;
    }
    return <pre {...props}>{preChildren}</pre>;
  },
};

export function MarkdownRenderer({ children }: MarkdownRendererProps) {
  return (
    <ReactMarkdown
      remarkPlugins={[remarkGfm, remarkEmoji]}
      rehypePlugins={[
        rehypeRaw,
        rehypeSanitize,
        [rehypeHighlight, { plainText: ['mermaid'] }],
        [rehypeExternalLinks, { target: '_blank', rel: ['noopener', 'noreferrer'] }],
      ]}
      components={markdownComponents}
    >
      {children}
    </ReactMarkdown>
  );
}
