import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
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
import { cn } from '@/lib/utils';
import { CardComments } from '@/components/CardComments';
import { CardAttachments } from '@/components/CardAttachments';
import {
  deleteCard,
  fetchCardLabels,
  fetchLabels,
  updateCard,
  uploadAttachment,
} from '@/lib/api';
import { LabelPicker } from '@/components/LabelPicker';
import { queryKeys } from '@/lib/query-keys';
import { QUERY_DEFAULTS } from '@/lib/query-config';
import { useUserDirectory } from '@/hooks/use-user-directory';
import { isTextInputFocused, buildPasteFileName } from '@/lib/utils';
import { ChevronLeft, ChevronRight } from 'lucide-react';
import type { BoardData, CardItem, CardSize, Lane, UpdateCardPatch } from '@/types';

type CardDetailSheetProps = {
  card: CardItem | null;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  currentUserId?: string;
  currentUserRole?: number;
  lanes?: Lane[];
  boardId?: string;
  sizes?: CardSize[];
  cardsInLane?: CardItem[];
  onNavigateCard?: (cardNumber: number) => void;
};

type CardDetailFormProps = {
  card: CardItem;
  onClose: () => void;
  currentUserId?: string;
  currentUserRole?: number;
  lanes?: Lane[];
  boardId?: string;
  sizes?: CardSize[];
  navPosition?: string | null;
  onNavigatePrev?: () => void;
  onNavigateNext?: () => void;
};

function arraysEqual(a: string[], b: string[]): boolean {
  if (a.length !== b.length) return false;
  const sorted1 = [...a].sort();
  const sorted2 = [...b].sort();
  return sorted1.every((v, i) => v === sorted2[i]);
}

export function CardDetailSheet({
  card,
  open,
  onOpenChange,
  currentUserId,
  currentUserRole,
  lanes,
  boardId,
  sizes,
  cardsInLane,
  onNavigateCard,
}: CardDetailSheetProps) {
  const isDirtyRef = useRef(false);

  const { prevCard, nextCard } = useMemo(() => {
    if (!card || !cardsInLane || cardsInLane.length === 0) return { prevCard: null, nextCard: null };
    const idx = cardsInLane.findIndex((c) => c.id === card.id);
    if (idx === -1) return { prevCard: null, nextCard: null };
    return {
      prevCard: idx > 0 ? cardsInLane[idx - 1] : null,
      nextCard: idx < cardsInLane.length - 1 ? cardsInLane[idx + 1] : null,
    };
  }, [card, cardsInLane]);

  const handleNavigate = useCallback(
    (direction: 'prev' | 'next') => {
      const target = direction === 'prev' ? prevCard : nextCard;
      if (!target || !onNavigateCard) return;
      if (isDirtyRef.current) {
        if (!window.confirm('You have unsaved changes. Discard them?')) return;
      }
      isDirtyRef.current = false;
      onNavigateCard(target.number);
    },
    [prevCard, nextCard, onNavigateCard],
  );

  useEffect(() => {
    if (!open) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (isTextInputFocused()) return;
      if (e.key === 'ArrowLeft' && prevCard) {
        e.preventDefault();
        handleNavigate('prev');
      } else if (e.key === 'ArrowRight' && nextCard) {
        e.preventDefault();
        handleNavigate('next');
      }
    };
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [open, prevCard, nextCard, handleNavigate]);

  const handleDialogOpenChange = useCallback(
    (nextOpen: boolean) => {
      if (!nextOpen) {
        if (isDirtyRef.current) {
          if (!window.confirm('You have unsaved changes. Discard them?')) return;
        }
      }
      onOpenChange(nextOpen);
    },
    [onOpenChange],
  );

  if (!card) return null;

  const navPosition = cardsInLane && cardsInLane.length > 1
    ? `${(cardsInLane.findIndex((c) => c.id === card.id) ?? 0) + 1} / ${cardsInLane.length}`
    : null;

  return (
    <Dialog open={open} onOpenChange={handleDialogOpenChange}>
      <DialogContent data-mobile-fullscreen className="flex flex-col p-0 md:max-h-[85vh] md:!w-[80vw] md:!max-w-[80vw]" style={{ overflow: 'visible' }}>
        {/* Floating nav buttons — desktop only, positioned outside the dialog */}
        {prevCard && (
          <button
            type="button"
            onClick={() => handleNavigate('prev')}
            className="absolute top-1/2 -left-14 z-50 hidden h-10 w-10 -translate-y-1/2 items-center justify-center rounded-full bg-background shadow-lg ring-1 ring-border transition-colors hover:bg-accent md:flex"
            aria-label="Previous card"
          >
            <ChevronLeft className="h-5 w-5" />
          </button>
        )}
        {nextCard && (
          <button
            type="button"
            onClick={() => handleNavigate('next')}
            className="absolute top-1/2 -right-14 z-50 hidden h-10 w-10 -translate-y-1/2 items-center justify-center rounded-full bg-background shadow-lg ring-1 ring-border transition-colors hover:bg-accent md:flex"
            aria-label="Next card"
          >
            <ChevronRight className="h-5 w-5" />
          </button>
        )}
        <div className="flex min-h-0 flex-1 flex-col overflow-hidden">
          <CardDetailForm
            key={card.id}
            card={card}
            onClose={() => handleDialogOpenChange(false)}
            currentUserId={currentUserId}
            currentUserRole={currentUserRole}
            lanes={lanes}
            boardId={boardId}
            sizes={sizes}
            isDirtyRef={isDirtyRef}
            navPosition={navPosition}
            onNavigatePrev={prevCard ? () => handleNavigate('prev') : undefined}
            onNavigateNext={nextCard ? () => handleNavigate('next') : undefined}
          />
        </div>
      </DialogContent>
    </Dialog>
  );
}

