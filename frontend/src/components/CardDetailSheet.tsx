import { useCallback, useEffect, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { Dialog, DialogContent, DialogHeader, DialogDescription } from '@/components/ui/dialog';
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
import {
  addCardLabel,
  deleteCard,
  fetchCardLabels,
  fetchLabels,
  fetchUserDirectory,
  reorderCard,
  removeCardLabel,
  updateCard,
  uploadAttachment,
} from '@/lib/api';
import { LabelPicker } from '@/components/LabelPicker';
import { queryKeys } from '@/lib/query-keys';
import type { CardItem, CardSize, Lane } from '@/types';

type CardDetailSheetProps = {
  card: CardItem | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  currentUserId?: string;
  currentUserRole?: number;
  lanes?: Lane[];
  boardId?: string;
  sizes?: CardSize[];
};

export function CardDetailSheet({
  card,
  open,
  onOpenChange,
  currentUserId,
  currentUserRole,
  lanes,
  boardId,
  sizes,
}: CardDetailSheetProps) {
  if (!card) return null;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent data-mobile-fullscreen className="flex flex-col overflow-hidden p-0 md:max-h-[85vh] md:!w-[80vw] md:!max-w-[80vw]">
        <CardDetailForm
          key={card.id}
          card={card}
          onOpenChange={onOpenChange}
          currentUserId={currentUserId}
          currentUserRole={currentUserRole}
          lanes={lanes}
          boardId={boardId}
          sizes={sizes}
        />
      </DialogContent>
    </Dialog>
  );
}

function isTextInputFocused(): boolean {
  const el = document.activeElement;
  if (!el) return false;
  const tag = el.tagName.toLowerCase();
  if (tag === 'textarea') return true;
  if (tag === 'input' && (el as HTMLInputElement).type !== 'file') return true;
  if ((el as HTMLElement).isContentEditable) return true;
  return false;
}

function buildPasteFileName(mimeType: string): string {
  const ext = mimeType.split('/')[1]?.replace('jpeg', 'jpg') ?? 'bin';
  const now = new Date();
  const ts = now.toISOString().replace(/[-:T]/g, '').replace(/\..+/, '').slice(0, 15);
  return `pasted-image-${ts}.${ext}`;
}

