import { useState } from 'react';
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
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  createLabel,
  createLane,
  createUser,
  deactivateUser,
  deleteLabel,
  deleteLane,
  fetchLabels,
  fetchLanes,
  fetchUsers,
  updateLabel,
  updateLane,
} from '@/lib/api';

type AdminPanelProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

const ROLE_MAP: Record<number, string> = {
  0: 'Administrator',
  1: 'Human',
  2: 'Agent',
};

export function AdminPanel({ open, onOpenChange }: AdminPanelProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-3xl max-h-[85vh] overflow-y-auto p-6">
        <DialogHeader>
          <DialogTitle>Admin Panel</DialogTitle>
          <DialogDescription>Manage lanes, users, and labels.</DialogDescription>
        </DialogHeader>

        <Tabs defaultValue="lanes" className="mt-2 flex flex-col gap-4">
          <TabsList variant="line" className="w-full justify-start gap-2 border-b pb-2">
            <TabsTrigger value="lanes">Lanes</TabsTrigger>
            <TabsTrigger value="users">Users</TabsTrigger>
            <TabsTrigger value="labels">Labels</TabsTrigger>
          </TabsList>

          <TabsContent value="lanes">
            <LanesTab />
          </TabsContent>
          <TabsContent value="users">
            <UsersTab />
          </TabsContent>
          <TabsContent value="labels">
            <LabelsTab />
          </TabsContent>
        </Tabs>
      </DialogContent>
    </Dialog>
  );
}

function LanesTab() {
  const queryClient = useQueryClient();
  const [newName, setNewName] = useState('');
  const [newPosition, setNewPosition] = useState('');
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editName, setEditName] = useState('');
  const [editPosition, setEditPosition] = useState('');
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  const lanesQuery = useQuery({
    queryKey: ['lanes'],
    queryFn: fetchLanes,
  });

  const createMutation = useMutation({
    mutationFn: () => createLane(newName.trim(), parseInt(newPosition, 10)),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['lanes'] });
      queryClient.invalidateQueries({ queryKey: ['board'] });
      setNewName('');
      setNewPosition('');
    },
    onError: (err) => {
      setDeleteError(err instanceof Error ? err.message : 'Failed to create lane.');
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, patch }: { id: string; patch: Record<string, unknown> }) =>
      updateLane(id, patch),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['lanes'] });
      queryClient.invalidateQueries({ queryKey: ['board'] });
      setEditingId(null);
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteLane(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['lanes'] });
      queryClient.invalidateQueries({ queryKey: ['board'] });
      setConfirmDeleteId(null);
      setDeleteError(null);
    },
    onError: () => {
      setDeleteError('Cannot delete lane — it may still contain cards.');
    },
  });

  const handleCreate = () => {
    if (!newName.trim() || !newPosition) return;
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
              id="lane-name"
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
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
              placeholder="0"
              className="w-20"
            />
          </div>
          <Button
            type="button"
            onClick={handleCreate}
            disabled={createMutation.isPending || !newName.trim() || !newPosition}
          >
            {createMutation.isPending ? 'Adding...' : 'Add Lane'}
          </Button>
        </div>
      </div>
    </div>
  );
}

