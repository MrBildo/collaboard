import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Check, Tag } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Input } from '@/components/ui/input';
import { getContrastColor, getReadableColor } from '@/lib/utils';
import type { Label } from '@/types';

type LabelPickerProps = {
  allLabels: Label[];
  assignedLabels: Label[];
  onAdd: (labelId: string) => void;
  onRemove: (labelId: string) => void;
};

export function LabelPicker({ allLabels, assignedLabels, onAdd, onRemove }: LabelPickerProps) {
  const [open, setOpen] = useState(false);
  const [filter, setFilter] = useState('');
  const containerRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  const assignedIds = useMemo(() => new Set(assignedLabels.map((l) => l.id)), [assignedLabels]);

  const filtered = useMemo(
    () =>
      filter
        ? allLabels.filter((l) => l.name.toLowerCase().includes(filter.toLowerCase()))
        : allLabels,
    [allLabels, filter],
  );

  const toggle = useCallback(
    (id: string) => {
      if (assignedIds.has(id)) {
        onRemove(id);
      } else {
        onAdd(id);
      }
    },
    [assignedIds, onAdd, onRemove],
  );

  useEffect(() => {
    if (open) {
      setFilter('');
      setTimeout(() => inputRef.current?.focus(), 0);
    }
  }, [open]);

  useEffect(() => {
    if (!open) return;
    function handleClick(e: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setOpen(false);
      }
    }
    document.addEventListener('mousedown', handleClick);
    return () => document.removeEventListener('mousedown', handleClick);
  }, [open]);

  useEffect(() => {
    if (!open) return;
    function handleKey(e: KeyboardEvent) {
      if (e.key === 'Escape') setOpen(false);
    }
    document.addEventListener('keydown', handleKey);
    return () => document.removeEventListener('keydown', handleKey);
  }, [open]);

  return (
    <div ref={containerRef} className="relative">
      <Button
        variant="outline"
        size="sm"
        className="h-auto min-h-8 gap-1.5 px-2.5 py-1"
        onClick={() => setOpen(!open)}
      >
        <Tag className="w-3.5 h-3.5 shrink-0 text-muted-foreground" />
        {assignedLabels.length > 0 ? (
          <span className="flex flex-wrap gap-1">
            {assignedLabels.map((label) => (
              <Badge
                key={label.id}
                variant="secondary"
                className="rounded-full px-1.5 py-0 text-[10px] leading-4"
                style={{
                  backgroundColor: label.color ?? '#6b7280',
                  color: getContrastColor(label.color),
                  borderColor: label.color ?? '#6b7280',
                }}
              >
                {label.name}
              </Badge>
            ))}
          </span>
        ) : (
          <span className="text-muted-foreground">Labels</span>
        )}
      </Button>

      {open && (
        <div className="absolute top-full left-0 z-50 mt-1 min-w-[12rem] overflow-hidden rounded-lg border border-border bg-popover text-popover-foreground shadow-md">
          <div className="p-1">
            <Input
              ref={inputRef}
              value={filter}
              onChange={(e) => setFilter(e.target.value)}
              placeholder="Search labels..."
              className="h-7 text-sm"
            />
          </div>
          <div className="max-h-48 overflow-y-auto p-1">
            {filtered.map((label) => {
              const selected = assignedIds.has(label.id);
              return (
                <button
                  key={label.id}
                  type="button"
                  onClick={() => toggle(label.id)}
                  className="relative flex w-full cursor-default items-center gap-2 rounded-md py-1.5 pr-8 pl-2 text-sm outline-none hover:bg-accent hover:text-accent-foreground"
                >
                  <span
                    className="size-3 shrink-0 rounded-full border"
                    style={{
                      backgroundColor: label.color ?? '#6b7280',
                      borderColor: getReadableColor(label.color),
                    }}
                  />
                  {label.name}
                  {selected && (
                    <Check className="absolute right-2 h-4 w-4" />
                  )}
                </button>
              );
            })}
            {filtered.length === 0 && (
              <p className="py-2 text-center text-sm text-muted-foreground">No labels found.</p>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
