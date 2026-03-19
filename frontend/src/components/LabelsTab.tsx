import { useState } from 'react';
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
import { LabelColorPicker } from '@/components/LabelColorPicker';
import { createLabel, deleteLabel, fetchLabels, updateLabel } from '@/lib/api';
import { queryKeys } from '@/lib/query-keys';
import { QUERY_DEFAULTS } from '@/lib/query-config';
import type { UpdateLabelPatch } from '@/types';

type LabelsTabProps = {
  boardId: string;
};

export function LabelsTab({ boardId }: LabelsTabProps) {
  const queryClient = useQueryClient();
  const [newName, setNewName] = useState('');
  const [newColor, setNewColor] = useState('#3b82f6');
  const [editName, setEditName] = useState('');
  const [editColor, setEditColor] = useState('');
  const list = useEditableList();

  const labelsQuery = useQuery({
    queryKey: queryKeys.labels.all(boardId),
    queryFn: () => fetchLabels(boardId),
    ...QUERY_DEFAULTS.labels,
  });

  const createMutation = useMutation({
    mutationFn: () => createLabel(boardId, newName.trim(), newColor || undefined),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.labels.all(boardId) });
      setNewName('');
      setNewColor('#3b82f6');
    },
    onError: (error: unknown) => {
      console.error('Failed to create label:', error);
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, patch }: { id: string; patch: UpdateLabelPatch }) =>
      updateLabel(boardId, id, patch),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.labels.all(boardId) });
      list.setEditingId(null);
    },
    onError: (error: unknown) => {
      console.error('Failed to update label:', error);
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteLabel(boardId, id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.labels.all(boardId) });
      list.clearDelete();
    },
    onError: (error: unknown) => {
      console.error('Failed to delete label:', error);
    },
  });

  const handleCreate = () => {
    if (!newName.trim()) return;
    createMutation.mutate();
  };

  const startEdit = (id: string, name: string, color: string | null | undefined) => {
    list.startEdit(id);
    setEditName(name);
    setEditColor(color ?? '#3b82f6');
  };

  const saveEdit = () => {
    if (!list.editingId) return;
    const patch: UpdateLabelPatch = {};
    const label = labelsQuery.data?.find((l) => l.id === list.editingId);
    if (!label) return;
    if (editName.trim() !== label.name) patch.name = editName.trim();
    if (editColor !== (label.color ?? '')) patch.color = editColor;
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

  const labels = labelsQuery.data ?? [];

  return (
    <div className="flex flex-col gap-4">
      <EditableListContainer>
        {labels.map((label) => (
          <EditableListRow key={label.id}>
            {list.editingId === label.id ? (
              <>
                <div className="flex flex-1 items-center gap-2">
                  <LabelColorPicker value={editColor} onValueChange={setEditColor} className="h-7 w-7" />
                  <Input
                    value={editName}
                    onChange={(e) => setEditName(e.target.value)}
                    maxLength={30}
                    className="h-7"
                    placeholder="Label name"
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
                  <span
                    className="inline-block h-4 w-4 shrink-0 rounded-full"
                    style={{ backgroundColor: label.color ?? '#6b7280' }}
                  />
                  <span className="font-medium">{label.name}</span>
                </div>
                <ItemActions
                  isConfirmingDelete={list.confirmDeleteId === label.id}
                  isDeleting={deleteMutation.isPending}
                  onEdit={() => startEdit(label.id, label.name, label.color)}
                  onDelete={() => handleDelete(label.id)}
                />
              </>
            )}
          </EditableListRow>
        ))}
      </EditableListContainer>

      <Separator />

      <div>
        <h3 className="mb-3 text-sm font-medium">Add Label</h3>
        <div className="flex items-end gap-2">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="label-name">Name</Label>
            <Input
              id="label-name"
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
              maxLength={30}
              placeholder="e.g. Bug"
            />
          </div>
          <LabelColorPicker value={newColor} onValueChange={setNewColor} />
          <Button onClick={handleCreate} disabled={createMutation.isPending || !newName.trim()}>
            {createMutation.isPending ? 'Adding...' : 'Add Label'}
          </Button>
        </div>
      </div>
    </div>
  );
}
