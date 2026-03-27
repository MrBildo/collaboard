import { Badge } from '@/components/ui/badge';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip';
import type { CardItem } from '@/types';

type CardOverlayProps = {
  card: CardItem;
  sizeMap: Map<string, string>;
};

export function CardOverlay({ card, sizeMap }: CardOverlayProps) {
  const sizeName = sizeMap.get(card.sizeId) ?? '?';
  const sizeDisplay = sizeName.length <= 3 ? sizeName : sizeName[0].toUpperCase();

  return (
    <div className="overflow-hidden rounded-lg border border-border bg-card p-3 shadow-xl">
      <div className="flex items-start justify-between gap-2">
        <h3 className="min-w-0 text-sm font-medium leading-snug break-words">{card.name}</h3>
        <Tooltip>
          <TooltipTrigger render={<span />}>
            <Badge variant="outline" className="mt-0.5 shrink-0 text-xs">
              {sizeDisplay}
            </Badge>
          </TooltipTrigger>
          <TooltipContent>{sizeName}</TooltipContent>
        </Tooltip>
      </div>
      <div className="mt-2 text-xs text-muted-foreground">
        <span>#{card.number}</span>
      </div>
    </div>
  );
}
