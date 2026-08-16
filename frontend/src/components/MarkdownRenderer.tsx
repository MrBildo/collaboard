import {
  createContext,
  isValidElement,
  useContext,
  useMemo,
  type ComponentPropsWithoutRef,
  type ReactNode,
} from 'react';
import ReactMarkdown, { type Components, type ExtraProps, type Options } from 'react-markdown';
import { Link } from 'react-router-dom';
import rehypeExternalLinks from 'rehype-external-links';
import rehypeHighlight from 'rehype-highlight';
import rehypeRaw from 'rehype-raw';
import rehypeSanitize from 'rehype-sanitize';
import remarkEmoji from 'remark-emoji';
import remarkGfm from 'remark-gfm';
import { CardLinkPreview, type CardLinkPreviewData } from '@/components/CardLinkPreview';
import { MermaidBlock } from '@/components/MermaidBlock';
import { PreviewCard, PreviewCardContent, PreviewCardTrigger } from '@/components/ui/preview-card';
import { findInternalPath, isCrossOriginHttpHref } from '@/lib/internal-href';
import { remarkCardLinks } from '@/lib/remark-card-links';
import '@/styles/highlight.css';

type MarkdownRendererProps = {
  children: string;
  // Card-link autolinking context. Both must be present to enable
  // `#NNN` → card-link rewriting; omit them and the renderer behaves exactly as
  // before (plain markdown, no autolinking — e.g. board-less render contexts).
  boardSlug?: string;
  cardNumbers?: Set<number>;
  // Per-card preview data for the hover/focus card-link preview,
  // keyed by card number. Sourced from the same board-data cache the autolink
  // gate uses, so no fetch-on-hover. Omit (or omit a given number) and the
  // `#NNN` link renders with no preview — never a hanging or empty tooltip.
  cardPreviews?: Map<number, CardLinkPreviewData>;
  // Render mermaid fences as plain code blocks (the diagram source) instead of
  // executing them as diagrams. The description-history view sets this: a
  // rendered diagram is injected as raw SVG, which bypasses both the markdown
  // sanitizer and the link-origin check, and a history view resurrects text
  // that may have been removed precisely because it was bad — then renders one
  // block per revision. Showing the fence's source is the honest snapshot.
  suppressDiagrams?: boolean;
};

// The anchor renderer (markdownComponents.a) is fixed at module scope, but it
// needs the per-render preview map. A context bridges that without rebuilding
// the components object on every render.
const CardPreviewsContext = createContext<Map<number, CardLinkPreviewData> | undefined>(undefined);

// Same bridge for the diagram-suppression flag: the `pre` renderer is fixed at
// module scope but the choice is per-render.
const DiagramsSuppressedContext = createContext(false);

// An internal card link is `/boards/{slug}/cards/{n}`. Pull the trailing card
// number so the anchor can look up its preview data. Returns null for any other
// internal path (no preview, link renders normally).
function cardNumberFromPath(path: string): number | null {
  const match = /\/cards\/(\d+)$/.exec(path);
  if (!match) return null;
  return Number(match[1]);
}

function findMermaidCode(children: ReactNode): string | null {
  if (!isValidElement(children)) return null;
  const props = children.props as { className?: string; children?: ReactNode };
  if (props.className === 'language-mermaid') {
    return String(props.children).replace(/\n$/, '');
  }
  return null;
}

