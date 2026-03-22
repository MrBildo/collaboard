import { useCallback } from 'react';
import { useQuery } from '@tanstack/react-query';
import { fetchUserDirectory } from '@/lib/api';
import { queryKeys } from '@/lib/query-keys';
import { QUERY_DEFAULTS } from '@/lib/query-config';

export function useUserDirectory() {
  const query = useQuery({
    queryKey: queryKeys.users.directory(),
    queryFn: fetchUserDirectory,
    ...QUERY_DEFAULTS.userDirectory,
  });

  const getUserName = useCallback(
    (id: string): string => query.data?.find((u) => u.id === id)?.name ?? 'Unknown',
    [query.data],
  );

  return { ...query, getUserName };
}
