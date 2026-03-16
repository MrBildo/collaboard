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
import { createSize, deleteSize, fetchSizes, updateSize } from '@/lib/api';
import type { UpdateSizePatch } from '@/types';
import { queryKeys } from '@/lib/query-keys';
import { QUERY_DEFAULTS } from '@/lib/query-config';

type SizesTabProps = {
  boardId: string;
};

export function SizesTab({ boardId }: SizesTabProps) {
  const queryClient = useQueryClient();
  const nameInputRef = useRef<HTMLInputElement>(null);
  const [newName, setNewName] = useState('');
  const [newOrdinal, setNewOrdinal] = useState('');
  const [editName, setEditName] = useState('');
  const [editOrdinal, setEditOrdinal] = useState('');
  const list = useEditableList();

  const sizesQuery = useQuery({
    queryKey: queryKeys.sizes.all(boardId),
    queryFn: () => fetchSizes(boardId),
    ...QUERY_DEFAULTS.boardData,
  });

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: queryKeys.sizes.all(boardId) });
    queryClient.invalidateQueries({ queryKey: queryKeys.boards.data(boardId) });
  };

  const createMutation = useMutation({
    mutationFn: () => {
      const sizes = sizesQuery.data ?? [];
      const ord = newOrdinal
        ? parseInt(newOrdinal, 10)
        : (sizes.length > 0 ? Math.max(...sizes.map((s) => s.ordinal)) + 1 : 0);
      return createSize(boardId, newName.trim(), ord);
    },
    onSuccess: () => {
      invalidate();
      setNewName('');
      setNewOrdinal('');
      setTimeout(() => nameInputRef.current?.focus(), 0);
    },
    onError: (err) => {
      list.setDeleteError(err instanceof Error ? err.message : 'Failed to create size.');
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, patch }: { id: string; patch: UpdateSizePatch }) =>
      updateSize(id, patch),
    onSuccess: () => {
      invalidate();
      list.setEditingId(null);
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteSize(id),
    onSuccess: () => {
      invalidate();
      list.clearDelete();
    },
    onError: () => {
      list.setDeleteError('Cannot delete size — it may still be in use by cards.');
    },
  });

  const handleCreate = () => {
    if (!newName.trim()) return;
    createMutation.mutate();
  };

  const startEdit = (id: string, name: string, ordinal: number) => {
    list.startEdit(id);
    setEditName(name);
    setEditOrdinal(String(ordinal));
  };

  const saveEdit = () => {
    if (!list.editingId) return;
    const patch: UpdateSizePatch = {};
    const size = sizesQuery.data?.find((s) => s.id === list.editingId);
    if (!size) return;
    if (editName.trim() !== size.name) patch.name = editName.trim();
    const ord = parseInt(editOrdinal, 10);
    if (!isNaN(ord) && ord !== size.ordinal) patch.ordinal = ord;
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

  const sizes = sizesQuery.data ?? [];

  return (
    <div className="flex flex-col gap-4">
      <EditableListContainer error={list.deleteError}>
        {sizes.map((size) => (
          <EditableListRow key={size.id}>
            {list.editingId === size.id ? (
              <>
                <div className="flex flex-1 items-center gap-2">
                  <Input
                    value={editName}
                    onChange={(e) => setEditName(e.target.value)}
                    className="h-7"
                    placeholder="Size name"
                  />
                  <Input
                    type="number"
                    value={editOrdinal}
                    onChange={(e) => setEditOrdinal(e.target.value)}
                    className="h-7 w-20"
                    placeholder="Ordinal"
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
                  <Badge variant="secondary">ord {size.ordinal}</Badge>
                </div>
                <ItemActions
                  isConfirmingDelete={list.confirmDeleteId === size.id}
                  isDeleting={deleteMutation.isPending}
                  onEdit={() => startEdit(size.id, size.name, size.ordinal)}
                  onDelete={() => handleDelete(size.id)}
                />
              </>
            )}
          </EditableListRow>
        ))}
      </EditableListContainer>

      <Separator />

      <div>
        <h3 className="mb-3 text-sm font-medium">Add Size</h3>
        <div className="flex items-end gap-2">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="size-name">Name</Label>
            <Input
              ref={nameInputRef}
              id="size-name"
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') handleCreate(); }}
              placeholder="e.g. XXL"
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="size-ordinal">Ordinal</Label>
            <Input
              id="size-ordinal"
              type="number"
              value={newOrdinal}
              onChange={(e) => setNewOrdinal(e.target.value)}
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
            {createMutation.isPending ? 'Adding...' : 'Add Size'}
          </Button>
        </div>
      </div>
    </div>
  );
}
