import { BoardSwitcher } from '@/components/BoardSwitcher';
import { SearchCommand } from '@/components/SearchCommand';
import { ThemeToggle } from '@/components/ThemeToggle';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { Menu } from 'lucide-react';
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
    <header className="flex h-14 shrink-0 items-center justify-between border-b border-border px-4">
      <div className="flex items-center gap-3">
        <img
          src="/collaboard-logo.png"
          alt="Collaboard"
          className="w-32 md:w-48"
          style={{ imageRendering: 'pixelated' }}
        />
        {boards.length > 1 && (
          <BoardSwitcher boards={boards} currentSlug={currentSlug} />
        )}
        {boards.length === 1 && boardName && (
          <span className="hidden max-w-[16rem] truncate text-sm font-medium text-muted-foreground md:inline">{boardName}</span>
        )}
      </div>
      <div className="hidden flex-1 justify-center px-4 md:flex">
        <SearchCommand />
      </div>
      {/* Desktop actions */}
      <div className="hidden items-center gap-2 md:flex">
        <Button onClick={onNewCard}>+ New Card</Button>
        {isAdmin && (
          <>
            <Button variant="outline" onClick={onBoardSettings}>
              Board Settings
            </Button>
            <Button variant="outline" onClick={onGlobalAdmin}>
              Admin
            </Button>
          </>
        )}
        <ThemeToggle />
        <Button variant="ghost" onClick={onLogout} className="text-muted-foreground">
          Logout
        </Button>
        {version && (
          <span className="text-xs text-muted-foreground/50">v{version}</span>
        )}
      </div>
      {/* Mobile menu */}
      <div className="flex items-center gap-1 md:hidden">
        <ThemeToggle />
        <DropdownMenu>
          <DropdownMenuTrigger className="inline-flex h-9 w-9 items-center justify-center rounded-md text-muted-foreground hover:bg-secondary hover:text-foreground">
            <Menu className="h-5 w-5" />
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="w-48">
            <DropdownMenuItem onClick={onNewCard}>
              + New Card
            </DropdownMenuItem>
            {isAdmin && (
              <>
                <DropdownMenuItem onClick={onBoardSettings}>
                  Board Settings
                </DropdownMenuItem>
                <DropdownMenuItem onClick={onGlobalAdmin}>
                  Admin
                </DropdownMenuItem>
              </>
            )}
            <DropdownMenuSeparator />
            <DropdownMenuItem onClick={onLogout}>
              Logout
            </DropdownMenuItem>
            {version && (
              <>
                <DropdownMenuSeparator />
                <div className="px-1.5 py-1 text-xs text-muted-foreground">v{version}</div>
              </>
            )}
          </DropdownMenuContent>
        </DropdownMenu>
      </div>
    </header>
  );
}
