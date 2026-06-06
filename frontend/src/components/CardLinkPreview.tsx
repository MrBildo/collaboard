import { MessageSquare, Paperclip } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { getContrastColor } from '@/lib/utils';
import type { CardSummary } from '@/types';

// Data the preview renders. Sourced entirely from the board-data cache the
// autolink (#273) already subscribes to — no fetch-on-hover (card #283).
export type CardLinkPreviewData = {
  card: CardSummary;
  laneName: string;
};

type CardLinkPreviewProps = {
  data: CardLinkPreviewData;
};

// A compact at-a-glance preview of a same-board card, shown inside the
// PreviewCard popup when a `#NN` autolink is hovered/focused. Reuses the
// CardSummary shape and the board-card visual vocabulary (SortableCard):
// outline size badge, secondary label chips, muted comment/attachment counts.
export function CardLinkPreview({ data }: CardLinkPreviewProps) {
  const { card, laneName } = data;

  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-start justify-between gap-2">
        <h3 className="min-w-0 text-sm font-medium leading-snug break-words">{card.name}</h3>
        <Badge variant="outline" className="mt-0.5 shrink-0 text-xs">
          {card.sizeName}
        </Badge>
      </div>

      <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground">
        <span className="font-medium">#{card.number}</span>
        <span className="truncate">{laneName}</span>

        {card.commentCount > 0 && (
          <span className="flex items-center gap-0.5">
            <MessageSquare className="h-3.5 w-3.5" />
            {card.commentCount}
          </span>
        )}

        {card.attachmentCount > 0 && (
          <span className="flex items-center gap-0.5">
            <Paperclip className="h-3.5 w-3.5" />
            {card.attachmentCount}
          </span>
        )}
      </div>

      {card.labels.length > 0 && (
        <div className="flex flex-wrap items-center gap-1">
          {card.labels.map((label) => (
            <Badge
              key={label.id}
              variant="secondary"
              className="max-w-full rounded-sm text-xs"
              style={
                label.color
                  ? { backgroundColor: label.color, color: getContrastColor(label.color) }
                  : undefined
              }
            >
              {label.name}
            </Badge>
          ))}
        </div>
      )}
    </div>
  );
}
