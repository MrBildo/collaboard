// Vendored from Dice UI / Radix — do not modify directly
import * as React from 'react';

function useLazyRef<T>(fn: () => T) {
  const ref = React.useRef<T | null>(null);

  if (ref.current === null) {
    ref.current = fn();
  }

  return ref as React.MutableRefObject<T>;
}

export { useLazyRef };
