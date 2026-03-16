import { useSortable } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { useQuery } from '@tanstack/react-query';
import { Badge } from '@/components/ui/badge';
import { fetchCardAttachments, fetchCardLabels, fetchComments } from '@/lib/api';
import { QUERY_DEFAULTS } from '@/lib/query-config';
import { queryKeys } from '@/lib/query-keys';
import { cn, getContrastColor } from '@/lib/utils';
import type { CardItem, CardSummary } from '@/types';

type SortableCardProps = {
  card: CardItem;
  onCardClick: (card: CardItem) => void;
  isDragging: boolean;
  sizeMap: Map<string, string>;
  enrichedData?: CardSummary;
};

export function SortableCard({
  card,
  onCardClick,
  isDragging,
  sizeMap,
  enrichedData,
}: SortableCardProps) {
  const { attributes, listeners, setNodeRef, transform, transition } = useSortable({ id: card.id });
  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
  };
  const labelsQuery = useQuery({
    queryKey: queryKeys.cards.labels(card.id),
    queryFn: () => fetchCardLabels(card.id),
    enabled: !enrichedData,
    ...QUERY_DEFAULTS.labels,
  });
  const commentsQuery = useQuery({
    queryKey: queryKeys.cards.comments(card.id),
    queryFn: () => fetchComments(card.id),
    enabled: !enrichedData,
    ...QUERY_DEFAULTS.comments,
  });
  const attachmentsQuery = useQuery({
    queryKey: queryKeys.cards.attachments(card.id),
    queryFn: () => fetchCardAttachments(card.id),
    enabled: !enrichedData,
    ...QUERY_DEFAULTS.attachments,
  });
  const labels = enrichedData?.labels ?? labelsQuery.data ?? [];
  const commentCount = enrichedData?.commentCount ?? commentsQuery.data?.length ?? 0;
  const attachmentCount = enrichedData?.attachmentCount ?? attachmentsQuery.data?.length ?? 0;

  return (
    <div
      ref={setNodeRef}
      style={style}
      {...listeners}
      {...attributes}
      onClick={() => onCardClick(card)}
      className={cn(
        'cursor-pointer rounded-lg border border-border bg-card p-3 shadow-sm transition-shadow hover:shadow-md hover:border-primary/30',
        isDragging && 'opacity-0',
      )}
    >
      <div className="flex items-start justify-between gap-2">
        <h3 className="text-sm font-medium leading-snug">{card.name}</h3>
        <Badge variant="outline" className="mt-0.5 shrink-0 text-[10px]">{sizeMap.get(card.sizeId) ?? '?'}</Badge>
      </div>

      <div className="mt-2 flex items-center gap-3 text-xs text-muted-foreground">
        <span>#{card.number}</span>

        {commentCount > 0 && (
          <span className="flex items-center gap-0.5" title={`${commentCount} comments`}>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
            </svg>
            {commentCount}
          </span>
        )}

        {attachmentCount > 0 && (
          <span className="flex items-center gap-0.5" title={`${attachmentCount} attachments`}>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <path d="M21.44 11.05l-9.19 9.19a6 6 0 0 1-8.49-8.49l9.19-9.19a4 4 0 0 1 5.66 5.66l-9.2 9.19a2 2 0 0 1-2.83-2.83l8.49-8.48" />
            </svg>
            {attachmentCount}
          </span>
        )}

      </div>

      {labels.length > 0 && (
        <div className="mt-2 flex flex-wrap gap-1">
          {labels.map((label) => (
            <Badge
              key={label.id}
              variant="secondary"
              className="text-[10px]"
              style={label.color ? { backgroundColor: label.color, color: getContrastColor(label.color) } : undefined}
            >
              {label.name}
            </Badge>
          ))}
        </div>
      )}
    </div>
  );
}
