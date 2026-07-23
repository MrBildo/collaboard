import { describe, test, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { VersionStatus } from '@/types';
import { GearMenu } from './GearMenu';

const baseProps = {
  isAdmin: false,
  onNewCard: vi.fn(),
  onBoardSettings: vi.fn(),
  onGlobalAdmin: vi.fn(),
  onLogout: vi.fn(),
};

function statusWithUpdate(latest = '1.17.0'): VersionStatus {
  return {
    current: '1.16.0',
    latest,
    updateAvailable: true,
    releaseUrl: 'https://example.test/release',
    lastChecked: new Date().toISOString(),
  };
}

function statusUpToDate(): VersionStatus {
  return {
    current: '1.16.0',
    latest: '1.16.0',
    updateAvailable: false,
    releaseUrl: 'https://example.test/release',
    lastChecked: new Date().toISOString(),
  };
}

describe('GearMenu update indicator', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  test('shows the dot on the gear trigger when an update is available', () => {
    render(<GearMenu {...baseProps} versionStatus={statusWithUpdate()} />);

    expect(screen.getByLabelText('Update available')).toBeInTheDocument();
  });

  test('hides the dot when up to date', () => {
    render(<GearMenu {...baseProps} versionStatus={statusUpToDate()} />);

    expect(screen.queryByLabelText('Update available')).not.toBeInTheDocument();
  });

  test('shows the update link row with the release URL inside the menu', async () => {
    const user = userEvent.setup();
    render(<GearMenu {...baseProps} versionStatus={statusWithUpdate()} />);

    // The trigger is the only button before the menu opens.
    await user.click(screen.getByRole('button'));
    // The dropdown content is rendered once the trigger is opened.
    const link = await screen.findByRole('link', { name: /v1.16.0.*v1.17.0 available/i });

    expect(link).toHaveAttribute('href', 'https://example.test/release');
    expect(link).toHaveAttribute('target', '_blank');
  });

  test('dismissing the update hides the dot and persists per-version', async () => {
    const user = userEvent.setup();
    const { rerender } = render(<GearMenu {...baseProps} versionStatus={statusWithUpdate()} />);

    await user.click(screen.getByRole('button'));
    await user.click(await screen.findByLabelText('Dismiss update reminder'));

    expect(localStorage.getItem('collaboard-dismissed-update')).toBe('1.17.0');
    expect(screen.queryByLabelText('Update available')).not.toBeInTheDocument();

    // A newer version than the dismissed one re-shows the dot.
    rerender(<GearMenu {...baseProps} versionStatus={statusWithUpdate('1.18.0')} />);
    expect(screen.getByLabelText('Update available')).toBeInTheDocument();
  });

  test('a previously-dismissed version stays hidden across mounts', () => {
    localStorage.setItem('collaboard-dismissed-update', '1.17.0');

    render(<GearMenu {...baseProps} versionStatus={statusWithUpdate('1.17.0')} />);

    expect(screen.queryByLabelText('Update available')).not.toBeInTheDocument();
  });

  test('falls back to the plain version footer when no status is provided', async () => {
    const user = userEvent.setup();
    render(<GearMenu {...baseProps} version="1.16.0" />);

    await user.click(screen.getByRole('button'));

    expect(await screen.findByText('v1.16.0')).toBeInTheDocument();
    expect(screen.queryByLabelText('Update available')).not.toBeInTheDocument();
  });

  test('prefers the fresher /version value over a stale status payload for the footer', async () => {
    const user = userEvent.setup();
    // version (5-minute staleTime) has already caught up to a new deploy; versionStatus
    // (30-minute staleTime) still reflects the pre-deploy build. The footer must show the
    // fresher value, not the stale one.
    render(<GearMenu {...baseProps} version="1.16.1" versionStatus={statusUpToDate()} />);

    await user.click(screen.getByRole('button'));

    expect(await screen.findByText('v1.16.1')).toBeInTheDocument();
    expect(screen.queryByText('v1.16.0')).not.toBeInTheDocument();
  });

  test('prefers the fresher /version value in the update-available link text too', async () => {
    const user = userEvent.setup();
    render(<GearMenu {...baseProps} version="1.16.1" versionStatus={statusWithUpdate()} />);

    await user.click(screen.getByRole('button'));

    expect(
      await screen.findByRole('link', { name: /v1.16.1.*v1.17.0 available/i }),
    ).toBeInTheDocument();
  });

  test('falls back to the status payload current version when /version has not resolved yet', async () => {
    const user = userEvent.setup();
    render(<GearMenu {...baseProps} versionStatus={statusUpToDate()} />);

    await user.click(screen.getByRole('button'));

    expect(await screen.findByText('v1.16.0')).toBeInTheDocument();
  });
});
