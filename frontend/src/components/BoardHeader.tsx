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
    <header className="flex flex-wrap items-center gap-x-3 gap-y-1 border-b border-border px-4 py-2 md:h-14 md:flex-nowrap md:py-0">
      {/* Logo — shrink-0 so it never clips */}
      <img
        src="/collaboard-logo.png"
        alt="Collaboard"
        className="w-32 shrink-0 md:w-48"
        style={{ imageRendering: 'pixelated' }}
      />
      {/* Board switcher — wraps below logo on mobile, inline on md+ */}
      {boards.length > 1 && (
        <div className="order-last w-full md:order-none md:w-auto">
          <BoardSwitcher boards={boards} currentSlug={currentSlug} />
        </div>
      )}
      {boards.length === 1 && boardName && (
        <span className="hidden max-w-[10rem] truncate text-sm font-medium text-muted-foreground md:inline">
          {boardName}
        </span>
      )}
      {/* Search — visible all tiers, fills center */}
      <div className="flex flex-1 justify-center md:px-4">
        <SearchCommand />
      </div>
      {/* Right actions */}
      <div className="flex shrink-0 items-center gap-2">
        {/* + New Card: md+ only */}
        <Button onClick={onNewCard} className="hidden md:inline-flex">
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
