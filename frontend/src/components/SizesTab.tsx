import { useRef, useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { DndContext, DragOverlay, closestCenter } from '@dnd-kit/core';
import { SortableContext, useSortable, verticalListSortingStrategy } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { GripVertical } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Separator } from '@/components/ui/separator';
import { EditableListContainer, EditFormActions, ItemActions } from '@/components/editable-list';
import { useEditableList } from '@/hooks/use-editable-list';
import { useIsMobile } from '@/hooks/use-is-mobile';
import { useSizesReorder } from '@/hooks/use-sizes-reorder';
import { createSize, deleteSize, fetchSizes, updateSize } from '@/lib/api';
import { queryKeys } from '@/lib/query-keys';
import { QUERY_DEFAULTS } from '@/lib/query-config';
import { cn } from '@/lib/utils';
import type { CardSize, UpdateSizePatch } from '@/types';

type SizesTabProps = {
  boardId: string;
};

// A static (non-draggable) size row for mobile view. Drag-and-drop is desktop-only
// (#312), so on mobile the row has no grip handle at all — no dead affordance that
// invites a drag it won't honor. The edit/delete actions stay; only reordering is
// gone (it has no non-drag alternative, the accepted trade per #312).
type StaticSizeRowProps = {
  children: ReactNode;
};

function StaticSizeRow({ children }: StaticSizeRowProps) {
  return (
    <div className="flex items-center gap-1 bg-card px-2 py-3 transition-colors hover:bg-muted/50">
      <div className="flex flex-1 items-center justify-between gap-2">{children}</div>
    </div>
  );
}

// A draggable size row. The drag handle (GripVertical) carries the dnd-kit
// listeners; the rest of the row keeps its edit/delete affordances. While a row
// is in edit mode the handle is disabled so a name-edit drag can't fire. The
// handle sets `touch-action: none` so a touch-press on it yields the gesture to
// the drag (TouchSensor delay) instead of scrolling the dialog panel (#306).
type SortableSizeRowProps = {
  size: CardSize;
  isEditing: boolean;
  children: ReactNode;
};

function SortableSizeRow({ size, isEditing, children }: SortableSizeRowProps) {
  const { setNodeRef, attributes, listeners, transform, transition, isDragging } = useSortable({
    id: size.id,
    disabled: isEditing,
  });

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
  };

  return (
    <div
      ref={setNodeRef}
      style={style}
      className={cn(
        'flex items-center gap-1 bg-card px-2 py-3 transition-colors hover:bg-muted/50',
        isDragging && 'opacity-50',
      )}
    >
      <Button
        type="button"
        variant="ghost"
        size="icon"
        aria-label={`Reorder ${size.name}`}
        disabled={isEditing}
        // size-11 (44px) overrides the icon size's 32px so the handle is a
        // finger-sized touch target (#306 is the mobile path); touch-none yields
        // the gesture to the TouchSensor drag instead of scrolling the panel.
        className="size-11 shrink-0 cursor-grab touch-none text-muted-foreground active:cursor-grabbing disabled:opacity-30"
        {...attributes}
        {...listeners}
      >
        <GripVertical className="h-4 w-4" />
      </Button>
      <div className="flex flex-1 items-center justify-between gap-2">{children}</div>
    </div>
  );
}

