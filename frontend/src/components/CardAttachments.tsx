import { useMemo, useRef, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button } from '@/components/ui/button';
import { AttachmentRow } from '@/components/AttachmentRow';
import { AttachmentDropZone } from '@/components/AttachmentDropZone';
import { api, deleteAttachment, fetchCardAttachments, uploadAttachment } from '@/lib/api';
import { MAX_FILE_SIZE_BYTES } from '@/lib/attachments';
import { queryKeys } from '@/lib/query-keys';
import { QUERY_DEFAULTS } from '@/lib/query-config';
import { useUserDirectory } from '@/hooks/use-user-directory';
import { ROLES } from '@/lib/roles';
import { formatDateTime, formatFileSize } from '@/lib/utils';
import { AlertCircle, Check, Loader2, Paperclip, Upload, X } from 'lucide-react';
import type { PendingFile } from '@/lib/attachments';
import type { AttachmentMeta } from '@/types';

type LiveModeProps = {
  mode: 'live';
  cardId: string;
  currentUserId?: string;
  currentUserRole?: number;
  readOnly?: boolean;
};

type PendingModeProps = {
  mode: 'pending';
  pendingFiles: PendingFile[];
  onAddFiles: (files: File[]) => void;
  onRemoveFile: (fileId: string) => void;
  disabled?: boolean;
};

type CardAttachmentsProps = LiveModeProps | PendingModeProps;

function StatusIcon({ status }: { status: PendingFile['status'] }) {
  switch (status) {
    case 'uploading':
      return <Loader2 className="h-4 w-4 shrink-0 animate-spin text-primary" />;
    case 'done':
      return <Check className="h-4 w-4 shrink-0 text-primary" />;
    case 'error':
      return <AlertCircle className="h-4 w-4 shrink-0 text-destructive" />;
    default:
      return <Paperclip className="h-4 w-4 shrink-0 text-muted-foreground" />;
  }
}

export function CardAttachments(props: CardAttachmentsProps) {
  if (props.mode === 'pending') {
    return <PendingAttachments {...props} />;
  }
  return <LiveAttachments {...props} />;
}

function LiveAttachments({ cardId, currentUserId, currentUserRole, readOnly }: LiveModeProps) {
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
      if (file.size > MAX_FILE_SIZE_BYTES) {
        console.error(`File "${file.name}" exceeds 5MB limit (${formatFileSize(file.size)})`);
        continue;
      }
      uploadMutation.mutate(file);
    }
  };

  const handleUpload = () => {
    fileInputRef.current?.click();
  };

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = e.target.files;
    if (files && files.length > 0) {
      handleFilesDropped(Array.from(files));
    }
    if (fileInputRef.current) {
      fileInputRef.current.value = '';
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
                {formatFileSize(attachment.fileSize)} &middot;{' '}
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
          <div>
            <input
              ref={fileInputRef}
              type="file"
              multiple
              className="hidden"
              onChange={handleFileChange}
            />
            <Button
              variant="outline"
              size="sm"
              onClick={handleUpload}
              disabled={uploadMutation.isPending}
            >
              <Upload className="mr-1.5 h-4 w-4" />
              {uploadMutation.isPending ? 'Uploading...' : 'Add Files'}
            </Button>
            <p className="mt-1 text-xs text-muted-foreground">
              Drop files, paste images, or click to attach. Max 5MB per file.
            </p>
          </div>
        )}
      </div>
    </AttachmentDropZone>
  );
}

function PendingAttachments({
  pendingFiles,
  onAddFiles,
  onRemoveFile,
  disabled,
}: PendingModeProps) {
  const fileInputRef = useRef<HTMLInputElement>(null);

  const handleFilesDropped = (files: File[]) => {
    onAddFiles(files);
  };

  const handleFileInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = e.target.files;
    if (files && files.length > 0) {
      onAddFiles(Array.from(files));
    }
    if (fileInputRef.current) {
      fileInputRef.current.value = '';
    }
  };

  return (
    <AttachmentDropZone onFiles={handleFilesDropped} disabled={disabled}>
      <div className="flex flex-col gap-4">
        {pendingFiles.length > 0 && (
          <div className="flex flex-col gap-2">
            {pendingFiles.map((pf) => (
              <AttachmentRow
                key={pf.id}
                fileName={pf.file.name}
                variant={pf.status === 'error' ? 'error' : 'default'}
                icon={<StatusIcon status={pf.status} />}
                metadata={
                  <>
                    {formatFileSize(pf.file.size)}
                    {pf.error && <span className="ml-1 text-destructive"> &mdash; {pf.error}</span>}
                  </>
                }
                actions={
                  pf.status !== 'uploading' && pf.status !== 'done' ? (
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon-sm"
                      className="ml-2 shrink-0"
                      onClick={() => onRemoveFile(pf.id)}
                      disabled={disabled && pf.status !== 'error'}
                    >
                      <X className="h-4 w-4" />
                    </Button>
                  ) : undefined
                }
              />
            ))}
          </div>
        )}

        <div>
          <input
            ref={fileInputRef}
            type="file"
            multiple
            className="hidden"
            onChange={handleFileInputChange}
          />
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => fileInputRef.current?.click()}
            disabled={disabled}
          >
            <Upload className="mr-1.5 h-4 w-4" />
            Add Files
          </Button>
          <p className="mt-1 text-xs text-muted-foreground">
            Drop files, paste images, or click to attach. Max 5MB per file.
          </p>
        </div>
      </div>
    </AttachmentDropZone>
  );
}
