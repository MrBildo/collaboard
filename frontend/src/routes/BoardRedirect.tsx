import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Navigate } from 'react-router-dom';
import { LayoutDashboard } from 'lucide-react';
import { EmptyState } from '@/components/ui/empty-state';
import { GlobalAdminPanel } from '@/components/GlobalAdminPanel';
import { LoginScreen } from '@/components/LoginScreen';
import { fetchBoards } from '@/lib/api';
import { findLastBoardSlug } from '@/lib/auth';
import { queryKeys } from '@/lib/query-keys';
import { QUERY_DEFAULTS } from '@/lib/query-config';
import { useAuth } from '@/hooks/use-auth';
import { useCurrentUser } from '@/hooks/use-current-user';

export function BoardRedirect() {
  const { loggedIn, handleLogin } = useAuth();
  const { isAdmin } = useCurrentUser(loggedIn);
  const [adminOpen, setAdminOpen] = useState(false);

  const boardsQuery = useQuery({
    queryKey: queryKeys.boards.all(),
    queryFn: fetchBoards,
    enabled: loggedIn,
    ...QUERY_DEFAULTS.boards,
  });

  if (!loggedIn) {
    return <LoginScreen onLogin={handleLogin} />;
  }

  if (boardsQuery.isLoading) {
    return (
      <div className="flex h-screen items-center justify-center bg-background text-muted-foreground">
        Loading boards...
      </div>
    );
  }

  const boards = boardsQuery.data ?? [];
  if (boards.length === 0) {
    // Zero boards (card #292, spec §3.1): upgrade the dead-end line into a
    // teaching empty-state. An admin gets a real action into the Admin Panel
    // (Boards tab is its default) instead of being told an admin must act; a
    // non-admin keeps warmed explanatory text and no dead button.
    return (
      <div className="flex h-screen items-center justify-center bg-background">
        <EmptyState
          icon={LayoutDashboard}
          title="No boards yet"
          description={
            isAdmin
              ? 'Boards are where your cards live. Create your first one to get started.'
              : "Your admin hasn't created a board yet. Check back once they do."
          }
          action={
            isAdmin
              ? { label: 'Create your first board', onClick: () => setAdminOpen(true) }
              : undefined
          }
        />
        <GlobalAdminPanel open={adminOpen} onOpenChange={setAdminOpen} />
      </div>
    );
  }

  const lastSlug = findLastBoardSlug();
  const lastBoard = lastSlug ? boards.find((b) => b.slug === lastSlug) : null;
  const targetSlug = lastBoard ? lastBoard.slug : boards[0].slug;

  return <Navigate to={`/boards/${targetSlug}`} replace />;
}
