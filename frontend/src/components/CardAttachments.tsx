import { useMemo, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button } from '@/components/ui/button';
import { AttachmentRow } from '@/components/AttachmentRow';
import { AttachmentDropZone } from '@/components/AttachmentDropZone';
import { api, deleteAttachment, fetchCardAttachments, uploadAttachment } from '@/lib/api';
import { queryKeys } from '@/lib/query-keys';
import { QUERY_DEFAULTS } from '@/lib/query-config';
import { useUserDirectory } from '@/hooks/use-user-directory';
import { ROLES } from '@/lib/roles';
import { formatDateTime } from '@/lib/utils';
import type { AttachmentMeta } from '@/types';

type CardAttachmentsProps = {
  cardId: string;
  currentUserId?: string;
  currentUserRole?: number;
  readOnly?: boolean;
};

export function CardAttachments({
  cardId,
  currentUserId,
  currentUserRole,
  readOnly,
}: CardAttachmentsProps) {
  const queryClient = useQueryClient();
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);

  const { getUserName } = useUserDirectory();

  const attachmentsQuery = useQuery({
    queryKey: queryKeys.cards.attachments(cardId),
    queryFn: () => fetchCardAttachments(cardId),
    ...QUERY_DEFAULTS.attachments,
  });

  const uploadMutation = useMutation({
    mutationFn: (file: File) => uploadAttachment(cardId, file),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.cards.attachments(cardId) });
      if (fileInputRef.current) {
        fileInputRef.current.value = '';
      }
    },
    onError: (error: unknown) => {
      console.error('Failed to upload attachment:', error);
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteAttachment(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.cards.attachments(cardId) });
      setConfirmDeleteId(null);
    },
    onError: (error: unknown) => {
      console.error('Failed to delete attachment:', error);
    },
  });

  const handleFilesDropped = (files: File[]) => {
    for (const file of files) {
      uploadMutation.mutate(file);
    }
  };

  const handleUpload = () => {
    fileInputRef.current?.click();
  };

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) {
      uploadMutation.mutate(file);
    }
  };

  const handleDownload = async (attachment: AttachmentMeta) => {
    const response = await api.get(`/attachments/${attachment.id}`, {
      responseType: 'blob',
    });
    const url = URL.createObjectURL(response.data);
    const a = document.createElement('a');
    a.href = url;
    a.download = attachment.fileName;
    a.click();
    URL.revokeObjectURL(url);
  };

  const handleDelete = (id: string) => {
    if (confirmDeleteId === id) {
      deleteMutation.mutate(id);
    } else {
      setConfirmDeleteId(id);
    }
  };

  const attachments = useMemo(
    () =>
      [...(attachmentsQuery.data ?? [])].sort(
        (a, b) => new Date(b.addedAtUtc).getTime() - new Date(a.addedAtUtc).getTime(),
      ),
    [attachmentsQuery.data],
  );

  return (
    <AttachmentDropZone onFiles={handleFilesDropped} disabled={readOnly}>
      <div className="flex flex-col gap-4">
        {attachmentsQuery.isLoading && (
          <p className="text-sm text-muted-foreground">Loading attachments...</p>
        )}

        {attachments.length === 0 && !attachmentsQuery.isLoading && (
          <p className="text-sm text-muted-foreground">No attachments yet.</p>
        )}

        {attachments.map((attachment) => (
          <AttachmentRow
            key={attachment.id}
            fileName={attachment.fileName}
            metadata={
              <>
                {getUserName(attachment.addedByUserId)} &middot;{' '}
                {formatDateTime(attachment.addedAtUtc)}
              </>
            }
            actions={
              <>
                <Button size="xs" variant="outline" onClick={() => handleDownload(attachment)}>
                  Download
                </Button>
                {!readOnly &&
                  (currentUserRole === ROLES.Administrator ||
                    attachment.addedByUserId === currentUserId) && (
                    <Button
                      size="xs"
                      variant="destructive"
                      onClick={() => handleDelete(attachment.id)}
                      disabled={deleteMutation.isPending}
                    >
                      {confirmDeleteId === attachment.id ? 'Confirm' : 'Delete'}
                    </Button>
                  )}
              </>
            }
          />
        ))}

        {!readOnly && (
          <div className="border-t pt-4">
            <input ref={fileInputRef} type="file" className="hidden" onChange={handleFileChange} />
            <Button variant="outline" onClick={handleUpload} disabled={uploadMutation.isPending}>
              {uploadMutation.isPending ? 'Uploading...' : 'Upload Attachment'}
            </Button>
          </div>
        )}
      </div>
    </AttachmentDropZone>
  );
}
