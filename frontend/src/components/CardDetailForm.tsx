import {
  forwardRef,
  useCallback,
  useEffect,
  useImperativeHandle,
  useMemo,
  useRef,
  useState,
} from 'react';
import { usePanelResize } from '@/hooks/use-panel-resize';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { DialogHeader, DialogDescription } from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { Label } from '@/components/ui/label';
import { Separator } from '@/components/ui/separator';
import { Badge } from '@/components/ui/badge';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { CardComments } from '@/components/CardComments';
import { CardAttachments } from '@/components/CardAttachments';
import { deleteCard, fetchCardLabels, fetchLabels, updateCard, uploadAttachment } from '@/lib/api';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip';
import { LabelPicker } from '@/components/LabelPicker';
import { queryKeys } from '@/lib/query-keys';
import { QUERY_DEFAULTS } from '@/lib/query-config';
import { useUserDirectory } from '@/hooks/use-user-directory';
import { useArchiveCard } from '@/hooks/use-archive-card';
import { useRestoreCard } from '@/hooks/use-restore-card';
import {
  cn,
  isTextInputFocused,
  buildPasteFileName,
  arraysEqual,
  formatDateTime,
} from '@/lib/utils';
import { Archive, ArchiveRestore, ChevronLeft, ChevronRight } from 'lucide-react';
import { ROLES } from '@/lib/roles';
import type { BoardData, CardItem, CardSize, Lane, UpdateCardPatch } from '@/types';

export type CardDetailFormHandle = {
  save: () => void;
};

type CardDetailFormProps = {
  card: CardItem;
  onClose: () => void;
  currentUserId?: string;
  currentUserRole?: number;
  lanes?: Lane[];
  boardId?: string;
  sizes?: CardSize[];
  isDirtyRef: React.MutableRefObject<boolean>;
  navPosition?: string | null;
  onNavigatePrev?: () => void;
  onNavigateNext?: () => void;
};

