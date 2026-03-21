import { useQuery } from '@tanstack/react-query';
import { fetchMe, fetchUsers } from '@/lib/api';
import { queryKeys } from '@/lib/query-keys';

export function useCurrentUser(loggedIn: boolean) {
  const meQuery = useQuery({
    queryKey: queryKeys.users.me(),
    queryFn: fetchMe,
    enabled: loggedIn,
    staleTime: Infinity,
  });
  const currentUserId = meQuery.data?.id;
  const currentUserRole = meQuery.data?.role;

  const adminCheck = useQuery({
    queryKey: queryKeys.users.adminCheck(),
    queryFn: async () => {
      await fetchUsers();
      return true;
    },
    retry: false,
    enabled: loggedIn,
  });
  const isAdmin = adminCheck.data === true;

  return { currentUserId, currentUserRole, isAdmin };
}