function CardDetailForm({
  card,
  onClose,
  currentUserId,
  currentUserRole,
  lanes,
  boardId,
  sizes,
  isDirtyRef,
  navPosition,
  onNavigatePrev,
  onNavigateNext,
}: CardDetailFormProps & { isDirtyRef: React.MutableRefObject<boolean> }) {
  const queryClient = useQueryClient();
  const dialogRef = useRef<HTMLDivElement>(null);

  const [name, setName] = useState(card.name);
  const [currentLaneId, setCurrentLaneId] = useState(card.laneId);
  const [description, setDescription] = useState(card.descriptionMarkdown ?? '');
  const [sizeId, setSizeId] = useState(card.sizeId);
  const [selectedLabelIds, setSelectedLabelIds] = useState<string[] | null>(null);
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
    if (!isTextInputFocused()) {
      el.focus({ preventScroll: true });
    }
    return () => el.removeEventListener('paste', handlePaste);
  }, [handlePaste]);

  const canDelete =
    currentUserRole === 0 || (currentUserRole === 1 && card.createdByUserId === currentUserId);

  const directoryQuery = useUserDirectory();
  const userName = (id: string) => directoryQuery.data?.find((u) => u.id === id)?.name ?? 'Unknown';

  const labelsQuery = useQuery({
    queryKey: queryKeys.cards.labels(card.id),
    queryFn: () => fetchCardLabels(card.id),
    ...QUERY_DEFAULTS.labels,
  });

  const allLabelsQuery = useQuery({
    queryKey: queryKeys.labels.all(boardId!),
    queryFn: () => fetchLabels(boardId!),
    enabled: !!boardId,
    ...QUERY_DEFAULTS.labels,
  });

  const originalLabelIds = useMemo(
    () => (labelsQuery.data ?? []).map((l) => l.id),
    [labelsQuery.data],
  );

  const effectiveLabelIds = selectedLabelIds ?? originalLabelIds;

  const assignedLabels = useMemo(
    () => (allLabelsQuery.data ?? []).filter((l) => effectiveLabelIds.includes(l.id)),
    [allLabelsQuery.data, effectiveLabelIds],
  );

  const isDirty =
    name !== card.name ||
    description !== (card.descriptionMarkdown ?? '') ||
    sizeId !== card.sizeId ||
    currentLaneId !== card.laneId ||
    !arraysEqual(effectiveLabelIds, originalLabelIds);

  useEffect(() => {
    isDirtyRef.current = isDirty;
  }, [isDirty, isDirtyRef]);

  const updateMutation = useMutation({
    mutationFn: (patch: UpdateCardPatch) => updateCard(card.id, patch),
    onSuccess: (updatedCard) => {
      if (boardId) {
        queryClient.setQueryData<BoardData>(
          queryKeys.boards.data(boardId),
          (old) => old ? {
            ...old,
            cards: old.cards.map((c) => c.id === card.id ? updatedCard : c),
          } : old,
        );
      }
      queryClient.invalidateQueries({ queryKey: queryKeys.cards.labels(card.id) });
      isDirtyRef.current = false;
      onClose();
    },
  });

  const deleteMutation = useMutation({
    mutationFn: () => deleteCard(card.id),
    onSuccess: () => {
      if (boardId) queryClient.invalidateQueries({ queryKey: queryKeys.boards.data(boardId) });
      isDirtyRef.current = false;
      onClose();
    },
  });

  const handleSave = () => {
    if (!isDirty) {
      isDirtyRef.current = false;
      onClose();
      return;
    }

    const patch: UpdateCardPatch = {};

    if (name !== card.name) patch.name = name;
    if (description !== (card.descriptionMarkdown ?? '')) patch.descriptionMarkdown = description;
    if (sizeId !== card.sizeId) patch.sizeId = sizeId;
    if (currentLaneId !== card.laneId) patch.laneId = currentLaneId;
    if (!arraysEqual(effectiveLabelIds, originalLabelIds)) patch.labelIds = effectiveLabelIds;

    updateMutation.mutate(patch);
  };

  const handleDelete = () => {
    if (!confirmDelete) {
      setConfirmDelete(true);
      return;
    }
    deleteMutation.mutate();
  };

  const handleClose = () => {
    onClose();
  };

  return (
    <div ref={dialogRef} tabIndex={-1} className="flex min-h-0 flex-1 flex-col overflow-hidden outline-none">
      {/* Paste feedback */}
      {pasteStatus && (
        <div className="bg-primary/10 text-primary border-b px-6 py-2 text-sm">{pasteStatus}</div>
      )}

      {/* Header */}
      <DialogHeader className="px-6 pt-6 pb-4">
        <div className="flex items-center gap-2">
          <DialogDescription className="text-xs">#{card.number}</DialogDescription>
          {(onNavigatePrev || onNavigateNext) && (
            <div className="flex items-center gap-1 md:hidden">
              <Button
                type="button"
                variant="ghost"
                size="icon-sm"
                onClick={onNavigatePrev}
                disabled={!onNavigatePrev}
                aria-label="Previous card"
              >
                <ChevronLeft className="h-4 w-4" />
              </Button>
              {navPosition && (
                <span className="text-xs text-muted-foreground tabular-nums">{navPosition}</span>
              )}
              <Button
                type="button"
                variant="ghost"
                size="icon-sm"
                onClick={onNavigateNext}
                disabled={!onNavigateNext}
                aria-label="Next card"
              >
                <ChevronRight className="h-4 w-4" />
              </Button>
            </div>
          )}
        </div>
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
            <Select
              value={currentLaneId}
              onValueChange={(v) => {
                if (v && v !== currentLaneId) {
                  setCurrentLaneId(v);
                }
              }}
            >
              <SelectTrigger className="w-36">
                <SelectValue>
                  {lanes.find((l) => l.id === currentLaneId)?.name ?? '?'}
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
          )}
          <LabelPicker
            allLabels={allLabelsQuery.data ?? []}
            assignedLabels={assignedLabels}
            onAdd={(id) => setSelectedLabelIds((prev) => [...(prev ?? originalLabelIds), id])}
            onRemove={(id) => setSelectedLabelIds((prev) => (prev ?? originalLabelIds).filter((x) => x !== id))}
          />
        </div>
      </DialogHeader>

      {/* Two-column body (stacked on mobile) */}
      <div className="flex flex-1 gap-0 overflow-hidden max-md:flex-col max-md:overflow-y-auto">
        {/* Left column — details */}
        <div className="flex-1 px-6 py-4 md:overflow-y-auto">
          {/* Description */}
          <div className="mb-4">
            <div className="mb-2 flex items-center gap-1">
              <button
                type="button"
                className={cn(
                  "rounded-md px-2.5 py-1 text-xs font-medium transition-colors",
                  !editingDescription
                    ? "bg-muted text-foreground"
                    : "text-muted-foreground hover:text-foreground"
                )}
                onClick={() => setEditingDescription(false)}
              >
                Preview
              </button>
              <button
                type="button"
                className={cn(
                  "rounded-md px-2.5 py-1 text-xs font-medium transition-colors",
                  editingDescription
                    ? "bg-muted text-foreground"
                    : "text-muted-foreground hover:text-foreground"
                )}
                onClick={() => setEditingDescription(true)}
              >
                Edit
              </button>
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
                    No description yet. Switch to Edit to add one.
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
        <div className="flex shrink-0 flex-col border-border px-5 pt-2 pb-4 max-md:w-full max-md:border-t md:w-[340px] md:overflow-y-auto md:border-l">
          <h3 className="mb-3 text-sm font-semibold">
            Comments{' '}
            <span className="font-normal text-muted-foreground">(Markdown supported)</span>
          </h3>
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
          <Button variant="outline" size="sm" onClick={handleClose}>
            Close
          </Button>
          <Button
            size="sm"
            onClick={handleSave}
            disabled={!isDirty || updateMutation.isPending}
          >
            {updateMutation.isPending ? 'Saving...' : 'Save'}
          </Button>
        </div>
      </div>
    </div>
  );
}
