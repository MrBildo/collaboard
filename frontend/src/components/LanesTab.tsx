import { useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import { Separator } from '@/components/ui/separator';
import {
  EditableListContainer,
  EditableListRow,
  EditFormActions,
  ItemActions,
} from '@/components/editable-list';
import { useEditableList } from '@/hooks/use-editable-list';
import { createLane, deleteLane, fetchLanes, updateLane } from '@/lib/api';
import type { UpdateLanePatch } from '@/types';
import { queryKeys } from '@/lib/query-keys';
import { QUERY_DEFAULTS } from '@/lib/query-config';

type LanesTabProps = {
  boardId: string;
};

export function LanesTab({ boardId }: LanesTabProps) {
  const queryClient = useQueryClient();
  const nameInputRef = useRef<HTMLInputElement>(null);
  const [newName, setNewName] = useState('');
  const [newPosition, setNewPosition] = useState('');
  const [editName, setEditName] = useState('');
  const [editPosition, setEditPosition] = useState('');
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

  const createMutation = useMutation({
    mutationFn: () => {
      const lanes = lanesQuery.data ?? [];
      const pos = newPosition
        ? parseInt(newPosition, 10)
        : (lanes.length > 0 ? Math.max(...lanes.map((l) => l.position)) + 1 : 0);
      return createLane(boardId, newName.trim(), pos);
    },
    onSuccess: () => {
      invalidate();
      setNewName('');
      setNewPosition('');
      setTimeout(() => nameInputRef.current?.focus(), 0);
    },
    onError: (err) => {
      list.setDeleteError(err instanceof Error ? err.message : 'Failed to create lane.');
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, patch }: { id: string; patch: UpdateLanePatch }) =>
      updateLane(id, patch),
    onSuccess: () => {
      invalidate();
      list.setEditingId(null);
    },
  });

  const deleteMutation = useMutation({
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

  const startEdit = (id: string, name: string, position: number) => {
    list.startEdit(id);
    setEditName(name);
    setEditPosition(String(position));
  };

  const saveEdit = () => {
    if (!list.editingId) return;
    const patch: UpdateLanePatch = {};
    const lane = lanesQuery.data?.find((l) => l.id === list.editingId);
    if (!lane) return;
    if (editName.trim() !== lane.name) patch.name = editName.trim();
    const pos = parseInt(editPosition, 10);
    if (!isNaN(pos) && pos !== lane.position) patch.position = pos;
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
                    className="h-7"
                    placeholder="Lane name"
                  />
                  <Input
                    type="number"
                    value={editPosition}
                    onChange={(e) => setEditPosition(e.target.value)}
                    className="h-7 w-20"
                    placeholder="Position"
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
                  <Badge variant="secondary">pos {lane.position}</Badge>
                </div>
                <ItemActions
                  isConfirmingDelete={list.confirmDeleteId === lane.id}
                  isDeleting={deleteMutation.isPending}
                  onEdit={() => startEdit(lane.id, lane.name, lane.position)}
                  onDelete={() => handleDelete(lane.id)}
                />
              </>
            )}
          </EditableListRow>
        ))}
      </EditableListContainer>

      <Separator />

      <div>
        <h3 className="mb-3 text-sm font-medium">Add Lane</h3>
        <div className="flex items-end gap-2">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="lane-name">Name</Label>
            <Input
              ref={nameInputRef}
              id="lane-name"
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') handleCreate(); }}
              placeholder="e.g. In Progress"
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="lane-position">Position</Label>
            <Input
              id="lane-position"
              type="number"
              value={newPosition}
              onChange={(e) => setNewPosition(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') handleCreate(); }}
              placeholder="Auto"
              className="w-20"
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
