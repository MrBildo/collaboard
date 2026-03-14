import { useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button } from '@/components/ui/button';
import { api, deleteAttachment, fetchCardAttachments, fetchUserDirectory, uploadAttachment } from '@/lib/api';
import type { AttachmentMeta } from '@/types';

type CardAttachmentsProps = {
  cardId: string;
  currentUserId?: string;
  currentUserRole?: number;
};

export function CardAttachments({ cardId, currentUserId, currentUserRole }: CardAttachmentsProps) {
  const queryClient = useQueryClient();
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);

  const directoryQuery = useQuery({
    queryKey: ['userDirectory'],
    queryFn: fetchUserDirectory,
    staleTime: 60_000,
  });
  const userName = (id: string) =>
    directoryQuery.data?.find((u) => u.id === id)?.name ?? 'Unknown';

  const attachmentsQuery = useQuery({
    queryKey: ['attachments', cardId],
    queryFn: () => fetchCardAttachments(cardId),
  });

  const uploadMutation = useMutation({
    mutationFn: (file: File) => uploadAttachment(cardId, file),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['attachments', cardId] });
      if (fileInputRef.current) {
        fileInputRef.current.value = '';
      }
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteAttachment(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['attachments', cardId] });
      setConfirmDeleteId(null);
    },
  });

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

  const attachments = attachmentsQuery.data ?? [];

  return (
    <div className="flex flex-col gap-4">
      {attachmentsQuery.isLoading && (
        <p className="text-sm text-muted-foreground">Loading attachments...</p>
      )}

      {attachments.length === 0 && !attachmentsQuery.isLoading && (
        <p className="text-sm text-muted-foreground">No attachments yet.</p>
      )}

      {attachments.map((attachment) => (
        <div
          key={attachment.id}
          className="flex items-center justify-between rounded-lg border bg-muted/30 p-3"
        >
          <div className="min-w-0 flex-1">
            <p className="truncate text-sm font-medium">{attachment.fileName}</p>
            <p className="text-xs text-muted-foreground">
              {userName(attachment.addedByUserId)} &middot; {new Date(attachment.addedAtUtc).toLocaleString()}
            </p>
          </div>
          <div className="ml-2 flex shrink-0 gap-1">
            <Button size="xs" variant="outline" onClick={() => handleDownload(attachment)}>
              Download
            </Button>
            {(currentUserRole === 0 || attachment.addedByUserId === currentUserId) && (
              <Button
                size="xs"
                variant="destructive"
                onClick={() => handleDelete(attachment.id)}
                disabled={deleteMutation.isPending}
              >
                {confirmDeleteId === attachment.id ? 'Confirm' : 'Delete'}
              </Button>
            )}
          </div>
        </div>
      ))}

      <div className="border-t pt-4">
        <input ref={fileInputRef} type="file" className="hidden" onChange={handleFileChange} />
        <Button variant="outline" onClick={handleUpload} disabled={uploadMutation.isPending}>
          {uploadMutation.isPending ? 'Uploading...' : 'Upload Attachment'}
        </Button>
      </div>
    </div>
  );
}
