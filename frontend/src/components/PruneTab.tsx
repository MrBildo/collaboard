import { useCallback, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import { Separator } from '@/components/ui/separator';
import { useClickOutside } from '@/hooks/use-click-outside';
import { Archive, Search, Trash2, Check, AlertTriangle } from 'lucide-react';
import { fetchLabels, fetchLanes, pruneCards, prunePreview } from '@/lib/api';
import { queryKeys } from '@/lib/query-keys';
import { QUERY_DEFAULTS } from '@/lib/query-config';
import { cn, formatDateTime } from '@/lib/utils';
import type { PruneFilters, PrunePreviewResponse } from '@/types';

const PRESETS = [
  { label: '7d', days: 7 },
  { label: '30d', days: 30 },
  { label: '90d', days: 90 },
  { label: '6mo', days: 182 },
  { label: '1yr', days: 365 },
] as const;

type PruneAction = 'archive' | 'delete';

type PruneTabProps = {
  boardId: string;
};

export function PruneTab({ boardId }: PruneTabProps) {
  const queryClient = useQueryClient();

  const [action, setAction] = useState<PruneAction>('archive');
  const [includeArchived, setIncludeArchived] = useState(false);
  const [olderThan, setOlderThan] = useState<string | null>(null);
  const [selectedPreset, setSelectedPreset] = useState<string | null>(null);
  const [selectedLaneIds, setSelectedLaneIds] = useState<string[]>([]);
  const [selectedLabelIds, setSelectedLabelIds] = useState<string[]>([]);
  const [isLaneDropdownOpen, setIsLaneDropdownOpen] = useState(false);
  const [isLabelDropdownOpen, setIsLabelDropdownOpen] = useState(false);
  const [preview, setPreview] = useState<PrunePreviewResponse | null>(null);
  const [isConfirming, setIsConfirming] = useState(false);
  const [resultCount, setResultCount] = useState<{ count: number; action: PruneAction } | null>(
    null,
  );

  const laneDropdownRef = useRef<HTMLDivElement>(null);
  const labelDropdownRef = useRef<HTMLDivElement>(null);

  const lanesQuery = useQuery({
    queryKey: queryKeys.lanes.all(boardId),
    queryFn: () => fetchLanes(boardId),
    ...QUERY_DEFAULTS.boardData,
  });

  const labelsQuery = useQuery({
    queryKey: queryKeys.labels.all(boardId),
    queryFn: () => fetchLabels(boardId),
    ...QUERY_DEFAULTS.labels,
  });

  const previewMutation = useMutation({
    mutationFn: (filters: PruneFilters) => prunePreview(boardId, filters),
    onSuccess: (data) => {
      setPreview(data);
      setIsConfirming(false);
      setResultCount(null);
    },
    onError: (error: unknown) => {
      console.error('Failed to preview prune:', error);
    },
  });

  const pruneMutation = useMutation({
    mutationFn: (filters: PruneFilters) => pruneCards(boardId, filters),
    onSuccess: (data) => {
      const count = data.archivedCount ?? data.deletedCount ?? 0;
      setResultCount({ count, action });
      setPreview(null);
      setIsConfirming(false);
      setOlderThan(null);
      setSelectedPreset(null);
      setSelectedLaneIds([]);
      setSelectedLabelIds([]);
      queryClient.invalidateQueries({ queryKey: queryKeys.boards.data(boardId) });
      queryClient.invalidateQueries({ queryKey: queryKeys.boards.cards(boardId) });
    },
    onError: (error: unknown) => {
      console.error('Failed to prune cards:', error);
    },
  });

  const clearResults = () => {
    setPreview(null);
    setIsConfirming(false);
    setResultCount(null);
  };

  const handlePresetClick = (preset: string, days: number) => {
    if (selectedPreset === preset) {
      setSelectedPreset(null);
      setOlderThan(null);
    } else {
      setSelectedPreset(preset);
      const cutoff = new Date();
      cutoff.setDate(cutoff.getDate() - days);
      setOlderThan(cutoff.toISOString());
    }
    clearResults();
  };

  const handleCustomDate = (value: string) => {
    setSelectedPreset(null);
    setOlderThan(value ? new Date(value).toISOString() : null);
    clearResults();
  };

  const toggleLane = (laneId: string) => {
    setSelectedLaneIds((prev) =>
      prev.includes(laneId) ? prev.filter((id) => id !== laneId) : [...prev, laneId],
    );
    clearResults();
  };

  const toggleLabel = (labelId: string) => {
    setSelectedLabelIds((prev) =>
      prev.includes(labelId) ? prev.filter((id) => id !== labelId) : [...prev, labelId],
    );
    clearResults();
  };

  const closeLaneDropdown = useCallback(() => setIsLaneDropdownOpen(false), []);
  const closeLabelDropdown = useCallback(() => setIsLabelDropdownOpen(false), []);
  useClickOutside(laneDropdownRef, closeLaneDropdown);
  useClickOutside(labelDropdownRef, closeLabelDropdown);

  const hasActiveFilter =
    olderThan !== null || selectedLaneIds.length > 0 || selectedLabelIds.length > 0;

  const buildFilters = (): PruneFilters => ({
    ...(olderThan ? { olderThan } : {}),
    ...(selectedLaneIds.length > 0 ? { laneIds: selectedLaneIds } : {}),
    ...(selectedLabelIds.length > 0 ? { labelIds: selectedLabelIds } : {}),
    action,
    ...(includeArchived ? { includeArchived: true } : {}),
  });

  const handlePreview = () => {
    previewMutation.mutate(buildFilters());
  };

  const handleExecute = () => {
    if (!isConfirming) {
      setIsConfirming(true);
    } else {
      pruneMutation.mutate(buildFilters());
    }
  };

  const lanes = lanesQuery.data ?? [];
  const labels = labelsQuery.data ?? [];

  const selectedLaneNames = lanes.filter((l) => selectedLaneIds.includes(l.id)).map((l) => l.name);

  const selectedLabelNames = labels
    .filter((l) => selectedLabelIds.includes(l.id))
    .map((l) => l.name);

  const formatDate = formatDateTime;

  const customDateValue =
    olderThan && !selectedPreset ? new Date(olderThan).toISOString().split('T')[0] : '';

  const isDelete = action === 'delete';

  return (
    <div className="flex flex-col gap-4">
      {/* Action toggle */}
      <div className="flex flex-col gap-2">
        <span className="text-sm font-medium">Action</span>
        <div className="flex gap-1">
          <Button
            variant={!isDelete ? 'default' : 'outline'}
            size="sm"
            onClick={() => {
              setAction('archive');
              clearResults();
            }}
          >
            <Archive className="mr-1.5 h-3.5 w-3.5" />
            Archive
          </Button>
          <Button
            variant={isDelete ? 'destructive' : 'outline'}
            size="sm"
            onClick={() => {
              setAction('delete');
              clearResults();
            }}
          >
            <Trash2 className="mr-1.5 h-3.5 w-3.5" />
            Delete
          </Button>
        </div>
        {isDelete && (
          <p className="flex items-center gap-1.5 text-xs text-destructive">
            <AlertTriangle className="h-3 w-3" />
            Cards will be permanently deleted.
          </p>
        )}
      </div>

      {/* Card status filter */}
      <div className="flex flex-col gap-2">
        <span className="text-sm font-medium">Card status</span>
        <label className="flex cursor-pointer items-center gap-2">
          <input
            type="checkbox"
            checked={includeArchived}
            onChange={(e) => {
              setIncludeArchived(e.target.checked);
              clearResults();
            }}
            className="rounded"
          />
          <span className="text-sm">Include archived cards</span>
        </label>
      </div>

      {/* No activity since */}
      <div className="flex flex-col gap-2">
        <span className="text-sm font-medium">No activity since</span>
        <div className="flex flex-wrap items-center gap-2">
          {PRESETS.map((p) => (
            <Button
              key={p.label}
              variant={selectedPreset === p.label ? 'default' : 'outline'}
              size="sm"
              onClick={() => handlePresetClick(p.label, p.days)}
            >
              {p.label}
            </Button>
          ))}
          <Input
            type="date"
            value={customDateValue}
            onChange={(e) => handleCustomDate(e.target.value)}
            className="h-8 w-40"
          />
        </div>
        {olderThan && (
          <p className="text-xs text-muted-foreground">
            Cards not updated since {formatDate(olderThan)}
          </p>
        )}
      </div>

      {/* In lanes */}
      <div className="flex flex-col gap-2">
        <span className="text-sm font-medium">In lanes</span>
        <div ref={laneDropdownRef} className="relative">
          <Button
            variant="outline"
            size="sm"
            onClick={() => setIsLaneDropdownOpen((o) => !o)}
            className="w-full justify-start"
          >
            {selectedLaneNames.length > 0 ? selectedLaneNames.join(', ') : 'Any lane'}
          </Button>
          {isLaneDropdownOpen && (
            <div className="absolute z-50 mt-1 w-full rounded-lg border border-border bg-card p-2 shadow-md">
              {lanes.map((lane) => (
                <label
                  key={lane.id}
                  className="flex cursor-pointer items-center gap-2 rounded px-2 py-1.5 hover:bg-muted/50"
                >
                  <input
                    type="checkbox"
                    checked={selectedLaneIds.includes(lane.id)}
                    onChange={() => toggleLane(lane.id)}
                    className="rounded"
                  />
                  <span className="text-sm">{lane.name}</span>
                </label>
              ))}
            </div>
          )}
        </div>
      </div>

      {/* With labels */}
      <div className="flex flex-col gap-2">
        <span className="text-sm font-medium">With labels</span>
        <div ref={labelDropdownRef} className="relative">
          <Button
            variant="outline"
            size="sm"
            onClick={() => setIsLabelDropdownOpen((o) => !o)}
            className="w-full justify-start"
          >
            {selectedLabelNames.length > 0 ? selectedLabelNames.join(', ') : 'Any label'}
          </Button>
          {isLabelDropdownOpen && (
            <div className="absolute z-50 mt-1 w-full rounded-lg border border-border bg-card p-2 shadow-md">
              {labels.map((label) => (
                <label
                  key={label.id}
                  className="flex cursor-pointer items-center gap-2 rounded px-2 py-1.5 hover:bg-muted/50"
                >
                  <input
                    type="checkbox"
                    checked={selectedLabelIds.includes(label.id)}
                    onChange={() => toggleLabel(label.id)}
                    className="rounded"
                  />
                  <span
                    className="inline-block h-3 w-3 rounded-full"
                    style={{ backgroundColor: label.color ?? '#6b7280' }}
                  />
                  <span className="text-sm">{label.name}</span>
                </label>
              ))}
            </div>
          )}
        </div>
      </div>

      <p className="text-xs text-muted-foreground">
        {isDelete ? (
          <>
            Permanently delete cards matching <strong>all</strong> selected filters.
          </>
        ) : (
          <>
            Archive cards matching <strong>all</strong> selected filters.
          </>
        )}
      </p>

      <Separator />

      <Button onClick={handlePreview} disabled={!hasActiveFilter || previewMutation.isPending}>
        <Search className="mr-2 w-4 h-4" />
        {previewMutation.isPending ? 'Searching...' : 'Preview'}
      </Button>

      {/* Preview results */}
      {preview && (
        <div className="flex flex-col gap-3">
          <div className="flex items-center gap-2">
            <span className="text-sm font-medium">
              {preview.matchCount} cards will be {isDelete ? 'permanently deleted' : 'archived'}
            </span>
            <Badge variant="secondary">preview</Badge>
          </div>

          {preview.cards.length > 0 ? (
            <div className="max-h-60 overflow-y-auto rounded-lg border border-border">
              {preview.cards.map((card) => (
                <div
                  key={card.id}
                  className="flex items-center gap-3 border-b border-border px-3 py-2 last:border-b-0 hover:bg-muted/50"
                >
                  <span className="text-sm font-medium text-muted-foreground">#{card.number}</span>
                  <span className="flex-1 truncate text-sm">{card.name}</span>
                  <span className="text-xs text-muted-foreground">{card.laneName}</span>
                  <span className="text-xs text-muted-foreground">
                    {formatDate(card.lastUpdatedAtUtc)}
                  </span>
                </div>
              ))}
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">No cards match the current filters.</p>
          )}

          {preview.matchCount > 0 && (
            <Button
              variant="destructive"
              onClick={handleExecute}
              disabled={pruneMutation.isPending}
              className={cn(!isDelete && 'bg-primary hover:bg-primary/90')}
            >
              {isDelete ? (
                <Trash2 className="mr-2 w-4 h-4" />
              ) : (
                <Archive className="mr-2 w-4 h-4" />
              )}
              {pruneMutation.isPending
                ? isDelete
                  ? 'Deleting...'
                  : 'Archiving...'
                : isConfirming
                  ? isDelete
                    ? `Permanently delete ${preview.matchCount} cards? This cannot be undone.`
                    : `Archive ${preview.matchCount} cards?`
                  : isDelete
                    ? `Delete ${preview.matchCount} cards`
                    : `Archive ${preview.matchCount} cards`}
            </Button>
          )}
        </div>
      )}

      {/* Success state */}
      {resultCount !== null && (
        <div className="flex items-center gap-2 text-sm font-medium text-chart-3">
          <Check className="w-4 h-4" />
          {resultCount.action === 'delete'
            ? `Deleted ${resultCount.count} cards`
            : `Archived ${resultCount.count} cards`}
        </div>
      )}
    </div>
  );
}