export const CardDetailForm = forwardRef<CardDetailFormHandle, CardDetailFormProps>(
  function CardDetailForm(
    {
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
    },
    ref,
  ) {
    const queryClient = useQueryClient();
    const dialogRef = useRef<HTMLDivElement>(null);
    const bodyRef = useRef<HTMLDivElement>(null);
    const { width: commentsWidth, isDragging, onMouseDown } = usePanelResize(bodyRef);

    const isArchived = card.isArchived;

    const [name, setName] = useState(card.name);
    const [currentLaneId, setCurrentLaneId] = useState(card.laneId);
    const [description, setDescription] = useState(card.descriptionMarkdown ?? '');
    const [sizeId, setSizeId] = useState(card.sizeId);
    const [selectedLabelIds, setSelectedLabelIds] = useState<string[] | null>(null);
    const [isEditingDescription, setIsEditingDescription] = useState(false);
    const [isConfirmingDelete, setIsConfirmingDelete] = useState(false);
    const [showArchiveActions, setShowArchiveActions] = useState(false);
    const [restoreLaneId, setRestoreLaneId] = useState<string | null>(null);
    const [showRestorePicker, setShowRestorePicker] = useState(false);
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
        if (isArchived) return;
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
      [isArchived, pasteMutation],
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
      currentUserRole === ROLES.Administrator ||
      (currentUserRole === ROLES.Human && card.createdByUserId === currentUserId);

    const { getUserName } = useUserDirectory();

    const labelsQuery = useQuery({
      queryKey: queryKeys.cards.labels(card.id),
      queryFn: () => fetchCardLabels(card.id),
      ...QUERY_DEFAULTS.labels,
    });

    const allLabelsQuery = useQuery({
      queryKey: queryKeys.labels.all(boardId as string),
      queryFn: () => fetchLabels(boardId as string),
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
      !isArchived &&
      (name !== card.name ||
        description !== (card.descriptionMarkdown ?? '') ||
        sizeId !== card.sizeId ||
        currentLaneId !== card.laneId ||
        !arraysEqual(effectiveLabelIds, originalLabelIds));

    useEffect(() => {
      isDirtyRef.current = isDirty;
    }, [isDirty, isDirtyRef]);

    const updateMutation = useMutation({
      mutationFn: (patch: UpdateCardPatch) => updateCard(card.id, patch),
      onSuccess: (updatedCard) => {
        if (boardId) {
          queryClient.setQueryData<BoardData>(queryKeys.boards.data(boardId), (old) =>
            old
              ? {
                  ...old,
                  cards: old.cards.map((c) => (c.id === card.id ? { ...c, ...updatedCard } : c)),
                }
              : old,
          );
          queryClient.invalidateQueries({ queryKey: queryKeys.boards.data(boardId) });
        }
        queryClient.invalidateQueries({ queryKey: queryKeys.cards.labels(card.id) });
        isDirtyRef.current = false;
        onClose();
      },
      onError: (error: unknown) => {
        console.error('Failed to update card:', error);
      },
    });

    const deleteMutation = useMutation({
      mutationFn: () => deleteCard(card.id),
      onSuccess: () => {
        if (boardId) {
          queryClient.setQueryData<BoardData>(queryKeys.boards.data(boardId), (old) =>
            old ? { ...old, cards: old.cards.filter((c) => c.id !== card.id) } : old,
          );
          queryClient.invalidateQueries({ queryKey: queryKeys.boards.cards(boardId) });
        }
        isDirtyRef.current = false;
        onClose();
      },
      onError: (error: unknown) => {
        console.error('Failed to delete card:', error);
      },
    });

    const archiveMutation = useArchiveCard({
      cardId: card.id,
      boardId,
      onSuccess: () => {
        isDirtyRef.current = false;
        onClose();
      },
    });

    const restoreMutation = useRestoreCard({
      cardId: card.id,
      boardId,
      onSuccess: () => {
        isDirtyRef.current = false;
        onClose();
      },
    });

    const handleSave = useCallback(() => {
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
    }, [
      isDirty,
      isDirtyRef,
      onClose,
      name,
      card.name,
      description,
      card.descriptionMarkdown,
      sizeId,
      card.sizeId,
      currentLaneId,
      card.laneId,
      effectiveLabelIds,
      originalLabelIds,
      updateMutation,
    ]);

    useImperativeHandle(ref, () => ({ save: handleSave }), [handleSave]);

    const handleDelete = () => {
      if (!isConfirmingDelete) {
        setIsConfirmingDelete(true);
        return;
      }
      deleteMutation.mutate();
    };

    const handleArchiveClick = () => {
      if (!showArchiveActions) {
        setShowArchiveActions(true);
        return;
      }
    };

    const handleArchiveConfirm = () => {
      archiveMutation.mutate();
    };

    const handleRestore = () => {
      if (!showRestorePicker) {
        setShowRestorePicker(true);
        if (lanes && lanes.length > 0) {
          setRestoreLaneId(lanes[0].id);
        }
        return;
      }
      if (restoreLaneId) {
        restoreMutation.mutate(restoreLaneId);
      }
    };

    const handleClose = () => {
      onClose();
    };

    return (
      <div
        ref={dialogRef}
        tabIndex={-1}
        className="flex min-h-0 flex-1 flex-col overflow-hidden outline-none"
      >
        {/* Paste feedback */}
        {pasteStatus && (
          <div className="bg-primary/10 text-primary border-b px-6 py-2 text-sm">{pasteStatus}</div>
        )}

        {/* Archived banner */}
        {isArchived && (
          <div className="flex items-center gap-2 border-b bg-accent/10 px-6 py-2">
            <Archive className="h-4 w-4 text-accent-foreground" />
            <span className="text-sm font-medium text-accent-foreground">
              This card is archived
            </span>
          </div>
        )}

        {/* Header */}
        <DialogHeader className="border-b px-6 pt-6 pb-4">
          <div className="flex items-center gap-2">
            <DialogDescription className="text-xs">#{card.number}</DialogDescription>
            {isArchived && <Badge className="bg-accent/20 text-accent-foreground">Archived</Badge>}
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
                maxLength={120}
                disabled={isArchived}
                className={cn(
                  'border-none bg-transparent px-0 text-xl font-semibold shadow-none focus-visible:ring-0',
                  isArchived && 'cursor-default opacity-70',
                )}
              />
            </div>
          </div>
          <div className="flex flex-wrap items-center gap-3 pt-2">
            {sizes && sizes.length > 0 && (
              <Tooltip>
                <TooltipTrigger render={<span />}>
                  <Select
                    value={sizeId}
                    onValueChange={(v) => v && setSizeId(v)}
                    disabled={isArchived}
                  >
                    <SelectTrigger className={cn('w-36', isArchived && 'opacity-70')}>
                      <SelectValue>{sizes.find((s) => s.id === sizeId)?.name ?? '?'}</SelectValue>
                    </SelectTrigger>
                    <SelectContent>
                      {sizes.map((s) => (
                        <SelectItem key={s.id} value={s.id}>
                          {s.name}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </TooltipTrigger>
                <TooltipContent>{sizes.find((s) => s.id === sizeId)?.name ?? '?'}</TooltipContent>
              </Tooltip>
            )}
            {lanes && lanes.length > 0 && !isArchived && (
              <Tooltip>
                <TooltipTrigger render={<span />}>
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
                </TooltipTrigger>
                <TooltipContent>
                  {lanes.find((l) => l.id === currentLaneId)?.name ?? '?'}
                </TooltipContent>
              </Tooltip>
            )}
            {!isArchived && (
              <LabelPicker
                allLabels={allLabelsQuery.data ?? []}
                assignedLabels={assignedLabels}
                onAdd={(id) => setSelectedLabelIds((prev) => [...(prev ?? originalLabelIds), id])}
                onRemove={(id) =>
                  setSelectedLabelIds((prev) => (prev ?? originalLabelIds).filter((x) => x !== id))
                }
              />
            )}
            {isArchived && assignedLabels.length > 0 && (
              <div className="flex flex-wrap gap-1">
                {assignedLabels.map((label) => (
                  <Badge
                    key={label.id}
                    className="opacity-70"
                    style={
                      label.color ? { backgroundColor: label.color, color: '#fff' } : undefined
                    }
                  >
                    {label.name}
                  </Badge>
                ))}
              </div>
            )}
          </div>
        </DialogHeader>

        {/* Two-column body (stacked on mobile) */}
        <div
          ref={bodyRef}
          className="flex flex-1 gap-0 overflow-hidden max-md:flex-col max-md:overflow-y-auto"
        >
          {/* Left column — details */}
          <div className="min-w-0 flex-1 px-6 py-4 md:overflow-y-auto">
            {/* Description */}
            <div className="mb-4">
              {!isArchived && (
                <div className="mb-2 flex items-center gap-1">
                  <Button
                    variant={!isEditingDescription ? 'secondary' : 'ghost'}
                    size="xs"
                    onClick={() => setIsEditingDescription(false)}
                  >
                    Preview
                  </Button>
                  <Button
                    variant={isEditingDescription ? 'secondary' : 'ghost'}
                    size="xs"
                    onClick={() => setIsEditingDescription(true)}
                  >
                    Edit
                  </Button>
                </div>
              )}
              {isEditingDescription && !isArchived ? (
                <Textarea
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  rows={16}
                  className="font-mono text-sm"
                  placeholder="Write a description..."
                />
              ) : (
                <div className="prose prose-sm max-w-none overflow-x-auto rounded-md border bg-muted/30 p-4 text-sm text-foreground">
                  {description ? (
                    <ReactMarkdown remarkPlugins={[remarkGfm]}>{description}</ReactMarkdown>
                  ) : (
                    <p className="italic text-muted-foreground">
                      {isArchived
                        ? 'No description.'
                        : 'No description yet. Switch to Edit to add one.'}
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
                readOnly={isArchived}
              />
            </div>

            {/* Metadata */}
            <div className="mt-4 text-xs text-muted-foreground">
              <p>
                Created by {getUserName(card.createdByUserId)} · {formatDateTime(card.createdAtUtc)}
              </p>
              <p>
                Updated by {getUserName(card.lastUpdatedByUserId)} ·{' '}
                {formatDateTime(card.lastUpdatedAtUtc)}
              </p>
            </div>
          </div>

          {/* Drag handle (desktop only) — wide hit area, thin visible line via pseudo-element */}
          <div
            onMouseDown={onMouseDown}
            className={cn(
              'relative hidden w-3 shrink-0 cursor-col-resize md:block',
              'after:absolute after:inset-y-0 after:left-1/2 after:w-px after:-translate-x-1/2 after:bg-border after:transition-colors',
              isDragging ? 'after:bg-primary/50' : 'hover:after:bg-primary/50',
            )}
          />

          {/* Right column — comments (below on mobile) */}
          <div
            className="comments-panel-resizable flex shrink-0 flex-col border-border px-5 pt-2 pb-4 max-md:w-full max-md:border-t md:overflow-y-auto"
            style={{ '--comments-width': `${Math.round(commentsWidth)}px` } as React.CSSProperties}
          >
            <h3 className="mb-3 text-sm font-semibold">Comments</h3>
            <CardComments
              cardId={card.id}
              currentUserId={currentUserId}
              currentUserRole={currentUserRole}
              readOnly={isArchived}
            />
          </div>
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between border-t px-6 py-3">
          {isArchived ? (
            /* Archived card footer */
            <div className="flex items-center gap-2">
              {showRestorePicker && lanes && lanes.length > 0 ? (
                <div className="flex items-center gap-2">
                  <Select
                    value={restoreLaneId ?? ''}
                    onValueChange={(v) => v && setRestoreLaneId(v)}
                  >
                    <SelectTrigger className="w-40">
                      <SelectValue placeholder="Select lane">
                        {lanes.find((l) => l.id === restoreLaneId)?.name ?? 'Select lane'}
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
                  <Button
                    size="sm"
                    onClick={handleRestore}
                    disabled={!restoreLaneId || restoreMutation.isPending}
                  >
                    <ArchiveRestore className="mr-1 h-4 w-4" />
                    {restoreMutation.isPending ? 'Restoring...' : 'Restore'}
                  </Button>
                  <Button variant="outline" size="sm" onClick={() => setShowRestorePicker(false)}>
                    Cancel
                  </Button>
                </div>
              ) : (
                <Button size="sm" onClick={handleRestore}>
                  <ArchiveRestore className="mr-1 h-4 w-4" />
                  Restore
                </Button>
              )}
              {canDelete && !showRestorePicker && (
                <Button
                  variant="destructive"
                  size="sm"
                  onClick={handleDelete}
                  disabled={deleteMutation.isPending}
                >
                  {isConfirmingDelete ? 'This cannot be undone. Confirm delete?' : 'Delete'}
                </Button>
              )}
            </div>
          ) : showArchiveActions ? (
            /* Active card — expanded archive actions */
            <div className="flex items-center gap-2">
              <Button size="sm" onClick={handleArchiveConfirm} disabled={archiveMutation.isPending}>
                <Archive className="mr-1 h-4 w-4" />
                {archiveMutation.isPending ? 'Archiving...' : 'Archive'}
              </Button>
              {canDelete && (
                <Button
                  variant="destructive"
                  size="sm"
                  onClick={handleDelete}
                  disabled={deleteMutation.isPending}
                >
                  {isConfirmingDelete ? 'This cannot be undone. Confirm?' : 'Delete'}
                </Button>
              )}
              <Button
                variant="outline"
                size="sm"
                onClick={() => {
                  setShowArchiveActions(false);
                  setIsConfirmingDelete(false);
                }}
              >
                Cancel
              </Button>
            </div>
          ) : canDelete ? (
            /* Active card — default state with archive button */
            <Button variant="outline" size="sm" onClick={handleArchiveClick}>
              <Archive className="mr-1 h-4 w-4" />
              Archive
            </Button>
          ) : (
            <div />
          )}
          {!isArchived && !showArchiveActions && (
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
          )}
          {isArchived && (
            <Button variant="outline" size="sm" onClick={handleClose}>
              Close
            </Button>
          )}
        </div>
      </div>
    );
  },
);
