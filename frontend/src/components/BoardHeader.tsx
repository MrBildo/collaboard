import { BoardSwitcher } from '@/components/BoardSwitcher';
import { GearMenu } from '@/components/GearMenu';
import { SearchCommand } from '@/components/SearchCommand';
import { Button } from '@/components/ui/button';
import type { Board, VersionStatus } from '@/types';
import type { Role } from '@/lib/roles';

type BoardHeaderProps = {
  boards: Board[];
  currentSlug?: string;
  boardName?: string;
  isAdmin: boolean;
  version?: string;
  versionStatus?: VersionStatus;
  currentUserName?: string;
  currentUserRole?: Role;
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
  versionStatus,
  currentUserName,
  currentUserRole,
  onNewCard,
  onBoardSettings,
  onGlobalAdmin,
  onLogout,
}: BoardHeaderProps) {
  return (
    <header className="flex h-14 shrink-0 items-center gap-x-3 border-b border-border px-4">
      {/* Left region — logo + board switcher. Equal-basis third so the center
          region stays page-symmetric regardless of side-content width (#176). */}
      <div className="flex min-w-0 flex-1 basis-0 items-center gap-x-3">
        {/* Logo — shrink-0 so it never clips */}
        <img src="/collaboard-logo.svg" alt="Collaboard" className="w-32 shrink-0 xs:w-48" />
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
      </div>
      {/* Center region — search, hidden on mobile, visible at xs+. Equal-basis
          third with justify-center makes the search genuinely page-centered;
          SearchCommand keeps its own w-full max-w-md cap (#176). */}
      <div className="flex min-w-0 flex-1 basis-0 justify-center xs:px-4">
        <div className="hidden xs:block xs:w-full">
          <SearchCommand />
        </div>
      </div>
      {/* Right region — actions. Equal-basis third, justified to the end. */}
      <div className="flex min-w-0 flex-1 basis-0 items-center justify-end gap-2">
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
          versionStatus={versionStatus}
          currentUserName={currentUserName}
          currentUserRole={currentUserRole}
          onNewCard={onNewCard}
          onBoardSettings={onBoardSettings}
          onGlobalAdmin={onGlobalAdmin}
          onLogout={onLogout}
        />
      </div>
    </header>
  );
}
