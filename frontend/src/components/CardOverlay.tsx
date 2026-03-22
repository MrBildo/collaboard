import { Badge } from '@/components/ui/badge';
import type { CardItem } from '@/types';

type CardOverlayProps = {
  card: CardItem;
  sizeMap: Map<string, string>;
};

export function CardOverlay({ card, sizeMap }: CardOverlayProps) {
  return (
    <div className="overflow-hidden rounded-lg border border-border bg-card p-3 shadow-xl">
      <div className="flex items-start justify-between gap-2">
        <h3 className="min-w-0 text-sm font-medium leading-snug break-words">{card.name}</h3>
        <Badge variant="outline" className="mt-0.5 max-w-[6rem] shrink-0 justify-start text-xs">
          {sizeMap.get(card.sizeId) ?? '?'}
        </Badge>
      </div>
      <div className="mt-2 text-xs text-muted-foreground">
        <span>#{card.number}</span>
      </div>
    </div>
  );
}
