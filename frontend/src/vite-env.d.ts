/// <reference types="vite/client" />

// Injected at build time by the `define` in vite.config.ts. Consumed as the
// persisted-query-cache buster in main.tsx so a new build discards the prior
// build's persisted cache instead of rehydrating stale data across an upgrade.
declare const __BUILD_VERSION__: string;
