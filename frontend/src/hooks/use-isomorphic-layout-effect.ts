// Vendored from Dice UI / Radix — do not modify directly
import * as React from "react";

const useIsomorphicLayoutEffect =
  typeof window !== "undefined" ? React.useLayoutEffect : React.useEffect;

export { useIsomorphicLayoutEffect };
