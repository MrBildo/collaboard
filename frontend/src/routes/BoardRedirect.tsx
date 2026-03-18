import { useQuery } from '@tanstack/react-query';
import { Navigate } from 'react-router-dom';
import { LoginScreen } from '@/components/LoginScreen';
import { fetchBoards } from '@/lib/api';
import { findLastBoardSlug } from '@/lib/auth';
import { queryKeys } from '@/lib/query-keys';
import { QUERY_DEFAULTS } from '@/lib/query-config';
import { useAuth } from '@/hooks/use-auth';

export function BoardRedirect() {
  const { loggedIn, handleLogin } = useAuth();

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
    return (
      <div className="flex h-screen items-center justify-center bg-background text-muted-foreground">
        No boards found. An admin needs to create one.
      </div>
    );
  }

  const lastSlug = findLastBoardSlug();
  const lastBoard = lastSlug ? boards.find((b) => b.slug === lastSlug) : null;
  const targetSlug = lastBoard ? lastBoard.slug : boards[0].slug;

  return <Navigate to={`/boards/${targetSlug}`} replace />;
}
