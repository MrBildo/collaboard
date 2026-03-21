import { useEffect, useState } from 'react';
import { Moon, Settings, Sun } from 'lucide-react';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';

function getStoredTheme(): 'light' | 'dark' {
  if (typeof window === 'undefined') return 'light';
  return (localStorage.getItem('collaboard-theme') as 'light' | 'dark') ?? 'light';
}

function applyTheme(theme: 'light' | 'dark') {
  document.documentElement.setAttribute('data-theme', theme);
  localStorage.setItem('collaboard-theme', theme);
}

type GearMenuProps = {
  isAdmin: boolean;
  version?: string;
  onNewCard: () => void;
  onBoardSettings: () => void;
  onGlobalAdmin: () => void;
  onLogout: () => void;
};

export function GearMenu({
  isAdmin,
  version,
  onNewCard,
  onBoardSettings,
  onGlobalAdmin,
  onLogout,
}: GearMenuProps) {
  const [theme, setTheme] = useState<'light' | 'dark'>(getStoredTheme);

  useEffect(() => {
    applyTheme(theme);
  }, [theme]);

  const toggleTheme = () => {
    setTheme((prev) => (prev === 'light' ? 'dark' : 'light'));
  };

  return (
    <DropdownMenu>
      <DropdownMenuTrigger className="inline-flex h-9 w-9 items-center justify-center rounded-md text-muted-foreground hover:bg-secondary hover:text-foreground">
        <Settings className="h-4 w-4" />
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-48">
        {/* + New Card: mobile only (hidden at md+) */}
        <DropdownMenuItem onClick={onNewCard} className="xs:hidden">
          + New Card
        </DropdownMenuItem>
        {/* Board Settings: mobile + md, hidden at lg (admin only) */}
        {isAdmin && (
          <DropdownMenuItem onClick={onBoardSettings} className="lg:hidden">
            Board Settings
          </DropdownMenuItem>
        )}
        {/* Admin: all tiers (admin only) */}
        {isAdmin && <DropdownMenuItem onClick={onGlobalAdmin}>Admin</DropdownMenuItem>}
        {/* Separator — visible when any action item above is shown */}
        <DropdownMenuSeparator className={isAdmin ? '' : 'xs:hidden'} />
        {/* Theme toggle: all tiers */}
        <DropdownMenuItem onClick={toggleTheme}>
          <span className="flex items-center gap-2">
            {theme === 'light' ? (
              <>
                <Moon className="h-3.5 w-3.5" /> Dark mode
              </>
            ) : (
              <>
                <Sun className="h-3.5 w-3.5" /> Light mode
              </>
            )}
          </span>
        </DropdownMenuItem>
        <DropdownMenuSeparator />
        <DropdownMenuItem onClick={onLogout}>Logout</DropdownMenuItem>
        {version && (
          <>
            <DropdownMenuSeparator />
            <div className="px-1.5 py-1 text-xs text-muted-foreground">v{version}</div>
          </>
        )}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
