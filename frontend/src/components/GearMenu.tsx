import { useEffect, useState } from 'react';
import { HelpCircle, Moon, Settings, Sun } from 'lucide-react';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { Badge } from '@/components/ui/badge';
import { ROLES, type Role } from '@/lib/roles';
import { cn } from '@/lib/utils';

const ROLE_LABELS: Record<Role, string> = {
  [ROLES.Administrator]: 'Administrator',
  [ROLES.Human]: 'Human',
  [ROLES.Agent]: 'Agent',
  [ROLES['Agent Admin']]: 'Agent Admin',
};

function roleBadgeClassName(role: Role): string {
  return role === ROLES.Administrator || role === ROLES['Agent Admin']
    ? 'bg-primary/15 text-primary'
    : role === ROLES.Agent
      ? 'bg-accent/15 text-accent'
      : '';
}

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
  currentUserName?: string;
  currentUserRole?: Role;
  onNewCard: () => void;
  onBoardSettings: () => void;
  onGlobalAdmin: () => void;
  onLogout: () => void;
};

export function GearMenu({
  isAdmin,
  version,
  currentUserName,
  currentUserRole,
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
        {/* Current user identity — shown while resolved; hidden during pending */}
        {currentUserName !== undefined && currentUserRole !== undefined && (
          <>
            <div className="flex items-center gap-2 px-2 py-1.5">
              <span className="min-w-0 flex-1 truncate text-sm font-medium text-foreground">
                {currentUserName}
              </span>
              <Badge
                variant="secondary"
                className={cn('shrink-0 text-xs', roleBadgeClassName(currentUserRole))}
              >
                {ROLE_LABELS[currentUserRole] ?? `Role ${currentUserRole}`}
              </Badge>
            </div>
            <DropdownMenuSeparator />
          </>
        )}
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
        {/* Help / User Guide: all tiers — links to published docs in a new tab */}
        <DropdownMenuItem
          onClick={() =>
            window.open(
              'https://github.com/MrBildo/collaboard/blob/main/docs/user-guide.md',
              '_blank',
              'noopener,noreferrer',
            )
          }
        >
          <span className="flex items-center gap-2">
            <HelpCircle className="h-3.5 w-3.5" /> Help / User Guide
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
