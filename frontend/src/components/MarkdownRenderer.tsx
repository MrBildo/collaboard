import { isValidElement, type ReactNode } from 'react';
import ReactMarkdown, { type Components } from 'react-markdown';
import rehypeRaw from 'rehype-raw';
import rehypeSanitize from 'rehype-sanitize';
import remarkGfm from 'remark-gfm';
import { MermaidBlock } from '@/components/MermaidBlock';

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
      remarkPlugins={[remarkGfm]}
      rehypePlugins={[rehypeRaw, rehypeSanitize]}
      components={markdownComponents}
    >
      {children}
    </ReactMarkdown>
  );
}
