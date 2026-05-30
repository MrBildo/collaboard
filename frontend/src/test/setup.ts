import '@testing-library/jest-dom';

// jsdom does not implement ResizeObserver. Shim it so any component that uses
// ResizeObserver (SearchCommand, use-lane-resize, use-label-layout, etc.) can
// render in tests without crashing. The no-op implementation is sufficient for
// unit tests that assert on rendered output rather than on resize behaviour.
class ResizeObserverStub {
  observe() {}
  unobserve() {}
  disconnect() {}
}
// eslint-disable-next-line @typescript-eslint/no-explicit-any
(globalThis as any).ResizeObserver = ResizeObserverStub;
