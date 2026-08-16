import { describe, test, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Inbox } from 'lucide-react';
import { EmptyState } from './empty-state';

// <EmptyState> is the onboarding teaching surface. The contract that
// is silent-and-expensive to regress is the role-aware action gate: a non-admin
// surface passes no `action` and must NOT render a button (a dead button telling
// a non-admin to "create a board" they can't create is the failure this guards).

describe('EmptyState', () => {
  test('renders the title and description', () => {
    render(<EmptyState icon={Inbox} title="No cards yet" description="Add your first below." />);

    expect(screen.getByText('No cards yet')).toBeInTheDocument();
    expect(screen.getByText('Add your first below.')).toBeInTheDocument();
  });

  test('renders no action button when no action is provided', () => {
    render(<EmptyState icon={Inbox} title="No boards yet" description="Ask your admin." />);

    expect(screen.queryByRole('button')).toBeNull();
  });

  test('renders the action button and fires onClick when an action is provided', async () => {
    const onClick = vi.fn();
    render(
      <EmptyState icon={Inbox} title="No lanes yet" action={{ label: 'Add a lane', onClick }} />,
    );

    const button = screen.getByRole('button', { name: 'Add a lane' });
    await userEvent.click(button);

    expect(onClick).toHaveBeenCalledTimes(1);
  });

  test('marks the icon aria-hidden so the title is the announced text', () => {
    const { container } = render(<EmptyState icon={Inbox} title="No cards yet" />);

    const icon = container.querySelector('svg[aria-hidden="true"]');
    expect(icon).not.toBeNull();
  });
});
