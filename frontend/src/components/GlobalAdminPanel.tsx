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
  EditableListContainer,
  EditableListRow,
  EditFormActions,
  ItemActions,
} from '@/components/editable-list';
import { useEditableList } from '@/hooks/use-editable-list';
import {
  createBoard,
  createUser,
  deactivateUser,
  deleteBoard,
  fetchBoards,
  fetchUsers,
  updateBoard,
} from '@/lib/api';
import type { UpdateBoardPatch } from '@/types';
import { ROLES } from '@/lib/roles';
import { queryKeys } from '@/lib/query-keys';
import { QUERY_DEFAULTS } from '@/lib/query-config';

type GlobalAdminPanelProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

const ROLE_MAP: Record<number, string> = {
  [ROLES.Administrator]: 'Administrator',
  [ROLES.Human]: 'Human',
  [ROLES.Agent]: 'Agent',
};

export function GlobalAdminPanel({ open, onOpenChange }: GlobalAdminPanelProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-3xl max-h-[85vh] overflow-y-auto p-6">
        <DialogHeader>
          <DialogTitle>Admin Panel</DialogTitle>
          <DialogDescription>Manage boards and users.</DialogDescription>
        </DialogHeader>

        <Tabs defaultValue="boards" className="mt-2 flex flex-col gap-4">
          <TabsList variant="line" className="w-full justify-start gap-2 border-b pb-2">
            <TabsTrigger value="boards">Boards</TabsTrigger>
            <TabsTrigger value="users">Users</TabsTrigger>
          </TabsList>

          <TabsContent value="boards">
            <BoardsTab />
          </TabsContent>
          <TabsContent value="users">
            <UsersTab />
          </TabsContent>
        </Tabs>
      </DialogContent>
    </Dialog>
  );
}

function BoardsTab() {
  const queryClient = useQueryClient();
  const [newName, setNewName] = useState('');
  const [editName, setEditName] = useState('');
  const list = useEditableList();

  const boardsQuery = useQuery({
    queryKey: queryKeys.boards.all(),
    queryFn: fetchBoards,
    ...QUERY_DEFAULTS.boards,
  });

  const createMutation = useMutation({
    mutationFn: () => createBoard(newName.trim()),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.boards.all() });
      setNewName('');
    },
    onError: (err) => {
      list.setDeleteError(err instanceof Error ? err.message : 'Failed to create board.');
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, patch }: { id: string; patch: UpdateBoardPatch }) =>
      updateBoard(id, patch),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.boards.all() });
      list.setEditingId(null);
    },
    onError: (error: unknown) => {
      console.error('Failed to update board:', error);
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteBoard(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.boards.all() });
      list.clearDelete();
    },
    onError: () => {
      list.setDeleteError('Cannot delete board — it may still have lanes.');
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
    const board = boardsQuery.data?.find((b) => b.id === list.editingId);
    if (!board) return;
    if (editName.trim() !== board.name) {
      updateMutation.mutate({ id: list.editingId, patch: { name: editName.trim() } });
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

  const boards = boardsQuery.data ?? [];

  return (
    <div className="flex flex-col gap-4">
      <EditableListContainer error={list.deleteError}>
        {boards.map((board) => (
          <EditableListRow key={board.id}>
            {list.editingId === board.id ? (
              <>
                <div className="flex flex-1 items-center gap-2">
                  <Input
                    value={editName}
                    onChange={(e) => setEditName(e.target.value)}
                    className="h-7"
                    placeholder="Board name"
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
                  <span className="font-medium">{board.name}</span>
                  <Badge variant="secondary">/{board.slug}</Badge>
                </div>
                <ItemActions
                  isConfirmingDelete={list.confirmDeleteId === board.id}
                  isDeleting={deleteMutation.isPending}
                  onEdit={() => startEdit(board.id, board.name)}
                  onDelete={() => handleDelete(board.id)}
                />
              </>
            )}
          </EditableListRow>
        ))}
      </EditableListContainer>

      <Separator />

      <div>
        <h3 className="mb-3 text-sm font-medium">Add Board</h3>
        <div className="flex items-end gap-2">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="board-name">Name</Label>
            <Input
              id="board-name"
              value={newName}
              onChange={(e) => setNewName(e.target.value)}
              placeholder="e.g. Sprint Board"
            />
          </div>
          <Button
            type="button"
            onClick={handleCreate}
            disabled={createMutation.isPending || !newName.trim()}
          >
            {createMutation.isPending ? 'Adding...' : 'Add Board'}
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
    queryKey: queryKeys.users.all(),
    queryFn: fetchUsers,
    ...QUERY_DEFAULTS.userDirectory,
  });

  const createMutation = useMutation({
    mutationFn: () => createUser(newName.trim(), parseInt(newRole, 10)),
    onSuccess: (user) => {
      queryClient.invalidateQueries({ queryKey: queryKeys.users.all() });
      setNewName('');
      setNewRole('1');
      setCreatedKey(user.authKey);
    },
    onError: (error: unknown) => {
      console.error('Failed to create user:', error);
    },
  });

  const deactivateMutation = useMutation({
    mutationFn: (id: string) => deactivateUser(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.users.all() });
      setConfirmDeactivateId(null);
    },
    onError: (error: unknown) => {
      console.error('Failed to deactivate user:', error);
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
      <EditableListContainer>
        {users.map((user) => (
          <EditableListRow key={user.id}>
            <div className="flex items-center gap-3">
              <span className="font-medium">{user.name}</span>
              <Badge variant="secondary" className={user.role === ROLES.Administrator ? 'bg-primary/15 text-primary' : user.role === ROLES.Agent ? 'bg-accent/15 text-accent' : ''}>{ROLE_MAP[user.role] ?? `Role ${user.role}`}</Badge>
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
          </EditableListRow>
        ))}
      </EditableListContainer>

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
