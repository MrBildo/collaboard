import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogDescription,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { createCard, fetchBoardData, fetchLabels } from '@/lib/api';
import { LabelPicker } from '@/components/LabelPicker';
import { queryKeys } from '@/lib/query-keys';
import { QUERY_DEFAULTS } from '@/lib/query-config';
import type { BoardData, CardSize, Lane } from '@/types';

type CreateCardDialogProps = {
  boardId: string;
  lanes: Lane[];
  sizes: CardSize[];
  open: boolean;
  onOpenChange: (open: boolean) => void;
  defaultLaneId?: string;
};

export function CreateCardDialog({ boardId, lanes, sizes, open, onOpenChange, defaultLaneId }: CreateCardDialogProps) {
  const queryClient = useQueryClient();

  const defaultSizeId = sizes.length > 0 ? [...sizes].sort((a, b) => a.ordinal - b.ordinal)[0].id : '';
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [sizeId, setSizeId] = useState(defaultSizeId);
  const [laneId, setLaneId] = useState(defaultLaneId ?? lanes[0]?.id ?? '');
  const [selectedLabelIds, setSelectedLabelIds] = useState<string[]>([]);

  const allLabelsQuery = useQuery({
    queryKey: queryKeys.labels.all(boardId),
    queryFn: () => fetchLabels(boardId),
    ...QUERY_DEFAULTS.labels,
  });

  const boardDataQuery = useQuery({
    queryKey: queryKeys.boards.data(boardId),
    queryFn: () => fetchBoardData(boardId),
    ...QUERY_DEFAULTS.boardData,
  });

  const createMutation = useMutation({
    mutationFn: () => {
      const cards = boardDataQuery.data?.cards ?? [];
      const laneCards = cards.filter((c) => c.laneId === laneId);
      const maxPosition =
        laneCards.length > 0
          ? Math.max(...laneCards.map((c) => c.position))
          : 0;

      return createCard(boardId, {
        name: name.trim(),
        descriptionMarkdown: description || undefined,
        sizeId: sizeId || undefined,
        laneId,
        position: maxPosition + 1,
        labelIds: selectedLabelIds.length > 0 ? selectedLabelIds : undefined,
      });
    },
    onSuccess: (newCard) => {
      queryClient.cancelQueries({ queryKey: queryKeys.boards.data(boardId) });
      queryClient.setQueryData<BoardData>(
        queryKeys.boards.data(boardId),
        (old) => old ? { ...old, cards: [...old.cards, newCard] } : old,
      );
      queryClient.invalidateQueries({ queryKey: queryKeys.boards.cards(boardId) });
      onOpenChange(false);
    },
  });

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim() || !sizeId) return;
    createMutation.mutate();
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <form onSubmit={handleSubmit}>
          <DialogHeader>
            <DialogTitle>Create Card</DialogTitle>
            <DialogDescription>Add a new card to the board.</DialogDescription>
          </DialogHeader>

          <div className="mt-4 flex flex-col gap-4">
            {/* Name */}
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="new-card-name">Name *</Label>
              <Input
                id="new-card-name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="Card name"
                required
              />
            </div>

            {/* Description */}
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="new-card-description">Description</Label>
              <Textarea
                id="new-card-description"
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                placeholder="Optional description (Markdown supported)"
                rows={4}
              />
            </div>

            {/* Size */}
            {sizes.length > 0 && (
              <div className="flex flex-col gap-1.5">
                <Label>Size</Label>
                <Select value={sizeId} onValueChange={(v) => v && setSizeId(v)}>
                  <SelectTrigger>
                    <SelectValue>
                      {sizes.find((s) => s.id === sizeId)?.name ?? 'Select size'}
                    </SelectValue>
                  </SelectTrigger>
                  <SelectContent>
                    {[...sizes].sort((a, b) => a.ordinal - b.ordinal).map((s) => (
                      <SelectItem key={s.id} value={s.id}>{s.name}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            )}

            {/* Labels */}
            {(allLabelsQuery.data ?? []).length > 0 && (
              <div className="flex flex-col gap-1.5">
                <Label>Labels</Label>
                <LabelPicker
                  allLabels={allLabelsQuery.data ?? []}
                  assignedLabels={(allLabelsQuery.data ?? []).filter((l) => selectedLabelIds.includes(l.id))}
                  onAdd={(id) => setSelectedLabelIds((prev) => [...prev, id])}
                  onRemove={(id) => setSelectedLabelIds((prev) => prev.filter((x) => x !== id))}
                />
              </div>
            )}

            {/* Lane */}
            <div className="flex flex-col gap-1.5">
              <Label>Lane</Label>
              <Select value={laneId} onValueChange={(v) => v && setLaneId(v)}>
                <SelectTrigger>
                  <SelectValue placeholder="Select lane">
                    {lanes.find((l) => l.id === laneId)?.name ?? 'Select lane'}
                  </SelectValue>
                </SelectTrigger>
                <SelectContent>
                  {lanes.map((lane) => (
                    <SelectItem key={lane.id} value={lane.id}>
                      {lane.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>

          <DialogFooter className="mt-4">
            <Button
              type="button"
              variant="outline"
              onClick={() => onOpenChange(false)}
            >
              Cancel
            </Button>
            <Button type="submit" disabled={createMutation.isPending || !name.trim() || !sizeId}>
              {createMutation.isPending ? 'Creating...' : 'Create'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
