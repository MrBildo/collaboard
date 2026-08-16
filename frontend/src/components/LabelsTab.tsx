import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Tags } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { EmptyState } from '@/components/ui/empty-state';
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

  // Admin-tab mutations are inline tier — failures surface
  // inline via `list.setDeleteError` → <EditableListContainer>. Previously all
  // three paths went silent (console.error only); this brings them in line with
  // the other admin tabs. skipToast keeps the floor quiet; the call site owns
  // the surface.
  const createMutation = useMutation({
    meta: { skipToast: true },
    mutationFn: () => createLabel(boardId, newName.trim(), newColor || undefined),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.labels.all(boardId) });
      setNewName('');
      setNewColor('#3b82f6');
    },
    onError: (err) => {
      list.setDeleteError(err instanceof Error ? err.message : 'Failed to create label.');
    },
  });

  const updateMutation = useMutation({
    meta: { skipToast: true },
    mutationFn: ({ id, patch }: { id: string; patch: UpdateLabelPatch }) =>
      updateLabel(boardId, id, patch),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.labels.all(boardId) });
      list.setEditingId(null);
    },
    onError: (err) => {
      list.setDeleteError(err instanceof Error ? err.message : 'Failed to update label.');
    },
  });

  const deleteMutation = useMutation({
    meta: { skipToast: true },
    mutationFn: (id: string) => deleteLabel(boardId, id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.labels.all(boardId) });
      list.clearDelete();
    },
    onError: (err) => {
      list.setDeleteError(err instanceof Error ? err.message : 'Failed to delete label.');
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
    <div className="flex h-full min-h-0 flex-col gap-4">
      {/* Scroll zone — fills remaining height, scrolls internally on long lists. */}
      <div className="min-h-0 flex-1 overflow-y-auto">
        {!labelsQuery.isLoading && labels.length === 0 && (
          // Empty labels: teach what labels are before the
          // Add form below, instead of rendering a blank list area. Self-disposing
          // — gone the moment the first label exists. Description-only (no action):
          // the Add form is right below, so the affordance is already in view.
          <EmptyState
            icon={Tags}
            title="No labels yet"
            description="Labels tag cards by type (Bug, Feature, Chore). Add your first below."
          />
        )}
        <EditableListContainer error={list.deleteError}>
          {labels.map((label) => (
            <EditableListRow key={label.id}>
              {list.editingId === label.id ? (
                <>
                  <div className="flex flex-1 items-center gap-2">
                    <LabelColorPicker
                      value={editColor}
                      onValueChange={setEditColor}
                      className="h-7 w-7"
                    />
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
      </div>

      {/* Pinned footer — never scrolls; the Add form stays reachable. */}
      <div className="shrink-0">
        <Separator className="mb-4" />
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