export function SizesTab({ boardId }: SizesTabProps) {
  const queryClient = useQueryClient();
  const nameInputRef = useRef<HTMLInputElement>(null);
  const [newName, setNewName] = useState('');
  const [editName, setEditName] = useState('');
  const list = useEditableList();
  const isMobile = useIsMobile();

  const sizesQuery = useQuery({
    queryKey: queryKeys.sizes.all(boardId),
    queryFn: () => fetchSizes(boardId),
    ...QUERY_DEFAULTS.boardData,
  });

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: queryKeys.sizes.all(boardId) });
    queryClient.invalidateQueries({ queryKey: queryKeys.boards.data(boardId) });
  };

  // Admin-tab mutations are inline tier (card #203, spec §2d) — failures surface
  // inline via `list.setDeleteError` → <EditableListContainer>. skipToast keeps
  // the floor quiet; the call site owns the surface.
  // Ordinal is server-managed now (drag the grip handle to reorder — #306, F2);
  // a new size is appended at the end (max ordinal + 1). Drag it into place after
  // creating.
  const createMutation = useMutation({
    meta: { skipToast: true },
    mutationFn: () => {
      const sizes = sizesQuery.data ?? [];
      const ord = sizes.length > 0 ? Math.max(...sizes.map((s) => s.ordinal)) + 1 : 0;
      return createSize(boardId, newName.trim(), ord);
    },
    onSuccess: () => {
      invalidate();
      setNewName('');
      setTimeout(() => nameInputRef.current?.focus(), 0);
    },
    onError: (err) => {
      list.setDeleteError(err instanceof Error ? err.message : 'Failed to create size.');
    },
  });

  const updateMutation = useMutation({
    meta: { skipToast: true },
    mutationFn: ({ id, patch }: { id: string; patch: UpdateSizePatch }) => updateSize(id, patch),
    onSuccess: () => {
      invalidate();
      list.setEditingId(null);
    },
    onError: (err) => {
      list.setDeleteError(err instanceof Error ? err.message : 'Failed to update size.');
    },
  });

  const deleteMutation = useMutation({
    meta: { skipToast: true },
    mutationFn: (id: string) => deleteSize(id),
    onSuccess: () => {
      invalidate();
      list.clearDelete();
    },
    onError: () => {
      list.setDeleteError('Cannot delete size — it may still be in use by cards.');
    },
  });

  const sizes = sizesQuery.data ?? [];
  const reorder = useSizesReorder(boardId, sizes);
  const orderedSizes = reorder.localSizes;
  const activeSize = orderedSizes.find((s) => s.id === reorder.activeSizeId) ?? null;

  const handleCreate = () => {
    if (!newName.trim()) return;
    createMutation.mutate();
  };

  const startEdit = (id: string, name: string) => {
    list.startEdit(id);
    setEditName(name);
  };

  const saveEdit = () => {
    if (!list.editingId) return;
    const patch: UpdateSizePatch = {};
    const size = sizes.find((s) => s.id === list.editingId);
    if (!size) return;
    if (editName.trim() !== size.name) patch.name = editName.trim();
    if (Object.keys(patch).length > 0) {
      updateMutation.mutate({ id: list.editingId, patch });
    } else {
      list.cancelEdit();
    }
  };

  const handleDelete = (id: string) => {
    if (list.confirmDeleteId === id) {
      deleteMutation.mutate(id);
    } else {
      list.confirmDelete(id);
    }
  };

  const renderRowContent = (size: CardSize): ReactNode =>
    list.editingId === size.id ? (
      <>
        <div className="flex flex-1 items-center gap-2">
          <Input
            value={editName}
            onChange={(e) => setEditName(e.target.value)}
            maxLength={20}
            className="h-7"
            placeholder="Size name"
          />
        </div>
        <EditFormActions
          onSave={saveEdit}
          onCancel={list.cancelEdit}
          isPending={updateMutation.isPending}
        />
      </>
    ) : (
      <>
        <div className="flex items-center gap-3">
          <span className="font-medium">{size.name}</span>
        </div>
        <ItemActions
          isConfirmingDelete={list.confirmDeleteId === size.id}
          isDeleting={deleteMutation.isPending}
          onEdit={() => startEdit(size.id, size.name)}
          onDelete={() => handleDelete(size.id)}
        />
      </>
    );

  return (
    <div className="flex h-full min-h-0 flex-col gap-4">
      {/* Scroll zone — fills remaining height, scrolls internally on long lists. */}
      <div className="min-h-0 flex-1 overflow-y-auto">
        <EditableListContainer error={list.deleteError}>
          {isMobile ? (
            // Mobile: drag-drop is desktop-only (#312). Render a static list — no
            // DndContext, no sensors, no grip handle. Reordering is unavailable
            // here; edit/delete stay.
            <div className="flex flex-col divide-y divide-border">
              {orderedSizes.map((size) => (
                <StaticSizeRow key={size.id}>{renderRowContent(size)}</StaticSizeRow>
              ))}
            </div>
          ) : (
            <DndContext
              sensors={reorder.sensors}
              collisionDetection={closestCenter}
              onDragStart={reorder.onDragStart}
              onDragOver={reorder.onDragOver}
              onDragEnd={reorder.onDragEnd}
            >
              <SortableContext
                items={orderedSizes.map((s) => s.id)}
                strategy={verticalListSortingStrategy}
              >
                <div className="flex flex-col divide-y divide-border">
                  {orderedSizes.map((size) => (
                    <SortableSizeRow
                      key={size.id}
                      size={size}
                      isEditing={list.editingId === size.id}
                    >
                      {renderRowContent(size)}
                    </SortableSizeRow>
                  ))}
                </div>
              </SortableContext>
              <DragOverlay>
                {activeSize ? (
                  <div className="flex items-center gap-1 rounded-lg border bg-card px-2 py-3 shadow-lg">
                    <span className="flex size-11 shrink-0 items-center justify-center text-muted-foreground">
                      <GripVertical className="h-4 w-4" />
                    </span>
                    <span className="font-medium">{activeSize.name}</span>
                  </div>
                ) : null}
              </DragOverlay>
            </DndContext>
          )}
        </EditableListContainer>
      </div>

      {/* Pinned footer — never scrolls; the Add form stays reachable (#310). */}
      <div className="shrink-0">
        <Separator className="mb-4" />
        <h3 className="mb-1 text-sm font-medium">Add Size</h3>
        <p className="mb-3 text-xs text-muted-foreground">
          {isMobile
            ? 'New sizes are added at the end. Reordering sizes is available on desktop.'
            : 'New sizes are added at the end. Drag the grip handle to reorder.'}
        </p>
        <div className="flex items-end gap-2">
          <div className="flex flex-1 flex-col gap-1.5">
            <Label htmlFor="size-name">Name</Label>
            <Input
              ref={nameInputRef}
              id="size-name"
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') handleCreate();
              }}
              maxLength={20}
              placeholder="e.g. XXL"
            />
          </div>
          <Button
            type="button"
            onClick={handleCreate}
            disabled={createMutation.isPending || !newName.trim()}
          >
            {createMutation.isPending ? 'Adding...' : 'Add Size'}
          </Button>
        </div>
      </div>
    </div>
  );
}
