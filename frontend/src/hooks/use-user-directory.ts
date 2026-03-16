import { useQuery } from '@tanstack/react-query';
import { fetchUserDirectory } from '@/lib/api';
import { queryKeys } from '@/lib/query-keys';
import { QUERY_DEFAULTS } from '@/lib/query-config';

export function useUserDirectory() {
  return useQuery({
    queryKey: queryKeys.users.directory(),
    queryFn: fetchUserDirectory,
    ...QUERY_DEFAULTS.userDirectory,
  });
}
