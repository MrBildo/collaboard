import { useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import { Separator } from '@/components/ui/separator';
import {
  createLane,
  deleteLane,
  fetchLanes,
  updateLane,
} from '@/lib/api';

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
          <DialogTitle>Board Lanes</DialogTitle>
          <DialogDescription>Manage lanes for this board.</DialogDescription>
        </DialogHeader>

        <div className="mt-2">
          <LanesTab boardId={boardId} />
        </div>
      </DialogContent>
    </Dialog>
  );
}

function LanesTab({ boardId }: { boardId: string }) {
  const queryClient = useQueryClient();
  const nameInputRef = useRef<HTMLInputElement>(null);
  const [newName, setNewName] = useState('');
  const [newPosition, setNewPosition] = useState('');
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editName, setEditName] = useState('');
  const [editPosition, setEditPosition] = useState('');
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  const lanesQuery = useQuery({
    queryKey: ['lanes', boardId],
    queryFn: () => fetchLanes(boardId),
  });

  const createMutation = useMutation({
    mutationFn: () => {
      const lanes = lanesQuery.data ?? [];
      const pos = newPosition
        ? parseInt(newPosition, 10)
        : (lanes.length > 0 ? Math.max(...lanes.map((l) => l.position)) + 1 : 0);
      return createLane(boardId, newName.trim(), pos);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['lanes', boardId] });
      queryClient.invalidateQueries({ queryKey: ['boardData', boardId] });
      setNewName('');
      setNewPosition('');
      setTimeout(() => nameInputRef.current?.focus(), 0);
    },
    onError: (err) => {
      setDeleteError(err instanceof Error ? err.message : 'Failed to create lane.');
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, patch }: { id: string; patch: Record<string, unknown> }) =>
      updateLane(id, patch),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['lanes', boardId] });
      queryClient.invalidateQueries({ queryKey: ['boardData', boardId] });
      setEditingId(null);
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteLane(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['lanes', boardId] });
      queryClient.invalidateQueries({ queryKey: ['boardData', boardId] });
      setConfirmDeleteId(null);
      setDeleteError(null);
    },
    onError: () => {
      setDeleteError('Cannot delete lane — it may still contain cards.');
    },
  });

  const handleCreate = () => {
    if (!newName.trim()) return;
    createMutation.mutate();
  };

  const startEdit = (id: string, name: string, position: number) => {
    setEditingId(id);
    setEditName(name);
    setEditPosition(String(position));
  };

  const saveEdit = () => {
    if (!editingId) return;
    const patch: Record<string, unknown> = {};
    const lane = lanesQuery.data?.find((l) => l.id === editingId);
    if (!lane) return;
    if (editName.trim() !== lane.name) patch.name = editName.trim();
    const pos = parseInt(editPosition, 10);
    if (!isNaN(pos) && pos !== lane.position) patch.position = pos;
    if (Object.keys(patch).length > 0) {
      updateMutation.mutate({ id: editingId, patch });
    } else {
      setEditingId(null);
    }
  };

  const handleDelete = (id: string) => {
    if (confirmDeleteId === id) {
      deleteMutation.mutate(id);
    } else {
      setConfirmDeleteId(id);
      setDeleteError(null);
    }
  };

  const lanes = lanesQuery.data ?? [];

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col divide-y divide-border rounded-lg border">
        {lanes.map((lane) => (
          <div key={lane.id} className="flex items-center justify-between px-4 py-3 transition-colors hover:bg-muted/50">
            {editingId === lane.id ? (
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
                <div className="ml-2 flex gap-1">
                  <Button size="xs" onClick={saveEdit} disabled={updateMutation.isPending}>
                    Save
                  </Button>
                  <Button size="xs" variant="outline" onClick={() => setEditingId(null)}>
                    Cancel
                  </Button>
                </div>
              </>
            ) : (
              <>
                <div className="flex items-center gap-3">
                  <span className="font-medium">{lane.name}</span>
                  <Badge variant="secondary">pos {lane.position}</Badge>
                </div>
                <div className="flex gap-1">
                  <Button
                    size="xs"
                    variant="ghost"
                    onClick={() => startEdit(lane.id, lane.name, lane.position)}
                    title="Edit"
                  >
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M17 3a2.85 2.83 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5Z" /><path d="m15 5 4 4" /></svg>
                  </Button>
                  <Button
                    size="xs"
                    variant="ghost"
                    className="text-destructive hover:text-destructive"
                    onClick={() => handleDelete(lane.id)}
                    disabled={deleteMutation.isPending}
                    title={confirmDeleteId === lane.id ? 'Confirm delete' : 'Delete'}
                  >
                    {confirmDeleteId === lane.id ? 'Confirm' : (
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M3 6h18" /><path d="M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6" /><path d="M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2" /></svg>
                    )}
                  </Button>
                </div>
              </>
            )}
          </div>
        ))}
      </div>

      {deleteError && <p className="text-sm text-destructive">{deleteError}</p>}

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
