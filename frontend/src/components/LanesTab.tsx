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
import { useLanesReorder } from '@/hooks/use-lanes-reorder';
import { createLane, deleteLane, fetchLanes, updateLane } from '@/lib/api';
import { queryKeys } from '@/lib/query-keys';
import { QUERY_DEFAULTS } from '@/lib/query-config';
import { cn } from '@/lib/utils';
import type { Lane, UpdateLanePatch } from '@/types';

type LanesTabProps = {
  boardId: string;
};

// A draggable lane row. The drag handle (GripVertical) carries the dnd-kit
// listeners; the rest of the row keeps its edit/delete affordances. While a row
// is in edit mode the handle is disabled so a name-edit drag can't fire. The
// handle sets `touch-action: none` so a touch-press on it yields the gesture to
// the drag (TouchSensor delay) instead of scrolling the dialog panel (#305).
type SortableLaneRowProps = {
  lane: Lane;
  isEditing: boolean;
  children: ReactNode;
};

function SortableLaneRow({ lane, isEditing, children }: SortableLaneRowProps) {
  const { setNodeRef, attributes, listeners, transform, transition, isDragging } = useSortable({
    id: lane.id,
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
        aria-label={`Reorder ${lane.name}`}
        disabled={isEditing}
        // size-11 (44px) overrides the icon size's 32px so the handle is a
        // finger-sized touch target (#305 is the mobile path); touch-none yields
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

export function LanesTab({ boardId }: LanesTabProps) {
  const queryClient = useQueryClient();
  const nameInputRef = useRef<HTMLInputElement>(null);
  const [newName, setNewName] = useState('');
  const [editName, setEditName] = useState('');
  const list = useEditableList();

  const lanesQuery = useQuery({
    queryKey: queryKeys.lanes.all(boardId),
    queryFn: () => fetchLanes(boardId),
    ...QUERY_DEFAULTS.boardData,
  });

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: queryKeys.lanes.all(boardId) });
    queryClient.invalidateQueries({ queryKey: queryKeys.boards.data(boardId) });
  };

  // Admin-tab mutations are inline tier (card #203, spec §2d) — the operator is
  // looking at the list, so create/update/delete failures surface inline via
  // `list.setDeleteError` → <EditableListContainer>. skipToast keeps the floor
  // quiet; the call site owns the surface.
  // Reordering is available here (drag the grip handle — #305) and on the board
  // (drag a lane header — #278); a new lane is appended at the end (max
  // position + 1). Drag it into place after creating.
  const createMutation = useMutation({
    meta: { skipToast: true },
    mutationFn: () => {
      const lanes = lanesQuery.data ?? [];
      const pos = lanes.length > 0 ? Math.max(...lanes.map((l) => l.position)) + 1 : 0;
      return createLane(boardId, newName.trim(), pos);
    },
    onSuccess: () => {
      invalidate();
      setNewName('');
      setTimeout(() => nameInputRef.current?.focus(), 0);
    },
    onError: (err) => {
      list.setDeleteError(err instanceof Error ? err.message : 'Failed to create lane.');
    },
  });

  const updateMutation = useMutation({
    meta: { skipToast: true },
    mutationFn: ({ id, patch }: { id: string; patch: UpdateLanePatch }) => updateLane(id, patch),
    onSuccess: () => {
      invalidate();
      list.setEditingId(null);
    },
    onError: (err) => {
      list.setDeleteError(err instanceof Error ? err.message : 'Failed to update lane.');
    },
  });

  const deleteMutation = useMutation({
    meta: { skipToast: true },
    mutationFn: (id: string) => deleteLane(id),
    onSuccess: () => {
      invalidate();
      list.clearDelete();
    },
    onError: () => {
      list.setDeleteError('Cannot delete lane — it may still contain cards.');
    },
  });

  const lanes = lanesQuery.data ?? [];
  const reorder = useLanesReorder(boardId, lanes);
  const orderedLanes = reorder.localLanes;
  const activeLane = orderedLanes.find((l) => l.id === reorder.activeLaneId) ?? null;

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
    const patch: UpdateLanePatch = {};
    const lane = lanes.find((l) => l.id === list.editingId);
    if (!lane) return;
    if (editName.trim() !== lane.name) patch.name = editName.trim();
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

  const renderRowContent = (lane: Lane): ReactNode =>
    list.editingId === lane.id ? (
      <>
        <div className="flex flex-1 items-center gap-2">
          <Input
            value={editName}
            onChange={(e) => setEditName(e.target.value)}
            maxLength={40}
            className="h-7"
            placeholder="Lane name"
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
          <span className="font-medium">{lane.name}</span>
        </div>
        <ItemActions
          isConfirmingDelete={list.confirmDeleteId === lane.id}
          isDeleting={deleteMutation.isPending}
          onEdit={() => startEdit(lane.id, lane.name)}
          onDelete={() => handleDelete(lane.id)}
        />
      </>
    );

  return (
    <div className="flex h-full min-h-0 flex-col gap-4">
      {/* Scroll zone — fills remaining height, scrolls internally on long lists. */}
      <div className="min-h-0 flex-1 overflow-y-auto">
        <EditableListContainer error={list.deleteError}>
          <DndContext
            sensors={reorder.sensors}
            collisionDetection={closestCenter}
            onDragStart={reorder.onDragStart}
            onDragOver={reorder.onDragOver}
            onDragEnd={reorder.onDragEnd}
          >
            <SortableContext
              items={orderedLanes.map((l) => l.id)}
              strategy={verticalListSortingStrategy}
            >
              <div className="flex flex-col divide-y divide-border">
                {orderedLanes.map((lane) => (
                  <SortableLaneRow key={lane.id} lane={lane} isEditing={list.editingId === lane.id}>
                    {renderRowContent(lane)}
                  </SortableLaneRow>
                ))}
              </div>
            </SortableContext>
            <DragOverlay>
              {activeLane ? (
                <div className="flex items-center gap-1 rounded-lg border bg-card px-2 py-3 shadow-lg">
                  <span className="flex size-11 shrink-0 items-center justify-center text-muted-foreground">
                    <GripVertical className="h-4 w-4" />
                  </span>
                  <span className="font-medium">{activeLane.name}</span>
                </div>
              ) : null}
            </DragOverlay>
          </DndContext>
        </EditableListContainer>
      </div>

      {/* Pinned footer — never scrolls; the Add form stays reachable (#310). */}
      <div className="shrink-0">
        <Separator className="mb-4" />
        <h3 className="mb-1 text-sm font-medium">Add Lane</h3>
        <p className="mb-3 text-xs text-muted-foreground">
          New lanes are added at the end. Drag the grip handle to reorder.
        </p>
        <div className="flex items-end gap-2">
          <div className="flex flex-1 flex-col gap-1.5">
            <Label htmlFor="lane-name">Name</Label>
            <Input
              ref={nameInputRef}
              id="lane-name"
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') handleCreate();
              }}
              maxLength={40}
              placeholder="e.g. In Progress"
            />
          </div>
          <Button
            type="button"
            onClick={handleCreate}
            disabled={createMutation.isPending || !newName.trim()}
          >
            {createMutation.isPending ? 'Adding...' : 'Add Lane'}
          </Button>
        </div>
      </div>
    </div>
  );
}
