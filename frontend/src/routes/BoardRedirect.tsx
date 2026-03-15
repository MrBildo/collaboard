import { useQuery } from '@tanstack/react-query';
import { Navigate } from 'react-router-dom';
import { LoginScreen } from '@/components/LoginScreen';
import { fetchBoards } from '@/lib/api';
import { getLastBoardSlug, isLoggedIn, setUserKey } from '@/lib/auth';
import { useCallback, useState } from 'react';

export function BoardRedirect() {
  const [loggedIn, setLoggedIn] = useState(isLoggedIn());

  const handleLogin = useCallback((key: string) => {
    setUserKey(key);
    setLoggedIn(true);
  }, []);

  const boardsQuery = useQuery({
    queryKey: ['boards'],
    queryFn: fetchBoards,
    enabled: loggedIn,
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

  const lastSlug = getLastBoardSlug();
  const lastBoard = lastSlug ? boards.find((b) => b.slug === lastSlug) : null;
  const targetSlug = lastBoard ? lastBoard.slug : boards[0].slug;

  return <Navigate to={`/boards/${targetSlug}`} replace />;
}
