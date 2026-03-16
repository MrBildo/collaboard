import { useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
} from '@/components/ui/dialog';
import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/tabs';
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
import {
  createLabel,
  createLane,
  createSize,
  deleteLabel,
  deleteLane,
  deleteSize,
  fetchLabels,
  fetchLanes,
  fetchSizes,
  updateLabel,
  updateLane,
  updateSize,
} from '@/lib/api';
import type { UpdateLabelPatch, UpdateLanePatch, UpdateSizePatch } from '@/types';
import { queryKeys } from '@/lib/query-keys';
import { QUERY_DEFAULTS } from '@/lib/query-config';

type AdminPanelProps = {
  boardId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

export function AdminPanel({ boardId, open, onOpenChange }: AdminPanelProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-3xl max-h-[85vh] overflow-y-auto p-6">
        <DialogHeader>
          <DialogTitle>Board Configuration</DialogTitle>
          <DialogDescription>Manage lanes, sizes, and labels for this board.</DialogDescription>
        </DialogHeader>

        <Tabs defaultValue="lanes" className="mt-2 flex flex-col gap-4">
          <TabsList variant="line" className="w-full justify-start gap-2 border-b pb-2">
            <TabsTrigger value="lanes">Lanes</TabsTrigger>
            <TabsTrigger value="sizes">Sizes</TabsTrigger>
            <TabsTrigger value="labels">Labels</TabsTrigger>
          </TabsList>

          <TabsContent value="lanes">
            <LanesTab boardId={boardId} />
          </TabsContent>
          <TabsContent value="sizes">
            <SizesTab boardId={boardId} />
          </TabsContent>
          <TabsContent value="labels">
            <LabelsTab boardId={boardId} />
          </TabsContent>
        </Tabs>
      </DialogContent>
    </Dialog>
  );
}

function LanesTab({ boardId }: { boardId: string }) {
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

function SizesTab({ boardId }: { boardId: string }) {
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

function LabelsTab({ boardId }: { boardId: string }) {
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
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, patch }: { id: string; patch: UpdateLabelPatch }) =>
      updateLabel(boardId, id, patch),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.labels.all(boardId) });
      list.setEditingId(null);
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteLabel(boardId, id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.labels.all(boardId) });
      list.clearDelete();
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
                  <input
                    type="color"
                    value={editColor}
                    onChange={(e) => setEditColor(e.target.value)}
                    className="h-7 w-10 cursor-pointer rounded border-0"
                  />
                  <Input
                    value={editName}
                    onChange={(e) => setEditName(e.target.value)}
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
                    className="inline-block h-4 w-4 rounded-full"
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
              placeholder="e.g. Bug"
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="label-color">Color</Label>
            <input
              id="label-color"
              type="color"
              value={newColor}
              onChange={(e) => setNewColor(e.target.value)}
              className="h-8 w-12 cursor-pointer rounded border-0"
            />
          </div>
          <Button onClick={handleCreate} disabled={createMutation.isPending || !newName.trim()}>
            {createMutation.isPending ? 'Adding...' : 'Add Label'}
          </Button>
        </div>
      </div>
    </div>
  );
}
