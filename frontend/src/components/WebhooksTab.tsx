import { useMemo, useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import {
  AlertTriangle,
  CheckCircle2,
  ChevronRight,
  Lock,
  Pencil,
  Plus,
  Send,
  Shield,
  ShieldAlert,
  Trash2,
  Unlock,
  Webhook,
  XCircle,
} from 'lucide-react';

import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Switch } from '@/components/ui/switch';
import { EmptyState } from '@/components/ui/empty-state';
import { InlineError } from '@/components/ui/inline-error';
import { WebhookFormDialog } from '@/components/WebhookFormDialog';
import {
  useWebhookDeliveries,
  useWebhookStatus,
  useWebhookSubscriptions,
} from '@/hooks/use-webhooks';
import {
  deleteWebhookSubscription,
  testWebhookSubscription,
  updateWebhookSubscription,
} from '@/lib/api';
import {
  classifyWebhookHealth,
  formatSuccessRate,
  isBlockedDelivery,
  isWildcard,
  successRate,
  type WebhookHealth,
} from '@/lib/webhooks';
import { queryKeys } from '@/lib/query-keys';
import { cn, formatRelativeTime } from '@/lib/utils';
import type { WebhookDelivery, WebhookSubscription, WebhookTestResult } from '@/types';

// The newest-first recent attempts shown in an expanded row.
const RECENT_LIMIT = 8;

type WebhookRowModel = {
  subscription: WebhookSubscription;
  recent: WebhookDelivery[];
  lastAttempt: WebhookDelivery | undefined;
  health: WebhookHealth;
};

