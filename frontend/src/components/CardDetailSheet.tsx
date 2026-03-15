import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogDescription,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { Label } from '@/components/ui/label';
import { Separator } from '@/components/ui/separator';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { CardComments } from '@/components/CardComments';
import { CardAttachments } from '@/components/CardAttachments';
import { addCardLabel, deleteCard, fetchCardLabels, fetchLabels, fetchUserDirectory, reorderCard, removeCardLabel, updateCard } from '@/lib/api';
import type { CardItem, Lane } from '@/types';

type CardDetailSheetProps = {
  card: CardItem | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  currentUserId?: string;
  currentUserRole?: number;
  lanes?: Lane[];
  boardId?: string;
};

export function CardDetailSheet({ card, open, onOpenChange, currentUserId, currentUserRole, lanes, boardId }: CardDetailSheetProps) {
  if (!card) return null;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="flex max-h-[85vh] !w-[80vw] !max-w-[80vw] flex-col overflow-hidden p-0">
        <CardDetailForm key={card.id} card={card} onOpenChange={onOpenChange} currentUserId={currentUserId} currentUserRole={currentUserRole} lanes={lanes} boardId={boardId} />
      </DialogContent>
    </Dialog>
  );
}

function CardDetailForm({
  card,
  onOpenChange,
  currentUserId,
  currentUserRole,
  lanes,
  boardId,
}: {
  card: CardItem;
  onOpenChange: (open: boolean) => void;
  currentUserId?: string;
  currentUserRole?: number;
  lanes?: Lane[];
  boardId?: string;
}) {
  const queryClient = useQueryClient();

  const [name, setName] = useState(card.name);
  const [currentLaneId, setCurrentLaneId] = useState(card.laneId);
  const [description, setDescription] = useState(card.descriptionMarkdown ?? '');
  const [size, setSize] = useState(card.size);
  const [editingDescription, setEditingDescription] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);

  const canDelete = currentUserRole === 0 || (currentUserRole === 1 && card.createdByUserId === currentUserId);

  const directoryQuery = useQuery({
    queryKey: ['userDirectory'],
    queryFn: fetchUserDirectory,
    staleTime: 60_000,
  });
  const userName = (id: string) =>
    directoryQuery.data?.find((u) => u.id === id)?.name ?? 'Unknown';

  const labelsQuery = useQuery({
    queryKey: ['cardLabels', card.id],
    queryFn: () => fetchCardLabels(card.id),
  });

  const allLabelsQuery = useQuery({
    queryKey: ['labels'],
    queryFn: fetchLabels,
  });

  const addLabelMutation = useMutation({
    mutationFn: (labelId: string) => addCardLabel(card.id, labelId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['cardLabels', card.id] });
      queryClient.invalidateQueries({ queryKey: ['board'] });
    },
  });

  const removeLabelMutation = useMutation({
    mutationFn: (labelId: string) => removeCardLabel(card.id, labelId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['cardLabels', card.id] });
      queryClient.invalidateQueries({ queryKey: ['board'] });
    },
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

  const moveMutation = useMutation({
    mutationFn: (laneId: string) => reorderCard(card.id, laneId, 0),
    onMutate: (laneId) => {
      setCurrentLaneId(laneId);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['boardData', boardId] });
    },
  });

  const handleSave = () => {
    const patch: Record<string, unknown> = {};

    if (name !== card.name) patch.name = name;
    if (description !== (card.descriptionMarkdown ?? '')) patch.descriptionMarkdown = description;
    if (size !== card.size) patch.size = size;

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

  return (
    <>
      {/* Header */}
      <DialogHeader className="px-6 pt-6 pb-0">
        <DialogDescription className="text-xs">#{card.number}</DialogDescription>
        <div className="flex items-start gap-3">
          <div className="flex-1">
            <Input
              value={name}
              onChange={(e) => setName(e.target.value)}
              className="border-none bg-transparent px-0 text-xl font-semibold shadow-none focus-visible:ring-0"
            />
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-3 pt-2">
          <Select value={size} onValueChange={(v) => v && setSize(v)}>
            <SelectTrigger className="w-20">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="S">S</SelectItem>
              <SelectItem value="M">M</SelectItem>
              <SelectItem value="L">L</SelectItem>
              <SelectItem value="XL">XL</SelectItem>
            </SelectContent>
          </Select>
          {lanes && lanes.length > 0 && (
            <select
              value={currentLaneId}
              onChange={(e) => {
                const v = e.target.value;
                if (v && v !== currentLaneId) {
                  moveMutation.mutate(v);
                }
              }}
              className="rounded-md border border-border bg-background px-3 py-1.5 text-sm text-foreground"
            >
              {lanes.map((lane) => (
                <option key={lane.id} value={lane.id}>{lane.name}</option>
              ))}
            </select>
          )}
          {(allLabelsQuery.data ?? []).map((label) => {
            const isAssigned = labels.some((l) => l.id === label.id);
            return (
              <button
                key={label.id}
                type="button"
                onClick={() => {
                  if (isAssigned) {
                    removeLabelMutation.mutate(label.id);
                  } else {
                    addLabelMutation.mutate(label.id);
                  }
                }}
                className="inline-flex items-center rounded-full border px-2.5 py-0.5 text-xs font-medium transition-opacity hover:opacity-80"
                style={{
                  backgroundColor: isAssigned ? (label.color ?? '#6b7280') : 'transparent',
                  color: isAssigned ? '#fff' : (label.color ?? '#9ca3af'),
                  borderColor: label.color ?? '#6b7280',
                  opacity: isAssigned ? 1 : 0.5,
                }}
              >
                {label.name}
              </button>
            );
          })}
        </div>
      </DialogHeader>

      {/* Two-column body */}
      <div className="flex flex-1 gap-0 overflow-hidden">
        {/* Left column — details */}
        <div className="flex-1 overflow-y-auto px-6 py-4">
          {/* Description */}
          <div className="mb-4">
            <div className="mb-2 flex items-center justify-between">
              <Label className="text-xs text-muted-foreground">Description</Label>
              <Button
                type="button"
                size="sm"
                variant="ghost"
                onClick={() => setEditingDescription(!editingDescription)}
              >
                {editingDescription ? 'Preview' : 'Edit'}
              </Button>
            </div>
            {editingDescription ? (
              <Textarea
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                rows={16}
                className="font-mono text-sm"
                placeholder="Write a description..."
              />
            ) : (
              <div className="prose prose-sm max-w-none rounded-md border bg-muted/30 p-4 text-sm text-foreground">
                {description ? (
                  <ReactMarkdown remarkPlugins={[remarkGfm]}>{description}</ReactMarkdown>
                ) : (
                  <p className="italic text-muted-foreground">No description yet. Click Edit to add one.</p>
                )}
              </div>
            )}
          </div>

          <Separator className="my-4" />

          {/* Attachments */}
          <div>
            <Label className="mb-2 text-xs text-muted-foreground">Attachments</Label>
            <CardAttachments cardId={card.id} currentUserId={currentUserId} currentUserRole={currentUserRole} />
          </div>

          {/* Metadata */}
          <div className="mt-4 text-xs text-muted-foreground">
            <p>Created by {userName(card.createdByUserId)} · {new Date(card.createdAtUtc).toLocaleString()}</p>
            <p>Updated by {userName(card.lastUpdatedByUserId)} · {new Date(card.lastUpdatedAtUtc).toLocaleString()}</p>
          </div>
        </div>

        {/* Right column — comments */}
        <div className="flex w-[340px] shrink-0 flex-col overflow-y-auto border-l border-border px-5 py-4">
          <h3 className="mb-3 text-sm font-semibold">Comments</h3>
          <CardComments cardId={card.id} currentUserId={currentUserId} currentUserRole={currentUserRole} />
        </div>
      </div>

      {/* Footer */}
      <div className="flex items-center justify-between border-t px-6 py-3">
        {canDelete ? (
          <Button variant="destructive" size="sm" onClick={handleDelete} disabled={deleteMutation.isPending}>
            {confirmDelete ? 'Confirm delete' : 'Delete'}
          </Button>
        ) : (
          <div />
        )}
        <div className="flex gap-2">
          <Button variant="outline" size="sm" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button size="sm" onClick={handleSave} disabled={updateMutation.isPending}>
            {updateMutation.isPending ? 'Saving...' : 'Save'}
          </Button>
        </div>
      </div>
    </>
  );
}
