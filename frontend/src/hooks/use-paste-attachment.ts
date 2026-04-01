import { useCallback, useEffect } from 'react';
import { buildPasteFileName } from '@/lib/utils';

type UsePasteAttachmentOptions = {
  onFile: (file: File) => void;
  enabled: boolean;
  containerRef?: React.RefObject<HTMLElement | null>;
};

export function usePasteAttachment({ onFile, enabled, containerRef }: UsePasteAttachmentOptions) {
  const handlePaste = useCallback(
    (e: ClipboardEvent) => {
      if (!enabled) return;

      const items = e.clipboardData?.items;
      if (!items) return;

      for (const item of Array.from(items)) {
        if (item.kind === 'file' && item.type.startsWith('image/')) {
          const blob = item.getAsFile();
          if (!blob) continue;

          e.preventDefault();
          const file = new File([blob], buildPasteFileName(blob.type), { type: blob.type });
          onFile(file);
          return;
        }
      }
    },
    [enabled, onFile],
  );

  useEffect(() => {
    const el = containerRef?.current ?? document;
    if (!el) return;
    el.addEventListener('paste', handlePaste as EventListener);
    return () => el.removeEventListener('paste', handlePaste as EventListener);
  }, [handlePaste, containerRef]);
}
