import { formatFileSize } from '@/lib/utils';

export type PendingFile = {
  id: string;
  file: File;
  status: 'pending' | 'uploading' | 'done' | 'error';
  error?: string;
};

export const MAX_FILE_SIZE_BYTES = 5 * 1024 * 1024;

let pendingFileCounter = 0;

export function validateFiles(files: File[]): PendingFile[] {
  return files.map((file) => {
    pendingFileCounter++;
    const id = `pf-${Date.now()}-${pendingFileCounter}`;
    if (file.size > MAX_FILE_SIZE_BYTES) {
      return {
        id,
        file,
        status: 'error' as const,
        error: `File exceeds 5MB limit (${formatFileSize(file.size)})`,
      };
    }
    return {
      id,
      file,
      status: 'pending' as const,
    };
  });
}
