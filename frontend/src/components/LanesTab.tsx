import { useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Separator } from '@/components/ui/separator';
import {
  EditableListContainer,
  EditableListRow,
  EditFormActions,
  ItemActions,
} from '@/components/editable-list';
import { useEditableList } from '@/hooks/use-editable-list';
import { createLane, deleteLane, fetchLanes, updateLane } from '@/lib/api';
import { queryKeys } from '@/lib/query-keys';
import { QUERY_DEFAULTS } from '@/lib/query-config';
import type { UpdateLanePatch } from '@/types';

type LanesTabProps = {
  boardId: string;
};

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
  // Reordering now lives on the board (drag a lane header — #278); a new lane is
  // appended at the end (max position + 1). Drag it into place after creating.
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
    const lane = lanesQuery.data?.find((l) => l.id === list.editingId);
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

  const lanes = lanesQuery.data ?? [];

  return (
    <div className="flex flex-col gap-4">
      <EditableListContainer error={list.deleteError}>
        {lanes.map((lane) => (
          <EditableListRow key={lane.id}>
            {list.editingId === lane.id ? (
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
            )}
          </EditableListRow>
        ))}
      </EditableListContainer>

      <Separator />

      <div>
        <h3 className="mb-1 text-sm font-medium">Add Lane</h3>
        <p className="mb-3 text-xs text-muted-foreground">
          New lanes are added at the end. Drag a lane header on the board to reorder.
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