export function WebhooksTab() {
  const subscriptionsQuery = useWebhookSubscriptions();
  const deliveriesQuery = useWebhookDeliveries();
  const statusQuery = useWebhookStatus();

  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const [createOpen, setCreateOpen] = useState(false);
  const [editing, setEditing] = useState<WebhookSubscription | undefined>(undefined);

  const subscriptions = subscriptionsQuery.data;
  // For rendering (length, empty check). The useMemo below depends on the raw
  // query data so its identity is stable across renders (a fresh `?? []` would
  // not be) — react-hooks/exhaustive-deps.
  const subList = subscriptions ?? [];

  const rows = useMemo<WebhookRowModel[]>(() => {
    const deliveries = deliveriesQuery.data?.items ?? [];
    return (subscriptions ?? []).map((subscription) => {
      // Deliveries arrive newest-first; the first match is the latest attempt.
      const recent = deliveries.filter((d) => d.subscriptionId === subscription.id);
      const lastAttempt = recent[0];
      return {
        subscription,
        recent: recent.slice(0, RECENT_LIMIT),
        lastAttempt,
        health: classifyWebhookHealth(subscription, lastAttempt),
      };
    });
  }, [subscriptions, deliveriesQuery.data]);

  const toggleExpanded = (id: string) => {
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  const status = statusQuery.data;
  const isLoading = subscriptionsQuery.isLoading;

  return (
    <div className="flex h-full min-h-0 flex-col gap-4">
      {/* Toolbar — count + global delivery posture + add. */}
      <div className="flex shrink-0 flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-3">
          <span className="text-sm font-medium">
            {subList.length} {subList.length === 1 ? 'subscription' : 'subscriptions'}
          </span>
          {status && (
            <GlobalPosture
              enabled={status.enabled}
              allowPrivate={status.allowPrivateNetworkTargets}
            />
          )}
        </div>
        <Button size="sm" onClick={() => setCreateOpen(true)}>
          <Plus aria-hidden="true" />
          Add webhook
        </Button>
      </div>

      {subscriptionsQuery.isError && (
        <InlineError message="Couldn't load webhook subscriptions. Try reopening the panel." />
      )}

      {/* Scroll zone — fills remaining height. */}
      <div className="min-h-0 flex-1 overflow-y-auto">
        {isLoading ? (
          <p className="px-1 py-6 text-center text-sm text-muted-foreground">Loading webhooks…</p>
        ) : subList.length === 0 ? (
          <EmptyState
            icon={Webhook}
            title="No webhooks yet"
            description="Send board events — card created, card moved — to an external URL. Each webhook subscribes to its own set of events."
            action={{ label: 'Add your first webhook', onClick: () => setCreateOpen(true) }}
          />
        ) : (
          <div className="overflow-hidden rounded-lg border border-border">
            <div className="grid grid-cols-[1.6rem_minmax(0,2fr)_5rem_8rem_minmax(0,8rem)_2.5rem] items-center gap-3 border-b border-border bg-muted px-3 py-2 text-[0.7rem] font-semibold tracking-wide text-muted-foreground uppercase">
              <span />
              <span>Webhook</span>
              <span>Events</span>
              <span>Reliability</span>
              <span>Last delivery</span>
              <span className="text-right">On</span>
            </div>
            {rows.map((row) => (
              <WebhookRow
                key={row.subscription.id}
                row={row}
                expanded={expanded.has(row.subscription.id)}
                onToggle={() => toggleExpanded(row.subscription.id)}
                onEdit={() => setEditing(row.subscription)}
              />
            ))}
          </div>
        )}
      </div>

      <WebhookFormDialog open={createOpen} onOpenChange={setCreateOpen} />
      <WebhookFormDialog
        open={editing !== undefined}
        onOpenChange={(open) => {
          if (!open) setEditing(undefined);
        }}
        subscription={editing}
      />
    </div>
  );
}

function GlobalPosture({ enabled, allowPrivate }: { enabled: boolean; allowPrivate: boolean }) {
  if (!enabled) {
    return (
      <span className="flex items-center gap-1.5 rounded-full border border-destructive/30 bg-destructive/10 px-2.5 py-1 text-xs text-destructive">
        <AlertTriangle className="size-3.5" aria-hidden="true" />
        Delivery globally disabled
      </span>
    );
  }
  return (
    <span className="flex items-center gap-1.5 rounded-full border border-border bg-muted px-2.5 py-1 text-xs text-muted-foreground">
      <Shield className="size-3.5" aria-hidden="true" />
      {allowPrivate ? 'Private targets allowed' : 'Private targets blocked'}
    </span>
  );
}

type WebhookRowProps = {
  row: WebhookRowModel;
  expanded: boolean;
  onToggle: () => void;
  onEdit: () => void;
};

function WebhookRow({ row, expanded, onToggle, onEdit }: WebhookRowProps) {
  const { subscription: sub, recent, lastAttempt, health } = row;
  const queryClient = useQueryClient();
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [testResult, setTestResult] = useState<WebhookTestResult | null>(null);
  const [testError, setTestError] = useState<string | null>(null);

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: queryKeys.webhooks.subscriptions() });
    queryClient.invalidateQueries({ queryKey: queryKeys.webhooks.deliveries() });
    queryClient.invalidateQueries({ queryKey: queryKeys.webhooks.status() });
  };

  // The per-row enable/disable toggle. Single-gesture, board-action-like — the
  // operator's attention is on the whole table, so a failure surfaces via the
  // global toast floor (the toast tier). No optimistic flip: the switch reflects
  // authoritative `enabled` after invalidation, and stays put on failure.
  const toggleMutation = useMutation({
    meta: { errorMessage: 'Couldn’t update the webhook.' },
    mutationFn: (next: boolean) => updateWebhookSubscription(sub.id, { enabled: next }),
    onSuccess: invalidate,
  });

  // The test delivery surfaces its outcome INLINE in the expanded panel — that's
  // the whole point of the affordance (skipToast). A success:false result is a
  // normal 200 response, shown as a failed test; a thrown error is a transport
  // failure, shown inline too.
  const testMutation = useMutation({
    meta: { skipToast: true },
    mutationFn: () => testWebhookSubscription(sub.id),
    onMutate: () => {
      setTestResult(null);
      setTestError(null);
    },
    onSuccess: (result) => {
      setTestResult(result);
      invalidate();
    },
    onError: (err) => setTestError(err instanceof Error ? err.message : String(err)),
  });

  // Delete is irreversible; a two-click confirm guards it. Failure → toast floor.
  const deleteMutation = useMutation({
    meta: { errorMessage: 'Couldn’t delete the webhook.' },
    mutationFn: () => deleteWebhookSubscription(sub.id),
    onSuccess: invalidate,
  });

  const rate = successRate(sub);
  const wildcard = isWildcard(sub.events);

  return (
    <div className={cn('border-b border-border last:border-b-0', expanded && 'bg-primary/[0.04]')}>
      {/* Always-visible row. Clicking the info area toggles expand; the switch
          cell stops propagation so it doesn't also collapse/expand. */}
      <div
        className={cn(
          'grid grid-cols-[1.6rem_minmax(0,2fr)_5rem_8rem_minmax(0,8rem)_2.5rem] items-center gap-3 px-3 py-2.5 transition-colors hover:bg-primary/[0.04]',
          !sub.enabled && 'opacity-60',
        )}
        onClick={onToggle}
      >
        <Button
          variant="ghost"
          size="icon-sm"
          aria-expanded={expanded}
          aria-label={expanded ? 'Collapse webhook details' : 'Expand webhook details'}
          onClick={(e) => {
            e.stopPropagation();
            onToggle();
          }}
        >
          <ChevronRight className={cn('transition-transform', expanded && 'rotate-90')} />
        </Button>

        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <span className="truncate text-sm font-medium">{sub.name || 'Unnamed webhook'}</span>
            <SignedBadge signed={sub.signed} />
            {!sub.enabled && (
              <Badge variant="outline" className="text-muted-foreground">
                Disabled
              </Badge>
            )}
            {health === 'blocked' && (
              <Badge
                variant="outline"
                className="border-destructive/30 bg-destructive/10 text-destructive"
              >
                <ShieldAlert aria-hidden="true" />
                Blocked
              </Badge>
            )}
          </div>
          <div className="truncate font-mono text-xs text-muted-foreground">{sub.url}</div>
        </div>

        <div className="text-xs text-muted-foreground">
          {wildcard ? (
            <span className="font-mono font-semibold text-primary">* all</span>
          ) : (
            <span>
              <span className="font-semibold text-foreground">{sub.events.length}</span>{' '}
              {sub.events.length === 1 ? 'event' : 'events'}
            </span>
          )}
        </div>

        <ReliabilityCell sub={sub} rate={rate} />

        <LastDeliveryCell health={health} sub={sub} lastAttempt={lastAttempt} />

        <div className="flex justify-end" onClick={(e) => e.stopPropagation()}>
          <Switch
            checked={sub.enabled}
            disabled={toggleMutation.isPending}
            onCheckedChange={(next) => toggleMutation.mutate(next)}
            aria-label={sub.enabled ? 'Disable webhook' : 'Enable webhook'}
          />
        </div>
      </div>

      {expanded && (
        <div className="grid gap-5 border-t border-border bg-primary/[0.03] px-4 py-4 pl-12 md:grid-cols-[1.3fr_1fr]">
          {/* Left — events, SSRF alert, actions, inline test result. */}
          <div className="flex flex-col gap-3">
            <div>
              <SectionHeading>Subscribed events</SectionHeading>
              <div className="flex flex-wrap gap-1.5">
                {wildcard ? (
                  <span className="rounded border border-primary/30 bg-primary/10 px-2 py-0.5 font-mono text-xs text-primary">
                    * all events
                  </span>
                ) : (
                  sub.events.map((event) => (
                    <span
                      key={event}
                      className="rounded border border-border bg-muted px-2 py-0.5 font-mono text-xs text-secondary-foreground"
                    >
                      {event}
                    </span>
                  ))
                )}
              </div>
            </div>

            {health === 'blocked' && <SsrfAlert />}

            <div className="flex flex-wrap gap-2">
              <Button variant="outline" size="sm" onClick={onEdit}>
                <Pencil aria-hidden="true" />
                Edit
              </Button>
              <Button
                variant="outline"
                size="sm"
                disabled={testMutation.isPending}
                onClick={() => testMutation.mutate()}
              >
                <Send aria-hidden="true" />
                {testMutation.isPending ? 'Sending…' : 'Send test'}
              </Button>
              {confirmDelete ? (
                <>
                  <Button
                    variant="destructive"
                    size="sm"
                    disabled={deleteMutation.isPending}
                    onClick={() => deleteMutation.mutate()}
                  >
                    <Trash2 aria-hidden="true" />
                    {deleteMutation.isPending ? 'Deleting…' : 'Confirm delete'}
                  </Button>
                  <Button variant="ghost" size="sm" onClick={() => setConfirmDelete(false)}>
                    Cancel
                  </Button>
                </>
              ) : (
                <Button variant="destructive" size="sm" onClick={() => setConfirmDelete(true)}>
                  <Trash2 aria-hidden="true" />
                  Delete
                </Button>
              )}
            </div>

            {testResult && <TestResultBox result={testResult} />}
            {testError && <InlineError message={`Test delivery failed: ${testError}`} />}
          </div>

          {/* Right — delivery-health strip + recent attempts. */}
          <div className="flex flex-col gap-3">
            <div>
              <SectionHeading>Delivery health</SectionHeading>
              <div className="flex items-center gap-4 rounded-md border border-border bg-card px-3 py-2.5">
                <HealthStat label="Success" value={formatSuccessRate(rate)} tone={health} />
                <div className="h-8 w-px bg-border" />
                <HealthStat label="Delivered" value={String(sub.successCount)} />
                <div className="h-8 w-px bg-border" />
                <HealthStat
                  label={health === 'blocked' ? 'Blocked' : 'Failed'}
                  value={String(sub.failureCount)}
                  tone={sub.failureCount > 0 ? 'failing' : undefined}
                />
              </div>
            </div>

            <div>
              <SectionHeading>Recent attempts</SectionHeading>
              {recent.length === 0 ? (
                <p className="text-xs text-muted-foreground">No delivery attempts recorded yet.</p>
              ) : (
                <div className="flex flex-col">
                  {recent.map((attempt) => (
                    <AttemptRow key={attempt.id} attempt={attempt} />
                  ))}
                </div>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

function SignedBadge({ signed }: { signed: boolean }) {
  return signed ? (
    <Badge variant="outline" className="text-muted-foreground">
      <Lock aria-hidden="true" />
      Signed
    </Badge>
  ) : (
    <Badge variant="outline" className="text-muted-foreground">
      <Unlock aria-hidden="true" />
      Unsigned
    </Badge>
  );
}

function ReliabilityCell({ sub, rate }: { sub: WebhookSubscription; rate: number | null }) {
  if (rate === null) {
    return <span className="text-xs text-muted-foreground">No deliveries</span>;
  }
  const successPct = rate * 100;
  return (
    <div className="text-xs">
      <span className="font-variant-numeric tabular-nums">
        <span className="font-semibold text-success">{sub.successCount.toLocaleString()}</span>
        <span className="text-muted-foreground"> · </span>
        <span className="font-semibold text-destructive">{sub.failureCount.toLocaleString()}</span>
      </span>
      {/* Runtime-data width (ratio), the same dynamic-dimension exception the
          color-picker sliders use — not a hardcoded design value. */}
      <div className="mt-1 flex h-1.5 w-full max-w-[7rem] overflow-hidden rounded-full bg-muted">
        <div className="bg-success" style={{ width: `${successPct}%` }} />
        <div className="bg-destructive" style={{ width: `${100 - successPct}%` }} />
      </div>
    </div>
  );
}

function LastDeliveryCell({
  health,
  sub,
  lastAttempt,
}: {
  health: WebhookHealth;
  sub: WebhookSubscription;
  lastAttempt: WebhookDelivery | undefined;
}) {
  const dotTone: Record<WebhookHealth, string> = {
    ok: 'bg-success',
    failing: 'bg-destructive',
    blocked: 'bg-destructive',
    disabled: 'bg-muted-foreground',
    idle: 'bg-muted-foreground',
  };
  const textTone: Record<WebhookHealth, string> = {
    ok: 'text-success',
    failing: 'text-destructive',
    blocked: 'text-destructive',
    disabled: 'text-muted-foreground',
    idle: 'text-muted-foreground',
  };

  let label: string;
  if (health === 'disabled' && sub.lastDeliveryStatus === null) label = 'Paused';
  else if (health === 'idle') label = 'No deliveries';
  else if (health === 'blocked') label = 'Private target';
  else if (lastAttempt?.httpStatusCode != null) label = String(lastAttempt.httpStatusCode);
  else label = sub.lastDeliveryStatus ?? '—';

  return (
    <div className="min-w-0 text-xs">
      <span className={cn('flex items-center gap-1.5 font-medium', textTone[health])}>
        <span className={cn('size-1.5 shrink-0 rounded-full', dotTone[health])} />
        <span className="truncate">{label}</span>
      </span>
      {sub.lastDeliveryAtUtc && (
        <div className="mt-0.5 text-muted-foreground">
          {formatRelativeTime(sub.lastDeliveryAtUtc)}
        </div>
      )}
    </div>
  );
}

function SectionHeading({ children }: { children: React.ReactNode }) {
  return (
    <div className="mb-2 text-[0.7rem] font-semibold tracking-wide text-muted-foreground uppercase">
      {children}
    </div>
  );
}

function HealthStat({
  label,
  value,
  tone,
}: {
  label: string;
  value: string;
  tone?: WebhookHealth | 'failing';
}) {
  const toneClass =
    tone === 'ok'
      ? 'text-success'
      : tone === 'failing' || tone === 'blocked'
        ? 'text-destructive'
        : 'text-foreground';
  return (
    <div className="flex flex-col">
      <span className="text-[0.65rem] tracking-wide text-muted-foreground uppercase">{label}</span>
      <span className={cn('mt-0.5 text-lg font-semibold tabular-nums', toneClass)}>{value}</span>
    </div>
  );
}

function AttemptRow({ attempt }: { attempt: WebhookDelivery }) {
  const blocked = isBlockedDelivery(attempt);
  const succeeded = attempt.status === 'Succeeded';
  const codeLabel = blocked
    ? 'blocked'
    : attempt.httpStatusCode != null
      ? String(attempt.httpStatusCode)
      : succeeded
        ? 'ok'
        : 'failed';
  return (
    <div className="grid grid-cols-[4.5rem_1fr_auto] items-center gap-2 border-t border-border py-1.5 text-xs first:border-t-0">
      <span className="text-muted-foreground">{formatRelativeTime(attempt.attemptedAtUtc)}</span>
      <span
        className="truncate font-mono text-xs text-secondary-foreground"
        title={attempt.error ?? undefined}
      >
        {attempt.eventType}
      </span>
      <Badge
        variant="outline"
        className={
          succeeded
            ? 'border-success/30 bg-success/10 text-success'
            : 'border-destructive/30 bg-destructive/10 text-destructive'
        }
      >
        {codeLabel}
      </Badge>
    </div>
  );
}

function TestResultBox({ result }: { result: WebhookTestResult }) {
  if (result.success) {
    return (
      <div className="flex items-start gap-2 rounded-md border border-success/30 bg-success/10 px-3 py-2 text-sm text-success">
        <CheckCircle2 className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
        <div>
          <p className="font-medium">
            Test delivered{result.statusCode != null ? ` — ${result.statusCode}` : ''}
          </p>
          <p className="text-xs text-success/80">Recorded in the delivery log · just now</p>
        </div>
      </div>
    );
  }
  return (
    <div className="flex items-start gap-2 rounded-md border border-destructive/30 bg-destructive/5 px-3 py-2 text-sm text-destructive">
      <XCircle className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
      <div>
        <p className="font-medium">
          Test failed{result.statusCode != null ? ` — ${result.statusCode}` : ''}
        </p>
        {result.error && <p className="text-xs break-words text-destructive/80">{result.error}</p>}
      </div>
    </div>
  );
}

function SsrfAlert() {
  return (
    <div className="flex items-start gap-2 rounded-md border border-accent/30 bg-accent/10 px-3 py-2 text-xs text-foreground">
      <AlertTriangle className="mt-0.5 size-4 shrink-0 text-accent" aria-hidden="true" />
      <div>
        Target resolves to a private or internal address — deliveries are blocked by SSRF
        protection. Set{' '}
        <code className="rounded bg-muted px-1 py-0.5 font-mono">
          Webhooks__AllowPrivateNetworkTargets=true
        </code>{' '}
        and restart to resume. The subscription is preserved and editable.
      </div>
    </div>
  );
}
