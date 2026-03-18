import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useState } from 'react';
import { clearUserKey, isLoggedIn, setUserKey } from '@/lib/auth';

export function useAuth() {
  const queryClient = useQueryClient();
  const [loggedIn, setLoggedIn] = useState(isLoggedIn());

  const handleLogin = useCallback((key: string) => {
    setUserKey(key);
    setLoggedIn(true);
  }, []);

  const handleLogout = useCallback(() => {
    clearUserKey();
    queryClient.clear();
    setLoggedIn(false);
  }, [queryClient]);

  return { loggedIn, handleLogin, handleLogout };
}
