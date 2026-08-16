import { describe, test, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { InlineError } from './inline-error';

// <InlineError> is the inline tier's surface — the regression that is silent and
// expensive here is the accessibility contract: if the alert role or
// the icon (color-not-alone) regresses, a screen-reader user or a colour-blind
// user loses the error with no visible test failure elsewhere.

describe('InlineError', () => {
  test('renders the message inside an assertive alert region', () => {
    render(<InlineError message="Couldn't save changes" />);

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent("Couldn't save changes");
    expect(alert).toHaveAttribute('aria-live', 'assertive');
  });

  test('pairs an icon with the text so the error is not conveyed by colour alone', () => {
    const { container } = render(<InlineError message="failed" />);

    // The lucide icon renders an <svg> marked aria-hidden — present for sighted
    // users as the non-colour cue, hidden from the accessibility tree so the
    // message text is the single announced string.
    const icon = container.querySelector('svg[aria-hidden="true"]');
    expect(icon).not.toBeNull();
  });
});
