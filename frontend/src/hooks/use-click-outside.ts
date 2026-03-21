import { useEffect } from 'react';
import { useAsRef } from '@/hooks/use-as-ref';

export function useClickOutside(ref: React.RefObject<HTMLElement | null>, callback: () => void) {
  const callbackRef = useAsRef(callback);

  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (ref.current && !ref.current.contains(event.target as Node)) {
        callbackRef.current();
      }
    }
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, [ref, callbackRef]);
}