function CardDetailForm({
  card,
  onOpenChange,
  currentUserId,
  currentUserRole,
  lanes,
  boardId,
  sizes,
}: {
  card: CardItem;
  onOpenChange: (open: boolean) => void;
  currentUserId?: string;
  currentUserRole?: number;
  lanes?: Lane[];
  boardId?: string;
  sizes?: CardSize[];
}) {
  const queryClient = useQueryClient();
  const dialogRef = useRef<HTMLDivElement>(null);

  const [name, setName] = useState(card.name);
  const [currentLaneId, setCurrentLaneId] = useState(card.laneId);
  const [description, setDescription] = useState(card.descriptionMarkdown ?? '');
  const [sizeId, setSizeId] = useState(card.sizeId);
  const [editingDescription, setEditingDescription] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [pasteStatus, setPasteStatus] = useState<string | null>(null);

  const pasteMutation = useMutation({
    mutationFn: (file: File) => uploadAttachment(card.id, file),
    onSuccess: (data) => {
      queryClient.invalidateQueries({ queryKey: queryKeys.cards.attachments(card.id) });
      setPasteStatus(`Attached "${data.fileName}"`);
      setTimeout(() => setPasteStatus(null), 3000);
    },
    onError: () => {
      setPasteStatus('Paste upload failed');
      setTimeout(() => setPasteStatus(null), 3000);
    },
  });

  const handlePaste = useCallback(
    (e: ClipboardEvent) => {
      if (isTextInputFocused()) return;

      const items = e.clipboardData?.items;
      if (!items) return;

      for (const item of Array.from(items)) {
        if (item.kind === 'file' && item.type.startsWith('image/')) {
          const blob = item.getAsFile();
          if (!blob) continue;

          e.preventDefault();
          const file = new File([blob], buildPasteFileName(blob.type), { type: blob.type });
          pasteMutation.mutate(file);
          return;
        }
      }
    },
    [pasteMutation],
  );

  useEffect(() => {
    const el = dialogRef.current;
    if (!el) return;
    el.addEventListener('paste', handlePaste);
    return () => el.removeEventListener('paste', handlePaste);
  }, [handlePaste]);

  const canDelete =
    currentUserRole === 0 || (currentUserRole === 1 && card.createdByUserId === currentUserId);

  const directoryQuery = useQuery({
    queryKey: queryKeys.users.directory(),
    queryFn: fetchUserDirectory,
    staleTime: 60_000,
  });
  const userName = (id: string) => directoryQuery.data?.find((u) => u.id === id)?.name ?? 'Unknown';

  const labelsQuery = useQuery({
    queryKey: queryKeys.cards.labels(card.id),
    queryFn: () => fetchCardLabels(card.id),
  });

  const allLabelsQuery = useQuery({
    queryKey: queryKeys.labels.all(boardId!),
    queryFn: () => fetchLabels(boardId!),
    enabled: !!boardId,
  });

  const addLabelMutation = useMutation({
    mutationFn: (labelId: string) => addCardLabel(card.id, labelId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.cards.labels(card.id) });
      if (boardId) queryClient.invalidateQueries({ queryKey: queryKeys.boards.data(boardId) });
    },
  });

  const removeLabelMutation = useMutation({
    mutationFn: (labelId: string) => removeCardLabel(card.id, labelId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.cards.labels(card.id) });
      if (boardId) queryClient.invalidateQueries({ queryKey: queryKeys.boards.data(boardId) });
    },
  });

  const updateMutation = useMutation({
    mutationFn: (patch: Record<string, unknown>) => updateCard(card.id, patch),
    onSuccess: () => {
      if (boardId) queryClient.invalidateQueries({ queryKey: queryKeys.boards.data(boardId) });
      onOpenChange(false);
    },
  });

  const deleteMutation = useMutation({
    mutationFn: () => deleteCard(card.id),
    onSuccess: () => {
      if (boardId) queryClient.invalidateQueries({ queryKey: queryKeys.boards.data(boardId) });
      onOpenChange(false);
    },
  });

  const moveMutation = useMutation({
    mutationFn: (laneId: string) => reorderCard(card.id, laneId, 0),
    onMutate: (laneId) => {
      setCurrentLaneId(laneId);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.boards.data(boardId!) });
    },
  });

  const handleSave = () => {
    const patch: Record<string, unknown> = {};

    if (name !== card.name) patch.name = name;
    if (description !== (card.descriptionMarkdown ?? '')) patch.descriptionMarkdown = description;
    if (sizeId !== card.sizeId) patch.sizeId = sizeId;

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
    <div ref={dialogRef} className="flex min-h-0 flex-1 flex-col overflow-hidden">
      {/* Paste feedback */}
      {pasteStatus && (
        <div className="bg-primary/10 text-primary border-b px-6 py-2 text-sm">{pasteStatus}</div>
      )}

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
          {sizes && sizes.length > 0 && (
            <Select value={sizeId} onValueChange={(v) => v && setSizeId(v)}>
              <SelectTrigger className="w-20">
                <SelectValue>
                  {sizes.find((s) => s.id === sizeId)?.name ?? '?'}
                </SelectValue>
              </SelectTrigger>
              <SelectContent>
                {sizes.map((s) => (
                  <SelectItem key={s.id} value={s.id}>{s.name}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
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
                <option key={lane.id} value={lane.id}>
                  {lane.name}
                </option>
              ))}
            </select>
          )}
          <LabelPicker
            allLabels={allLabelsQuery.data ?? []}
            assignedLabels={labels}
            onAdd={(id) => addLabelMutation.mutate(id)}
            onRemove={(id) => removeLabelMutation.mutate(id)}
          />
        </div>
      </DialogHeader>

      {/* Two-column body (stacked on mobile) */}
      <div className="flex flex-1 gap-0 overflow-hidden max-md:flex-col max-md:overflow-y-auto">
        {/* Left column — details */}
        <div className="flex-1 px-6 py-4 md:overflow-y-auto">
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
                  <p className="italic text-muted-foreground">
                    No description yet. Click Edit to add one.
                  </p>
                )}
              </div>
            )}
          </div>

          <Separator className="my-4" />

          {/* Attachments */}
          <div>
            <Label className="mb-2 text-xs text-muted-foreground">Attachments</Label>
            <CardAttachments
              cardId={card.id}
              currentUserId={currentUserId}
              currentUserRole={currentUserRole}
            />
          </div>

          {/* Metadata */}
          <div className="mt-4 text-xs text-muted-foreground">
            <p>
              Created by {userName(card.createdByUserId)} ·{' '}
              {new Date(card.createdAtUtc).toLocaleString()}
            </p>
            <p>
              Updated by {userName(card.lastUpdatedByUserId)} ·{' '}
              {new Date(card.lastUpdatedAtUtc).toLocaleString()}
            </p>
          </div>
        </div>

        {/* Right column — comments (below on mobile) */}
        <div className="flex shrink-0 flex-col border-border px-5 py-4 max-md:w-full max-md:border-t md:w-[340px] md:overflow-y-auto md:border-l">
          <h3 className="mb-3 text-sm font-semibold">Comments</h3>
          <CardComments
            cardId={card.id}
            currentUserId={currentUserId}
            currentUserRole={currentUserRole}
          />
        </div>
      </div>

      {/* Footer */}
      <div className="flex items-center justify-between border-t px-6 py-3">
        {canDelete ? (
          <Button
            variant="destructive"
            size="sm"
            onClick={handleDelete}
            disabled={deleteMutation.isPending}
          >
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
    </div>
  );
}
