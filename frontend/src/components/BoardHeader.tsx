import { BoardSwitcher } from '@/components/BoardSwitcher';
import { GearMenu } from '@/components/GearMenu';
import { SearchCommand } from '@/components/SearchCommand';
import { Button } from '@/components/ui/button';
import type { Board } from '@/types';

type BoardHeaderProps = {
  boards: Board[];
  currentSlug?: string;
  boardName?: string;
  isAdmin: boolean;
  version?: string;
  onNewCard: () => void;
  onBoardSettings: () => void;
  onGlobalAdmin: () => void;
  onLogout: () => void;
};

export function BoardHeader({
  boards,
  currentSlug,
  boardName,
  isAdmin,
  version,
  onNewCard,
  onBoardSettings,
  onGlobalAdmin,
  onLogout,
}: BoardHeaderProps) {
  return (
    <header className="relative flex h-14 shrink-0 items-center gap-x-3 border-b border-border px-4">
      {/* Logo — shrink-0 so it never clips */}
      <img
        src="/collaboard-logo.png"
        alt="Collaboard"
        className="w-32 shrink-0 xs:w-48"
        style={{ imageRendering: 'pixelated' }}
      />
      {/* Board switcher — always inline */}
      {boards.length > 1 && (
        <div className="shrink min-w-0">
          <BoardSwitcher boards={boards} currentSlug={currentSlug} />
        </div>
      )}
      {boards.length === 1 && boardName && (
        <span className="hidden max-w-[10rem] truncate text-sm font-medium text-muted-foreground xs:inline">
          {boardName}
        </span>
      )}
      {/* Flex spacer — pushes right actions to the end */}
      <div className="flex-1" />
      {/* Search — absolutely centered on the page, hidden on mobile */}
      <div className="pointer-events-none absolute inset-x-0 hidden items-center justify-center px-4 xs:flex">
        <div className="pointer-events-auto w-full max-w-md">
          <SearchCommand />
        </div>
      </div>
      {/* Right actions */}
      <div className="flex shrink-0 items-center gap-2">
        {/* + New Card: xs+ only */}
        <Button onClick={onNewCard} className="hidden xs:inline-flex">
          + New Card
        </Button>
        {/* Board Settings: lg+ only (admin) */}
        {isAdmin && (
          <Button variant="outline" onClick={onBoardSettings} className="hidden lg:inline-flex">
            Board Settings
          </Button>
        )}
        {/* Gear menu — always visible, main menu across all tiers */}
        <GearMenu
          isAdmin={isAdmin}
          version={version}
          onNewCard={onNewCard}
          onBoardSettings={onBoardSettings}
          onGlobalAdmin={onGlobalAdmin}
          onLogout={onLogout}
        />
      </div>
    </header>
  );
}
