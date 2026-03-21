// Vendored from Dice UI / Radix — do not modify directly
import * as React from 'react';

import { useIsomorphicLayoutEffect } from '@/hooks/use-isomorphic-layout-effect';

function useAsRef<T>(props: T) {
  const ref = React.useRef<T>(props);

  useIsomorphicLayoutEffect(() => {
    ref.current = props;
  });

  return ref;
}

export { useAsRef };