// Card descriptions and comments are markdown that anyone with an account — or
// any agent writing on their behalf — can author, so which links this app is
// willing to follow itself is a decision about untrusted input.
//
// A link becomes client-side navigation only when it resolves to this app's own
// origin, and the destination that gets navigated to is the resolved one, so it
// is the value whose origin was actually checked. Everything else is an
// external link: it renders as an ordinary anchor, and if it leaves this origin
// it gets `target="_blank"` and `rel="noopener noreferrer"` here, at render
// time. The markdown plugin that normally adds those cannot make that call —
// it works on link text during the build and has no idea which origin the page
// is served from, which is exactly how a link like `/\elsewhere.example` slipped
// past it while also being mistaken for an internal one.
//
// Four of the props this receives belong to the renderer rather than to the
// rendered link, and each is taken out of the author's attributes deliberately:
// `node` is react-markdown's handle on the parsed markdown and is not an HTML
// attribute at all (React would write it into the DOM as `[object Object]`
// without complaining, since it does not recognise it); `href` is replaced by
// the resolved path on an in-app link; and `target`/`rel` are decided here,
// because the plugin that adds them cannot tell a link back to this origin from
// a link away, so its decoration must never ride along on in-app navigation —
// a router link carrying a `target` is one the router declines to intercept,
// which quietly turns client-side navigation into a full page load. Everything
// left over is the author's: a link title, the aria and footnote attributes
// generated for GFM footnotes, whatever else survived the sanitiser.
function MarkdownAnchor({
  // eslint-disable-next-line @typescript-eslint/no-unused-vars -- named so that it is left out of the attributes below
  node,
  href,
  target,
  rel,
  children,
  ...authorAttributes
}: ComponentPropsWithoutRef<'a'> & ExtraProps) {
  const cardPreviews = useContext(CardPreviewsContext);
  const internalPath = findInternalPath(href);

  if (internalPath !== null) {
    const cardNumber = cardNumberFromPath(internalPath);
    const preview = cardNumber !== null ? cardPreviews?.get(cardNumber) : undefined;

    // No preview data (board-less render, cache miss, or a non-card internal
    // link) — render the plain router link, no popup.
    if (!preview) {
      return (
        <Link to={internalPath} {...authorAttributes}>
          {children}
        </Link>
      );
    }

    return (
      <PreviewCard>
        <PreviewCardTrigger render={<Link to={internalPath} {...authorAttributes} />}>
          {children}
        </PreviewCardTrigger>
        <PreviewCardContent>
          <CardLinkPreview data={preview} />
        </PreviewCardContent>
      </PreviewCard>
    );
  }

  // An ordinary anchor keeps whatever decoration it arrived with — that is what
  // the plugin is for — except where this link demonstrably leaves the origin,
  // where the protections are set here rather than inferred from the shape of
  // the href.
  const offOrigin = isCrossOriginHttpHref(href);

  return (
    <a
      {...authorAttributes}
      href={href}
      target={offOrigin ? '_blank' : target}
      rel={offOrigin ? 'noopener noreferrer' : rel}
    >
      {children}
    </a>
  );
}

function MarkdownPre({
  // eslint-disable-next-line @typescript-eslint/no-unused-vars -- `node` is named so that it is left out of `props`; it is the renderer's own handle on the parsed markdown, not an attribute of the block being rendered
  node,
  children: preChildren,
  ...props
}: ComponentPropsWithoutRef<'pre'> & ExtraProps) {
  const diagramsSuppressed = useContext(DiagramsSuppressedContext);
  const mermaidCode = findMermaidCode(preChildren);
  if (mermaidCode !== null && !diagramsSuppressed) {
    return <MermaidBlock>{mermaidCode}</MermaidBlock>;
  }
  // A suppressed mermaid fence falls through here and renders as an ordinary
  // code block — the diagram's source, visible and inert.
  return <pre {...props}>{preChildren}</pre>;
}

const markdownComponents: Components = {
  pre: MarkdownPre,
  a: MarkdownAnchor,
};

type RemarkPlugins = NonNullable<Options['remarkPlugins']>;

export function MarkdownRenderer({
  children,
  boardSlug,
  cardNumbers,
  cardPreviews,
  suppressDiagrams = false,
}: MarkdownRendererProps) {
  const remarkPlugins = useMemo<RemarkPlugins>(() => {
    const plugins: RemarkPlugins = [remarkGfm, remarkEmoji];
    if (boardSlug && cardNumbers) {
      plugins.push([remarkCardLinks, { boardSlug, cardNumbers }]);
    }
    return plugins;
  }, [boardSlug, cardNumbers]);

  return (
    <CardPreviewsContext.Provider value={cardPreviews}>
      <DiagramsSuppressedContext.Provider value={suppressDiagrams}>
        <ReactMarkdown
          remarkPlugins={remarkPlugins}
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
      </DiagramsSuppressedContext.Provider>
    </CardPreviewsContext.Provider>
  );
}
