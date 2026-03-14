import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
  SheetDescription,
  SheetFooter,
} from '@/components/ui/sheet';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { deleteCard, fetchCardLabels, updateCard } from '@/lib/api';
import type { CardItem } from '@/types';

type CardDetailSheetProps = {
  card: CardItem | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

export function CardDetailSheet({ card, open, onOpenChange }: CardDetailSheetProps) {
  if (!card) return null;

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="flex flex-col overflow-y-auto sm:max-w-md">
        <CardDetailForm key={card.id} card={card} onOpenChange={onOpenChange} />
      </SheetContent>
    </Sheet>
  );
}

function CardDetailForm({
  card,
  onOpenChange,
}: {
  card: CardItem;
  onOpenChange: (open: boolean) => void;
}) {
  const queryClient = useQueryClient();

  const [name, setName] = useState(card.name);
  const [description, setDescription] = useState(card.descriptionMarkdown ?? '');
  const [size, setSize] = useState(card.size);
  const [blocked, setBlocked] = useState(card.blocked ?? '');
  const [confirmDelete, setConfirmDelete] = useState(false);

  const labelsQuery = useQuery({
    queryKey: ['cardLabels', card.id],
    queryFn: () => fetchCardLabels(card.id),
  });

  const updateMutation = useMutation({
    mutationFn: (patch: Record<string, unknown>) => updateCard(card.id, patch),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['board'] });
      onOpenChange(false);
    },
  });

  const deleteMutation = useMutation({
    mutationFn: () => deleteCard(card.id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['board'] });
      onOpenChange(false);
    },
  });

  const handleSave = () => {
    const patch: Record<string, unknown> = {};

    if (name !== card.name) patch.name = name;
    if (description !== (card.descriptionMarkdown ?? ''))
      patch.descriptionMarkdown = description;
    if (size !== card.size) patch.size = size;

    const blockedValue = blocked.trim() === '' ? null : blocked.trim();
    if (blockedValue !== (card.blocked ?? null)) patch.blocked = blockedValue;

    if (Object.keys(patch).length > 0) {
      updateMutation.mutate(patch);
    } else {
      onOpenChange(false);
    }
  };

  const handleDelete = () => {
    if (!confirmDelete) {
      setConfirmDelete(true);
      return;
    }
    deleteMutation.mutate();
  };

  const labels = labelsQuery.data ?? [];
  const isBlocked = blocked.trim() !== '';

  return (
    <>
      <SheetHeader>
        <SheetDescription>#{card.number}</SheetDescription>
        <SheetTitle>{card.name}</SheetTitle>
      </SheetHeader>

      <div className="flex flex-1 flex-col gap-4 px-4">
        {/* Name */}
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="card-name">Name</Label>
          <Input
            id="card-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
          />
        </div>

        {/* Description */}
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="card-description">Description (Markdown)</Label>
          <Textarea
            id="card-description"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            rows={6}
          />
        </div>

        {/* Size */}
        <div className="flex flex-col gap-1.5">
          <Label>Size</Label>
          <Select value={size} onValueChange={(v) => v && setSize(v)}>
            <SelectTrigger>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="S">S</SelectItem>
              <SelectItem value="M">M</SelectItem>
              <SelectItem value="L">L</SelectItem>
              <SelectItem value="XL">XL</SelectItem>
            </SelectContent>
          </Select>
        </div>

        {/* Blocked */}
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="card-blocked">
            Blocked reason
            {isBlocked && (
              <Badge variant="destructive" className="ml-2">
                Blocked
              </Badge>
            )}
          </Label>
          <Input
            id="card-blocked"
            value={blocked}
            onChange={(e) => setBlocked(e.target.value)}
            placeholder="Leave empty if not blocked"
          />
        </div>

        {/* Labels */}
        {labels.length > 0 && (
          <div className="flex flex-col gap-1.5">
            <Label>Labels</Label>
            <div className="flex flex-wrap gap-1">
              {labels.map((label) => (
                <Badge
                  key={label.id}
                  variant="secondary"
                  style={
                    label.color
                      ? { backgroundColor: label.color, color: '#fff' }
                      : undefined
                  }
                >
                  {label.name}
                </Badge>
              ))}
            </div>
          </div>
        )}

        {/* Metadata */}
        <div className="mt-2 text-xs text-muted-foreground">
          <p>Created: {new Date(card.createdAtUtc).toLocaleString()}</p>
          <p>Updated: {new Date(card.lastUpdatedAtUtc).toLocaleString()}</p>
        </div>
      </div>

      <SheetFooter>
        <div className="flex w-full items-center justify-between">
          <Button
            variant="destructive"
            onClick={handleDelete}
            disabled={deleteMutation.isPending}
          >
            {confirmDelete ? 'Confirm delete' : 'Delete'}
          </Button>
          <div className="flex gap-2">
            <Button variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button onClick={handleSave} disabled={updateMutation.isPending}>
              {updateMutation.isPending ? 'Saving...' : 'Save'}
            </Button>
          </div>
        </div>
      </SheetFooter>
    </>
  );
}
