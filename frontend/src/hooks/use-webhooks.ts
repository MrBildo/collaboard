import { useQuery } from '@tanstack/react-query';

import { fetchWebhookDeliveries, fetchWebhookStatus, fetchWebhookSubscriptions } from '@/lib/api';
import { queryKeys } from '@/lib/query-keys';
import { QUERY_DEFAULTS } from '@/lib/query-config';

// Webhook data hooks (#326). Thin query wrappers — mutations live at their call
// sites (matching the admin-tab pattern) so each owns its #203 error surface.
//
// The admin UI joins two reads client-side: the subscriptions list carries the
// authoritative on-read metrics (success/fail counts + last-delivery, computed
// server-side over the whole log), and a single recent deliveries window carries
// per-attempt error text — joined by subscriptionId, it refines a row's last
// delivery into "blocked vs HTTP-failed" and feeds the expanded recent-attempts
// log. One window fetch, no N+1; the recent slice is enough for "a handful of
// webhooks" (Variant B's target). The 200-row cap is the endpoint's max.
const DELIVERY_WINDOW = 200;

export function useWebhookSubscriptions(enabled = true) {
  return useQuery({
    queryKey: queryKeys.webhooks.subscriptions(),
    queryFn: fetchWebhookSubscriptions,
    enabled,
    ...QUERY_DEFAULTS.webhooks,
  });
}

export function useWebhookDeliveries(enabled = true) {
  return useQuery({
    queryKey: queryKeys.webhooks.deliveries(),
    queryFn: () => fetchWebhookDeliveries({ limit: DELIVERY_WINDOW }),
    enabled,
    ...QUERY_DEFAULTS.webhooks,
  });
}

export function useWebhookStatus(enabled = true) {
  return useQuery({
    queryKey: queryKeys.webhooks.status(),
    queryFn: fetchWebhookStatus,
    enabled,
    ...QUERY_DEFAULTS.webhooks,
  });
}
