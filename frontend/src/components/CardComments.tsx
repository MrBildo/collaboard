import { useCallback, useMemo, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { Button } from '@/components/ui/button';
import { Textarea } from '@/components/ui/textarea';
import { createComment, deleteComment, fetchComments, updateComment } from '@/lib/api';
import { queryKeys } from '@/lib/query-keys';
import { QUERY_DEFAULTS } from '@/lib/query-config';
import { useUserDirectory } from '@/hooks/use-user-directory';
import { ROLES } from '@/lib/roles';
import { formatDateTime } from '@/lib/utils';

type CardCommentsProps = {
  cardId: string;
  currentUserId?: string;
  currentUserRole?: number;
};

export function CardComments({ cardId, currentUserId, currentUserRole }: CardCommentsProps) {
  const queryClient = useQueryClient();

  const { getUserName } = useUserDirectory();
  const [newComment, setNewComment] = useState('');
  const [newCommentFocused, setNewCommentFocused] = useState(false);
  const [isPreviewingNew, setIsPreviewingNew] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editText, setEditText] = useState('');
  const [isPreviewingEdit, setIsPreviewingEdit] = useState(false);
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);
  const newCommentRef = useRef<HTMLTextAreaElement>(null);

  const commentsQuery = useQuery({
    queryKey: queryKeys.cards.comments(cardId),
    queryFn: () => fetchComments(cardId),
    ...QUERY_DEFAULTS.comments,
  });

  const createMutation = useMutation({
    mutationFn: (content: string) => createComment(cardId, content),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.cards.comments(cardId) });
      setNewComment('');
      setNewCommentFocused(false);
    },
    onError: (error: unknown) => {
      console.error('Failed to create comment:', error);
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, content }: { id: string; content: string }) => updateComment(id, { contentMarkdown: content }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.cards.comments(cardId) });
      setEditingId(null);
      setEditText('');
    },
    onError: (error: unknown) => {
      console.error('Failed to update comment:', error);
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteComment(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.cards.comments(cardId) });
      setConfirmDeleteId(null);
    },
    onError: (error: unknown) => {
      console.error('Failed to delete comment:', error);
    },
  });

  const handleAdd = () => {
    const trimmed = newComment.trim();
    if (!trimmed) return;
    createMutation.mutate(trimmed);
  };

  const handleCancelNew = useCallback(() => {
    setNewComment('');
    setNewCommentFocused(false);
    setIsPreviewingNew(false);
    newCommentRef.current?.blur();
  }, []);

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

  const handleCancelEdit = () => {
    setEditingId(null);
    setEditText('');
    setIsPreviewingEdit(false);
  };

  const handleDelete = (id: string) => {
    if (confirmDeleteId === id) {
      deleteMutation.mutate(id);
    } else {
      setConfirmDeleteId(id);
    }
  };

  const comments = useMemo(
    () => [...(commentsQuery.data ?? [])].sort(
      (a, b) => new Date(b.lastUpdatedAtUtc).getTime() - new Date(a.lastUpdatedAtUtc).getTime(),
    ),
    [commentsQuery.data],
  );

  const isExpanded = newCommentFocused || newComment.length > 0;

  return (
    <div className="flex flex-1 flex-col gap-3 overflow-hidden">
      {/* New comment input — sticky at top */}
      <div className="flex shrink-0 flex-col gap-2">
        {isExpanded && (
          <div className="flex items-center gap-1">
            <Button
              variant={!isPreviewingNew ? 'secondary' : 'ghost'}
              size="xs"
              onClick={() => setIsPreviewingNew(false)}
            >
              Edit
            </Button>
            <Button
              variant={isPreviewingNew ? 'secondary' : 'ghost'}
              size="xs"
              onClick={() => setIsPreviewingNew(true)}
            >
              Preview
            </Button>
          </div>
        )}
        {isPreviewingNew && isExpanded ? (
          <div className="prose prose-sm max-w-none overflow-x-auto rounded-md border bg-muted/30 p-4 text-sm text-foreground">
            {newComment.trim() ? (
              <ReactMarkdown remarkPlugins={[remarkGfm]}>{newComment}</ReactMarkdown>
            ) : (
              <p className="italic text-muted-foreground">Nothing to preview.</p>
            )}
          </div>
        ) : (
          <Textarea
            ref={newCommentRef}
            value={newComment}
            onChange={(e) => setNewComment(e.target.value)}
            onFocus={() => setNewCommentFocused(true)}
            placeholder="Add a comment..."
            rows={isExpanded ? 3 : 1}
            className="bg-muted transition-all"
          />
        )}
        {isExpanded && (
          <div className="flex justify-end gap-2">
            <Button size="sm" variant="outline" onClick={handleCancelNew}>
              Cancel
            </Button>
            <Button size="sm" onClick={handleAdd} disabled={createMutation.isPending || !newComment.trim()}>
              {createMutation.isPending ? 'Saving...' : 'Save'}
            </Button>
          </div>
        )}
      </div>

      {/* Comment list — scrollable */}
      <div className="flex flex-col gap-3 overflow-y-auto">
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
                <div className="flex items-center gap-1">
                  <Button
                    variant={!isPreviewingEdit ? 'secondary' : 'ghost'}
                    size="xs"
                    onClick={() => setIsPreviewingEdit(false)}
                  >
                    Edit
                  </Button>
                  <Button
                    variant={isPreviewingEdit ? 'secondary' : 'ghost'}
                    size="xs"
                    onClick={() => setIsPreviewingEdit(true)}
                  >
                    Preview
                  </Button>
                </div>
                {isPreviewingEdit ? (
                  <div className="prose prose-sm max-w-none overflow-x-auto rounded-md border bg-muted/30 p-4 text-sm text-foreground">
                    {editText.trim() ? (
                      <ReactMarkdown remarkPlugins={[remarkGfm]}>{editText}</ReactMarkdown>
                    ) : (
                      <p className="italic text-muted-foreground">Nothing to preview.</p>
                    )}
                  </div>
                ) : (
                  <Textarea value={editText} onChange={(e) => setEditText(e.target.value)} rows={3} className="bg-muted" />
                )}
                <div className="flex justify-end gap-2">
                  <Button size="sm" variant="outline" onClick={handleCancelEdit}>
                    Cancel
                  </Button>
                  <Button size="sm" onClick={handleSaveEdit} disabled={updateMutation.isPending}>
                    {updateMutation.isPending ? 'Saving...' : 'Save'}
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
                      {getUserName(comment.userId)}
                    </span>
                    {' · '}
                    {formatDateTime(comment.lastUpdatedAtUtc)}
                  </span>
                  <div className="flex gap-1">
                    {(currentUserRole === ROLES.Administrator || comment.userId === currentUserId) && (
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
      </div>
    </div>
  );
}
