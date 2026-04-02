import type React from 'react';
import { cn } from '@/lib/utils';

type AttachmentRowProps = {
  fileName: string;
  metadata: React.ReactNode;
  actions?: React.ReactNode;
  icon?: React.ReactNode;
  variant?: 'default' | 'error';
};

export function AttachmentRow({
  fileName,
  metadata,
  actions,
  icon,
  variant = 'default',
}: AttachmentRowProps) {
  return (
    <div
      className={cn(
        'flex items-center justify-between rounded-lg border p-3',
        variant === 'error' ? 'border-destructive/30 bg-destructive/5' : 'bg-muted/30',
      )}
    >
      <div className={cn('min-w-0 flex-1', icon ? 'flex items-center gap-2' : '')}>
        {icon}
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-medium">{fileName}</p>
          <p className="text-xs text-muted-foreground">{metadata}</p>
        </div>
      </div>
      {actions && <div className="ml-2 flex shrink-0 gap-1">{actions}</div>}
    </div>
  );
}
