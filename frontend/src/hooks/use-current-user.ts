import { useQuery } from '@tanstack/react-query';
import { fetchMe, fetchUsers } from '@/lib/api';
import { queryKeys } from '@/lib/query-keys';
import type { Role } from '@/lib/roles';

export function useCurrentUser(loggedIn: boolean) {
  const meQuery = useQuery({
    queryKey: queryKeys.users.me(),
    queryFn: fetchMe,
    enabled: loggedIn,
    staleTime: Infinity,
  });
  const currentUserId = meQuery.data?.id;
  const currentUserName = meQuery.data?.name;
  // The schema validates the role is a number from the backend; Role is the
  // narrowed type for valid role values.
  const currentUserRole = meQuery.data?.role as Role | undefined;

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

  return { currentUserId, currentUserName, currentUserRole, isAdmin };
}
