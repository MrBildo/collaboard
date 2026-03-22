import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useClickOutside } from '@/hooks/use-click-outside';
import { Check, Tag } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Input } from '@/components/ui/input';
import { cn, getContrastColor, getReadableColor } from '@/lib/utils';
import type { Label } from '@/types';

type LabelPickerProps = {
  allLabels: Label[];
  assignedLabels: Label[];
  onAdd: (labelId: string) => void;
  onRemove: (labelId: string) => void;
};

export function LabelPicker({ allLabels, assignedLabels, onAdd, onRemove }: LabelPickerProps) {
  const [isOpen, setIsOpen] = useState(false);
  const [filter, setFilter] = useState('');
  const [focusedIndex, setFocusedIndex] = useState(-1);
  const containerRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLDivElement>(null);

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

  const handleFilterChange = useCallback((value: string) => {
    setFilter(value);
    setFocusedIndex(-1);
  }, []);

  useEffect(() => {
    if (focusedIndex >= 0 && listRef.current) {
      const items = listRef.current.querySelectorAll('[role="option"]');
      if (items[focusedIndex]) {
        (items[focusedIndex] as HTMLElement).scrollIntoView({ block: 'nearest' });
      }
    }
  }, [focusedIndex]);

  const handleOpen = () => {
    if (!isOpen) {
      setFilter('');
      setFocusedIndex(-1);
      setTimeout(() => inputRef.current?.focus(), 0);
    }
    setIsOpen(!isOpen);
  };

  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === 'ArrowDown') {
        e.preventDefault();
        setFocusedIndex((prev) => Math.min(prev + 1, filtered.length - 1));
      } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        setFocusedIndex((prev) => Math.max(prev - 1, 0));
      } else if ((e.key === 'Enter' || e.key === ' ') && focusedIndex >= 0) {
        e.preventDefault();
        toggle(filtered[focusedIndex].id);
      } else if (e.key === 'Escape') {
        e.preventDefault();
        setIsOpen(false);
      }
    },
    [filtered, focusedIndex, toggle],
  );

  const handleClose = useCallback(() => setIsOpen(false), []);
  useClickOutside(containerRef, handleClose);

  return (
    <div ref={containerRef} className="relative" aria-label="Label picker">
      <Button
        variant="outline"
        size="sm"
        className="h-auto min-h-8 gap-1.5 px-2.5 py-1"
        onClick={handleOpen}
        aria-expanded={isOpen}
        aria-haspopup="listbox"
      >
        <Tag className="w-3.5 h-3.5 shrink-0 text-muted-foreground" />
        {assignedLabels.length > 0 ? (
          <span className="flex flex-wrap gap-1">
            {assignedLabels.map((label) => (
              <Badge
                key={label.id}
                variant="secondary"
                className="rounded-sm px-1.5 py-0 text-xs leading-4"
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

      {isOpen && (
        <div
          className="absolute top-full left-0 z-50 mt-1 min-w-[12rem] overflow-hidden rounded-lg border border-border bg-popover text-popover-foreground shadow-md"
          onKeyDown={handleKeyDown}
        >
          <div className="p-1">
            <Input
              ref={inputRef}
              value={filter}
              onChange={(e) => handleFilterChange(e.target.value)}
              placeholder="Search labels..."
              className="h-7 text-sm"
              aria-label="Search labels"
              role="combobox"
              aria-expanded={true}
              aria-controls="label-picker-listbox"
              aria-activedescendant={
                focusedIndex >= 0 ? `label-option-${filtered[focusedIndex]?.id}` : undefined
              }
            />
          </div>
          <div
            ref={listRef}
            className="max-h-48 overflow-y-auto p-1"
            role="listbox"
            id="label-picker-listbox"
            aria-label="Available labels"
          >
            {filtered.map((label, index) => {
              const selected = assignedIds.has(label.id);
              const focused = index === focusedIndex;
              return (
                <Button
                  key={label.id}
                  id={`label-option-${label.id}`}
                  variant="ghost"
                  size="sm"
                  role="option"
                  aria-selected={selected}
                  onClick={() => toggle(label.id)}
                  className={cn(
                    'relative w-full justify-start gap-2 pr-8',
                    focused && 'bg-accent text-accent-foreground',
                  )}
                >
                  <span
                    className="size-3 shrink-0 rounded-full border"
                    style={{
                      backgroundColor: label.color ?? '#6b7280',
                      borderColor: getReadableColor(label.color),
                    }}
                  />
                  {label.name}
                  {selected && <Check className="absolute right-2 h-4 w-4" />}
                </Button>
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