function UsersTab() {
  const queryClient = useQueryClient();
  const [newName, setNewName] = useState('');
  const [newRole, setNewRole] = useState('1');
  const [createdKey, setCreatedKey] = useState<string | null>(null);
  const [confirmDeactivateId, setConfirmDeactivateId] = useState<string | null>(null);

  const usersQuery = useQuery({
    queryKey: ['users'],
    queryFn: fetchUsers,
  });

  const createMutation = useMutation({
    mutationFn: () => createUser(newName.trim(), parseInt(newRole, 10)),
    onSuccess: (user) => {
      queryClient.invalidateQueries({ queryKey: ['users'] });
      setNewName('');
      setNewRole('1');
      setCreatedKey(user.authKey);
    },
  });

  const deactivateMutation = useMutation({
    mutationFn: (id: string) => deactivateUser(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['users'] });
      setConfirmDeactivateId(null);
    },
  });

  const handleCreate = () => {
    if (!newName.trim()) return;
    createMutation.mutate();
  };

  const handleDeactivate = (id: string) => {
    if (confirmDeactivateId === id) {
      deactivateMutation.mutate(id);
    } else {
      setConfirmDeactivateId(id);
    }
  };

  const copyKey = async (key: string) => {
    await navigator.clipboard.writeText(key);
  };

  const users = usersQuery.data ?? [];

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col divide-y divide-border rounded-lg border">
        {users.map((user) => (
          <div key={user.id} className="flex items-center justify-between px-4 py-3 transition-colors hover:bg-muted/50">
            <div className="flex items-center gap-3">
              <span className="font-medium">{user.name}</span>
              <Badge variant="secondary" className={user.role === 0 ? 'bg-primary/15 text-primary' : user.role === 2 ? 'bg-accent/15 text-accent' : ''}>{ROLE_MAP[user.role] ?? `Role ${user.role}`}</Badge>
              <Badge variant={user.isActive ? 'outline' : 'destructive'}>
                {user.isActive ? 'Active' : 'Inactive'}
              </Badge>
            </div>
            <div className="flex gap-1">
              {user.isActive && (
                <Button
                  size="xs"
                  variant="ghost"
                  className="text-destructive hover:text-destructive"
                  onClick={() => handleDeactivate(user.id)}
                  disabled={deactivateMutation.isPending}
                >
                  {confirmDeactivateId === user.id ? 'Confirm' : 'Deactivate'}
                </Button>
              )}
            </div>
          </div>
        ))}
      </div>

      {createdKey && (
        <div className="rounded-lg border bg-muted/30 p-3">
          <p className="mb-1 text-sm font-medium">New user auth key:</p>
          <div className="flex items-center gap-2">
            <code className="flex-1 rounded bg-muted px-2 py-1 text-xs break-all">
              {createdKey}
            </code>
            <Button size="xs" variant="outline" onClick={() => copyKey(createdKey)}>
              Copy
            </Button>
            <Button size="xs" variant="ghost" onClick={() => setCreatedKey(null)}>
              Dismiss
            </Button>
          </div>
        </div>
      )}

      <Separator />

      <div>
        <h3 className="mb-3 text-sm font-medium">Add User</h3>
        <div className="flex items-end gap-2">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="user-name">Name</Label>
            <Input
              id="user-name"
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
              placeholder="e.g. Jane Doe"
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label>Role</Label>
            <Select value={newRole} onValueChange={(v) => v && setNewRole(v)}>
              <SelectTrigger className="w-40">
                <SelectValue>{ROLE_MAP[parseInt(newRole, 10)] ?? newRole}</SelectValue>
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="0">Administrator</SelectItem>
                <SelectItem value="1">Human</SelectItem>
                <SelectItem value="2">Agent</SelectItem>
              </SelectContent>
            </Select>
          </div>
          <Button onClick={handleCreate} disabled={createMutation.isPending || !newName.trim()}>
            {createMutation.isPending ? 'Adding...' : 'Add User'}
          </Button>
        </div>
      </div>
    </div>
  );
}

function LabelsTab() {
  const queryClient = useQueryClient();
  const [newName, setNewName] = useState('');
  const [newColor, setNewColor] = useState('#3b82f6');
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editName, setEditName] = useState('');
  const [editColor, setEditColor] = useState('');
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);

  const labelsQuery = useQuery({
    queryKey: ['labels'],
    queryFn: fetchLabels,
  });

  const createMutation = useMutation({
    mutationFn: () => createLabel(newName.trim(), newColor || undefined),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['labels'] });
      setNewName('');
      setNewColor('#3b82f6');
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, patch }: { id: string; patch: Record<string, unknown> }) =>
      updateLabel(id, patch),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['labels'] });
      setEditingId(null);
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteLabel(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['labels'] });
      setConfirmDeleteId(null);
    },
  });

  const handleCreate = () => {
    if (!newName.trim()) return;
    createMutation.mutate();
  };

  const startEdit = (id: string, name: string, color: string | null | undefined) => {
    setEditingId(id);
    setEditName(name);
    setEditColor(color ?? '#3b82f6');
  };

  const saveEdit = () => {
    if (!editingId) return;
    const patch: Record<string, unknown> = {};
    const label = labelsQuery.data?.find((l) => l.id === editingId);
    if (!label) return;
    if (editName.trim() !== label.name) patch.name = editName.trim();
    if (editColor !== (label.color ?? '')) patch.color = editColor;
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
    }
  };

  const labels = labelsQuery.data ?? [];

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col divide-y divide-border rounded-lg border">
        {labels.map((label) => (
          <div key={label.id} className="flex items-center justify-between px-4 py-3 transition-colors hover:bg-muted/50">
            {editingId === label.id ? (
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
                  <span
                    className="inline-block h-4 w-4 rounded-full"
                    style={{ backgroundColor: label.color ?? '#6b7280' }}
                  />
                  <span className="font-medium">{label.name}</span>
                </div>
                <div className="flex gap-1">
                  <Button
                    size="xs"
                    variant="ghost"
                    onClick={() => startEdit(label.id, label.name, label.color)}
                    title="Edit"
                  >
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M17 3a2.85 2.83 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5Z" /><path d="m15 5 4 4" /></svg>
                  </Button>
                  <Button
                    size="xs"
                    variant="ghost"
                    className="text-destructive hover:text-destructive"
                    onClick={() => handleDelete(label.id)}
                    disabled={deleteMutation.isPending}
                    title={confirmDeleteId === label.id ? 'Confirm delete' : 'Delete'}
                  >
                    {confirmDeleteId === label.id ? 'Confirm' : (
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M3 6h18" /><path d="M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6" /><path d="M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2" /></svg>
                    )}
                  </Button>
                </div>
              </>
            )}
          </div>
        ))}
      </div>

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
