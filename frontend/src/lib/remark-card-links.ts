import { findAndReplace } from 'mdast-util-find-and-replace';
import type { Link, Root, Text } from 'mdast';

// Autolinks `#NNN` card references to their card-detail route (card #273).
//
// Mechanism: a remark (mdast) plugin running before any rehype phase. Operating
// on the parse tree gives us the two hardest false-positive suppressions for
// free: fenced and inline code hold their content in a `value` string (not child
// `Text` nodes), so the visitor never descends into them. Existing markdown
// links DO carry `Text` children, so we explicitly `ignore` them — matching how
// GFM's own autolink-literal extension suppresses re-linking inside links.
//
// Scope is "live-only" (card #273, fork 3a): a `#N` is linkified only when `N`
// is a known non-archived card number on the current board. A reference to an
// archived or nonexistent card stays plain text — no dangling links, no 404s.

// `#` then digits, with no trailing word char or hyphen. The trailing boundary
// rejects hex colors (`#28a745`), heading-anchor fragments (`#28-foo`), and any
// `#word` shape; the digits-only body rejects `#section` and bare `#`.
const CARD_REF = /#(\d+)(?![\w-])/g;

export type RemarkCardLinksOptions = {
  // Board slug for the relative href `/boards/{slug}/cards/{n}`. Relative by
  // design: rehype-external-links only rewrites absolute/`//` hrefs, so an
  // internal link never gets `target="_blank"`.
  boardSlug: string;
  // Live (non-archived) card numbers on the board. Membership gates linkifying.
  cardNumbers: Set<number>;
};

export function remarkCardLinks({ boardSlug, cardNumbers }: RemarkCardLinksOptions) {
  return (tree: Root) => {
    findAndReplace(
      tree,
      [
        CARD_REF,
        (match: string, digits: string): Link | false => {
          const cardNumber = Number(digits);
          if (!cardNumbers.has(cardNumber)) {
            // Not a known live card — leave the literal text untouched.
            return false;
          }
          const linkText: Text = { type: 'text', value: match };
          return {
            type: 'link',
            url: `/boards/${boardSlug}/cards/${cardNumber}`,
            children: [linkText],
          };
        },
      ],
      { ignore: ['link', 'linkReference'] },
    );
  };
}
