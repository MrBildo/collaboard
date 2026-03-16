import { useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { Button } from '@/components/ui/button';
import { Textarea } from '@/components/ui/textarea';
import { createComment, deleteComment, fetchComments, fetchUserDirectory, updateComment } from '@/lib/api';
import { queryKeys } from '@/lib/query-keys';

type CardCommentsProps = {
  cardId: string;
  currentUserId?: string;
  currentUserRole?: number;
};

export function CardComments({ cardId, currentUserId, currentUserRole }: CardCommentsProps) {
  const queryClient = useQueryClient();

  const directoryQuery = useQuery({
    queryKey: queryKeys.users.directory(),
    queryFn: fetchUserDirectory,
    staleTime: 60_000,
  });
  const userMap = new Map<string, string>(
    (directoryQuery.data ?? []).map((u) => [u.id, u.name]),
  );
  const [newComment, setNewComment] = useState('');
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editText, setEditText] = useState('');
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  const commentsQuery = useQuery({
    queryKey: queryKeys.cards.comments(cardId),
    queryFn: () => fetchComments(cardId),
  });

  const createMutation = useMutation({
    mutationFn: (content: string) => createComment(cardId, content),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.cards.comments(cardId) });
      setNewComment('');
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, content }: { id: string; content: string }) => updateComment(id, content),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.cards.comments(cardId) });
      setEditingId(null);
      setEditText('');
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteComment(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.cards.comments(cardId) });
      setConfirmDeleteId(null);
    },
  });

  const handleAdd = () => {
    const trimmed = newComment.trim();
    if (!trimmed) return;
    createMutation.mutate(trimmed);
  };

  const handleEdit = (id: string, currentContent: string) => {
    setEditingId(id);
    setEditText(currentContent);
  };

  const handleSaveEdit = () => {
    if (!editingId) return;
    const trimmed = editText.trim();
    if (!trimmed) return;
    updateMutation.mutate({ id: editingId, content: trimmed });
  };

  const handleDelete = (id: string) => {
    if (confirmDeleteId === id) {
      deleteMutation.mutate(id);
    } else {
      setConfirmDeleteId(id);
    }
  };

  const comments = [...(commentsQuery.data ?? [])].sort(
    (a, b) => new Date(b.lastUpdatedAtUtc).getTime() - new Date(a.lastUpdatedAtUtc).getTime(),
  );

  return (
    <div className="flex flex-col gap-4">
      {commentsQuery.isLoading && (
        <p className="text-sm text-muted-foreground">Loading comments...</p>
      )}

      {comments.length === 0 && !commentsQuery.isLoading && (
        <p className="text-sm text-muted-foreground">No comments yet.</p>
      )}

      {comments.map((comment) => (
        <div key={comment.id} className="rounded-lg border bg-muted/30 p-3">
          {editingId === comment.id ? (
            <div className="flex flex-col gap-2">
              <Textarea value={editText} onChange={(e) => setEditText(e.target.value)} rows={3} />
              <div className="flex gap-2">
                <Button size="sm" onClick={handleSaveEdit} disabled={updateMutation.isPending}>
                  {updateMutation.isPending ? 'Saving...' : 'Save'}
                </Button>
                <Button
                  size="sm"
                  variant="outline"
                  onClick={() => {
                    setEditingId(null);
                    setEditText('');
                  }}
                >
                  Cancel
                </Button>
              </div>
            </div>
          ) : (
            <>
              <div className="prose prose-sm max-w-none break-words">
                <ReactMarkdown remarkPlugins={[remarkGfm]}>{comment.contentMarkdown}</ReactMarkdown>
              </div>
              <div className="mt-2 flex items-center justify-between">
                <span className="text-xs text-muted-foreground">
                  <span className="font-medium text-foreground">
                    {userMap.get(comment.userId) ?? 'Unknown'}
                  </span>
                  {' · '}
                  {new Date(comment.lastUpdatedAtUtc).toLocaleString()}
                </span>
                <div className="flex gap-1">
                  {(currentUserRole === 0 || comment.userId === currentUserId) && (
                    <>
                      <Button
                        size="xs"
                        variant="ghost"
                        onClick={() => handleEdit(comment.id, comment.contentMarkdown)}
                      >
                        Edit
                      </Button>
                      <Button
                        size="xs"
                        variant="destructive"
                        onClick={() => handleDelete(comment.id)}
                        disabled={deleteMutation.isPending}
                      >
                        {confirmDeleteId === comment.id ? 'Confirm' : 'Delete'}
                      </Button>
                    </>
                  )}
                </div>
              </div>
            </>
          )}
        </div>
      ))}

      <div className="flex flex-col gap-2 border-t pt-4">
        <Textarea
          ref={textareaRef}
          value={newComment}
          onChange={(e) => setNewComment(e.target.value)}
          placeholder="Add a comment (Markdown supported)"
          rows={3}
          className="bg-muted"
        />
        <div className="flex justify-end">
          <Button onClick={handleAdd} disabled={createMutation.isPending || !newComment.trim()}>
            {createMutation.isPending ? 'Adding...' : 'Add Comment'}
          </Button>
        </div>
      </div>
    </div>
  );
}
