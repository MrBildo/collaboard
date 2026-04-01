import { useCallback, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { MarkdownRenderer } from '@/components/MarkdownRenderer';
import {
  Dialog,
  DialogContent,
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
import {
  cancelTempCard,
  createCard,
  createTempCard,
  fetchBoardData,
  fetchLabels,
  finalizeCard,
  uploadAttachment,
} from '@/lib/api';
import { LabelPicker } from '@/components/LabelPicker';
import { CardAttachments } from '@/components/CardAttachments';
import { validateFiles } from '@/lib/attachments';
import { usePasteAttachment } from '@/hooks/use-paste-attachment';
import { queryKeys } from '@/lib/query-keys';
import { QUERY_DEFAULTS } from '@/lib/query-config';
import { Loader2 } from 'lucide-react';
import type { PendingFile } from '@/lib/attachments';
import type { BoardData, CardSize, Lane } from '@/types';

type CreateCardDialogProps = {
  boardId: string;
  lanes: Lane[];
  sizes: CardSize[];
  open: boolean;
  onOpenChange: (open: boolean) => void;
  defaultLaneId?: string;
};

export function CreateCardDialog({
  boardId,
  lanes,
  sizes,
  open,
  onOpenChange,
  defaultLaneId,
}: CreateCardDialogProps) {
  const queryClient = useQueryClient();
  const dialogContentRef = useRef<HTMLDivElement>(null);

  const defaultSizeId =
    sizes.length > 0 ? [...sizes].sort((a, b) => a.ordinal - b.ordinal)[0].id : '';
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [sizeId, setSizeId] = useState(defaultSizeId);
  const [laneId, setLaneId] = useState(defaultLaneId ?? lanes[0]?.id ?? '');
  const [selectedLabelIds, setSelectedLabelIds] = useState<string[]>([]);
  const [isPreviewingDescription, setIsPreviewingDescription] = useState(false);

  const [pendingFiles, setPendingFiles] = useState<PendingFile[]>([]);
  const [tempCardId, setTempCardId] = useState<string | null>(null);
  const [isCreating, setIsCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);

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
      const maxPosition = laneCards.length > 0 ? Math.max(...laneCards.map((c) => c.position)) : 0;

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
      queryClient.setQueryData<BoardData>(queryKeys.boards.data(boardId), (old) =>
        old
          ? {
              ...old,
              cards: [...old.cards, newCard],
            }
          : old,
      );
      queryClient.invalidateQueries({ queryKey: queryKeys.boards.data(boardId) });
      handleClose();
    },
    onError: (error: unknown) => {
      console.error('Failed to create card:', error);
    },
  });

  const addFiles = useCallback((files: File[]) => {
    const validated = validateFiles(files);
    setPendingFiles((prev) => [...prev, ...validated]);
  }, []);

  const removeFile = useCallback((fileId: string) => {
    setPendingFiles((prev) => prev.filter((f) => f.id !== fileId));
  }, []);

  const handlePasteFile = useCallback(
    (file: File) => {
      addFiles([file]);
    },
    [addFiles],
  );

  usePasteAttachment({
    onFile: handlePasteFile,
    enabled: open && !isCreating,
    containerRef: dialogContentRef,
  });

  const resetState = () => {
    setName('');
    setDescription('');
    setSizeId(defaultSizeId);
    setLaneId(defaultLaneId ?? lanes[0]?.id ?? '');
    setSelectedLabelIds([]);
    setIsPreviewingDescription(false);
    setPendingFiles([]);
    setTempCardId(null);
    setIsCreating(false);
    setCreateError(null);
  };

  const handleClose = () => {
    resetState();
    onOpenChange(false);
  };

  const handleCancel = async () => {
    if (tempCardId) {
      try {
        await cancelTempCard(tempCardId);
      } catch {
        // Best-effort cleanup
      }
    }
    handleClose();
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim() || !sizeId) return;

    setCreateError(null);

    const filesToUpload = pendingFiles.filter(
      (f) => f.status === 'pending' || f.status === 'error',
    );

    // Case 1: No pending files — use existing flow
    if (pendingFiles.length === 0) {
      createMutation.mutate();
      return;
    }

    // Case 2: Only errored files that cannot be uploaded (size errors)
    // Filter out only size-error files and check if there are any uploadable files
    const uploadableFiles = filesToUpload.filter((f) => !f.error);
    const doneFiles = pendingFiles.filter((f) => f.status === 'done');

    if (uploadableFiles.length === 0 && doneFiles.length === 0) {
      // All files have errors, nothing to upload — cannot proceed with attachments
      setCreateError('Remove failed files before creating the card, or remove all attachments.');
      return;
    }

    setIsCreating(true);

    try {
      // Step 1: Create temp card if not already created
      let cardId = tempCardId;
      if (!cardId) {
        const result = await createTempCard(boardId, {
          laneId,
          name: name.trim(),
          descriptionMarkdown: description || undefined,
          sizeId: sizeId || undefined,
          labelIds: selectedLabelIds.length > 0 ? selectedLabelIds : undefined,
        });
        cardId = result.id;
        setTempCardId(cardId);
      }

      // Step 2: Upload all pending/error files in parallel
      if (uploadableFiles.length > 0) {
        // Mark all uploadable as uploading
        setPendingFiles((prev) =>
          prev.map((f) => {
            const isUploadable = uploadableFiles.some((u) => u.id === f.id);
            return isUploadable ? { ...f, status: 'uploading' as const, error: undefined } : f;
          }),
        );

        const results = await Promise.allSettled(
          uploadableFiles.map(async (pf) => {
            await uploadAttachment(cardId, pf.file);
            return pf.id;
          }),
        );

        // Update statuses based on results
        const succeededIds = new Set<string>();
        const failedMap = new Map<string, string>();

        for (let i = 0; i < results.length; i++) {
          const result = results[i];
          const pf = uploadableFiles[i];
          if (result.status === 'fulfilled') {
            succeededIds.add(pf.id);
          } else {
            const msg =
              result.reason instanceof Error ? result.reason.message : String(result.reason);
            failedMap.set(pf.id, msg);
          }
        }

        setPendingFiles((prev) =>
          prev.map((f) => {
            if (succeededIds.has(f.id)) return { ...f, status: 'done' as const };
            if (failedMap.has(f.id))
              return { ...f, status: 'error' as const, error: failedMap.get(f.id) };
            return f;
          }),
        );

        // If any uploads failed, stop here — user can retry
        if (failedMap.size > 0) {
          setIsCreating(false);
          setCreateError('Some uploads failed. Remove failed files and try again.');
          return;
        }
      }

      // Step 3: All uploads succeeded — finalize
      await finalizeCard(cardId);

      queryClient.invalidateQueries({ queryKey: queryKeys.boards.data(boardId) });
      handleClose();
    } catch (error: unknown) {
      const msg = error instanceof Error ? error.message : 'Failed to create card';
      setCreateError(msg);
      setIsCreating(false);
    }
  };

  const hasFilesWithErrors = pendingFiles.some(
    (f) => f.status === 'error' && f.error?.includes('5MB'),
  );
  const isFormDisabled = isCreating;
  const canSubmit = name.trim().length > 0 && !!sizeId && !isCreating && !createMutation.isPending;

  return (
    <Dialog
      open={open}
      onOpenChange={(isOpen) => {
        if (!isOpen) {
          handleCancel();
        }
      }}
    >
      <DialogContent
        ref={dialogContentRef}
        data-mobile-fullscreen
        className="flex flex-col p-0 md:max-h-[85vh] md:max-w-[60vw]"
      >
        <form onSubmit={handleSubmit} className="flex min-h-0 flex-1 flex-col">
          <DialogHeader className="border-b p-4">
            <DialogTitle>Create Card</DialogTitle>
            <DialogDescription>Add a new card to the board.</DialogDescription>
          </DialogHeader>

          <div className="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto p-4">
            {/* Name */}
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="new-card-name">Name *</Label>
              <Input
                id="new-card-name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="Card name"
                maxLength={120}
                required
                disabled={isFormDisabled}
              />
            </div>

            {/* Description */}
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="new-card-description">Description</Label>
              <div className="mb-1.5 flex items-center gap-1">
                <Button
                  variant={!isPreviewingDescription ? 'secondary' : 'ghost'}
                  size="xs"
                  type="button"
                  onClick={() => setIsPreviewingDescription(false)}
                  disabled={isFormDisabled}
                >
                  Edit
                </Button>
                <Button
                  variant={isPreviewingDescription ? 'secondary' : 'ghost'}
                  size="xs"
                  type="button"
                  onClick={() => setIsPreviewingDescription(true)}
                  disabled={isFormDisabled}
                >
                  Preview
                </Button>
              </div>
              {isPreviewingDescription ? (
                <div className="prose prose-sm max-h-64 max-w-none overflow-y-auto overflow-x-auto rounded-md border bg-muted/30 p-4 text-sm text-foreground">
                  {description.trim() ? (
                    <MarkdownRenderer>{description}</MarkdownRenderer>
                  ) : (
                    <p className="italic text-muted-foreground">Nothing to preview.</p>
                  )}
                </div>
              ) : (
                <Textarea
                  id="new-card-description"
                  className="font-mono md:text-sm"
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  placeholder="Write a description..."
                  rows={4}
                  disabled={isFormDisabled}
                />
              )}
            </div>

            {/* Size */}
            {sizes.length > 0 && (
              <div className="flex flex-col gap-1.5">
                <Label>Size</Label>
                <Select
                  value={sizeId}
                  onValueChange={(v) => v && setSizeId(v)}
                  disabled={isFormDisabled}
                >
                  <SelectTrigger className="w-36">
                    <SelectValue>
                      {sizes.find((s) => s.id === sizeId)?.name ?? 'Select size'}
                    </SelectValue>
                  </SelectTrigger>
                  <SelectContent>
                    {[...sizes]
                      .sort((a, b) => a.ordinal - b.ordinal)
                      .map((s) => (
                        <SelectItem key={s.id} value={s.id}>
                          {s.name}
                        </SelectItem>
                      ))}
                  </SelectContent>
                </Select>
              </div>
            )}

            {/* Lane */}
            <div className="flex flex-col gap-1.5">
              <Label>Lane</Label>
              <Select
                value={laneId}
                onValueChange={(v) => v && setLaneId(v)}
                disabled={isFormDisabled}
              >
                <SelectTrigger className="w-36">
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

            {/* Labels */}
            {(allLabelsQuery.data ?? []).length > 0 && (
              <div className="flex flex-col gap-1.5">
                <Label>Labels</Label>
                <LabelPicker
                  allLabels={allLabelsQuery.data ?? []}
                  assignedLabels={(allLabelsQuery.data ?? []).filter((l) =>
                    selectedLabelIds.includes(l.id),
                  )}
                  onAdd={(id) => setSelectedLabelIds((prev) => [...prev, id])}
                  onRemove={(id) => setSelectedLabelIds((prev) => prev.filter((x) => x !== id))}
                />
              </div>
            )}

            {/* Attachments */}
            <div className="flex flex-col gap-1.5">
              <Label>Attachments</Label>
              <CardAttachments
                mode="pending"
                pendingFiles={pendingFiles}
                onAddFiles={addFiles}
                onRemoveFile={removeFile}
                disabled={isFormDisabled}
              />
            </div>
          </div>

          {/* Error display */}
          {createError && (
            <div className="mx-4 mb-1 rounded-md border border-destructive/30 bg-destructive/5 px-3 py-2 text-sm text-destructive">
              {createError}
            </div>
          )}

          <div className="flex items-center justify-end gap-2 border-t px-4 py-3">
            <Button type="button" variant="outline" size="sm" onClick={handleCancel}>
              Cancel
            </Button>
            <Button type="submit" size="sm" disabled={!canSubmit || hasFilesWithErrors}>
              {isCreating ? (
                <>
                  <Loader2 className="mr-1.5 h-4 w-4 animate-spin" />
                  Creating...
                </>
              ) : createMutation.isPending ? (
                'Creating...'
              ) : (
                'Create'
              )}
            </Button>
          </div>
        </form>
      </DialogContent>
    </Dialog>
  );
}
