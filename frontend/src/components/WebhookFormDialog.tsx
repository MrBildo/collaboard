import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';

import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Badge } from '@/components/ui/badge';
import { Switch } from '@/components/ui/switch';
import { Checkbox } from '@/components/ui/checkbox';
import { InlineError } from '@/components/ui/inline-error';
import { createWebhookSubscription, updateWebhookSubscription } from '@/lib/api';
import {
  WEBHOOK_WILDCARD,
  buildWebhookCreateInput,
  buildWebhookUpdatePatch,
  isWildcard,
  type WebhookFormState,
} from '@/lib/webhooks';
import { useWebhookEventCatalog } from '@/hooks/use-webhooks';
import { queryKeys } from '@/lib/query-keys';
import { toMessage } from '@/lib/mutation-floor';
import type { CreateWebhookInput, UpdateWebhookPatch, WebhookSubscription } from '@/types';

type WebhookFormDialogProps = {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  // undefined = create; present = edit. The form is keyed by this in the parent
  // so it remounts (and re-initializes) when the target changes.
  subscription?: WebhookSubscription;
};

export function WebhookFormDialog({ open, onOpenChange, subscription }: WebhookFormDialogProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      {/* This dialog opens from inside the Admin Panel dialog, so base-ui treats
          it as nested and (by default) renders no backdrop — leaving it
          non-modal: no dim/blur, background stays clickable. forceMountOverlay
          restores the backdrop so it's a true modal over the panel, matching the
          panel's own dim+blur. max-h-[90vh] gives the tall, vertically-biased
          form its own portrait envelope rather than the panel's height. */}
      <DialogContent forceMountOverlay className="flex max-h-[90vh] flex-col gap-4 sm:max-w-lg">
        {/* Keyed remount resets all field state when switching create ↔ edit or
            between two subscriptions, without a useEffect sync. */}
        <WebhookForm
          key={subscription?.id ?? 'new'}
          subscription={subscription}
          onDone={() => onOpenChange(false)}
        />
      </DialogContent>
    </Dialog>
  );
}

type WebhookFormProps = {
  subscription?: WebhookSubscription;
  onDone: () => void;
};

function WebhookForm({ subscription, onDone }: WebhookFormProps) {
  const isEdit = subscription !== undefined;
  const queryClient = useQueryClient();

  // The selectable event catalog is the server's source of truth (#336), fetched
  // on first open and cached for the session. The per-event checkboxes render
  // from it; "Send all events" (the wildcard) is independent, so a still-loading
  // or failed catalog never blocks creating a wildcard subscription.
  const catalogQuery = useWebhookEventCatalog();
  const eventGroups = catalogQuery.data ?? [];

  const [name, setName] = useState(subscription?.name ?? '');
  const [url, setUrl] = useState(subscription?.url ?? '');
  const [enabled, setEnabled] = useState(subscription?.enabled ?? true);
  const [sendAll, setSendAll] = useState(subscription ? isWildcard(subscription.events) : false);
  // Initialize the per-event selection from the subscription's concrete events
  // (the wildcard is carried by `sendAll`, not a checkbox). Catalog-independent
  // so it doesn't race the fetch — the boxes pre-check once the catalog renders.
  const [selected, setSelected] = useState<string[]>(
    subscription ? subscription.events.filter((e) => e !== WEBHOOK_WILDCARD) : [],
  );
  // The signing secret is write-only: we never receive it, only `signed`. The
  // input holds a NEW value (replace); `clearSecret` removes it (edit only).
  const [secret, setSecret] = useState('');
  const [clearSecret, setClearSecret] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const invalidate = () => {
    queryClient.invalidateQueries({ queryKey: queryKeys.webhooks.subscriptions() });
    queryClient.invalidateQueries({ queryKey: queryKeys.webhooks.status() });
  };

  // Form mutations are inline tier (#203): the operator's attention is on this
  // dialog, so failures surface inline here (skipToast keeps the floor quiet).
  const createMutation = useMutation({
    meta: { skipToast: true },
    mutationFn: (input: CreateWebhookInput) => createWebhookSubscription(input),
    onSuccess: () => {
      invalidate();
      onDone();
    },
    onError: (err) => setError(toMessage(err)),
  });

  const updateMutation = useMutation({
    meta: { skipToast: true },
    mutationFn: ({ id, patch }: { id: string; patch: UpdateWebhookPatch }) =>
      updateWebhookSubscription(id, patch),
    onSuccess: () => {
      invalidate();
      onDone();
    },
    onError: (err) => setError(toMessage(err)),
  });

  const isPending = createMutation.isPending || updateMutation.isPending;
  const hasEvents = sendAll || selected.length > 0;
  const canSubmit = url.trim().length > 0 && hasEvents && !isPending;

  const toggleEvent = (type: string, checked: boolean) => {
    setSelected((prev) => (checked ? [...prev, type] : prev.filter((t) => t !== type)));
  };

  const handleSubmit = () => {
    setError(null);
    if (!url.trim()) {
      setError('A payload URL is required.');
      return;
    }
    if (!hasEvents) {
      setError('Select at least one event, or enable "Send all events".');
      return;
    }
    const state: WebhookFormState = { url, name, enabled, sendAll, selected, secret, clearSecret };

    if (isEdit && subscription) {
      const patch = buildWebhookUpdatePatch(subscription, state);
      if (Object.keys(patch).length === 0) {
        onDone();
        return;
      }
      updateMutation.mutate({ id: subscription.id, patch });
      return;
    }

    createMutation.mutate(buildWebhookCreateInput(state));
  };

  return (
    <>
      <DialogHeader>
        <DialogTitle>{isEdit ? 'Edit webhook' : 'New webhook'}</DialogTitle>
        <DialogDescription>Deliver board events to an external URL.</DialogDescription>
      </DialogHeader>

      {/* Negative margin + matching padding gives the inputs' focus ring (3px)
          room to render inside this overflow-clip scroll container without
          indenting content from the header/footer edges (#338). */}
      <div className="-m-2 flex min-h-0 flex-col gap-4 overflow-y-auto p-2">
        <div className="grid grid-cols-[1fr_auto] items-end gap-3">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="wh-name">
              Name <span className="font-normal text-muted-foreground">(optional)</span>
            </Label>
            <Input
              id="wh-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              maxLength={120}
              placeholder="e.g. automation prod"
            />
          </div>
          <label className="flex h-8 items-center gap-2 text-sm" htmlFor="wh-enabled">
            <span className="font-medium">Enabled</span>
            <Switch id="wh-enabled" checked={enabled} onCheckedChange={setEnabled} />
          </label>
        </div>

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="wh-url">Payload URL</Label>
          <Input
            id="wh-url"
            value={url}
            onChange={(e) => setUrl(e.target.value)}
            className="font-mono text-xs"
            placeholder="https://example.com/webhooks/collaboard"
          />
          <p className="text-xs text-muted-foreground">
            <code className="font-mono">http</code>/<code className="font-mono">https</code> only.
            Private or internal targets are blocked unless the server allows them.
          </p>
        </div>

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="wh-secret">
            Signing secret <span className="font-normal text-muted-foreground">(optional)</span>
          </Label>
          <div className="flex items-center gap-2">
            <Badge variant={subscription?.signed ? 'secondary' : 'outline'}>
              {subscription?.signed ? 'Signed' : 'Unsigned'}
            </Badge>
            <span className="text-xs text-muted-foreground">
              {subscription?.signed
                ? 'A secret is set — deliveries are signed (HMAC-SHA256).'
                : 'No secret set — deliveries are unsigned.'}
            </span>
          </div>
          <Input
            id="wh-secret"
            type="password"
            value={secret}
            onChange={(e) => setSecret(e.target.value)}
            disabled={clearSecret}
            autoComplete="new-password"
            className="font-mono text-xs"
            placeholder={
              isEdit ? 'Leave blank to keep the current secret' : 'Set a secret to sign deliveries'
            }
          />
          <p className="text-xs text-muted-foreground">
            <span className="font-medium text-foreground">Write-only.</span> Stored once, never
            shown again.
            {isEdit ? ' Leave blank to keep · type a new value to replace.' : ''}
          </p>
          {isEdit && subscription?.signed && (
            <label
              className="flex items-center gap-2 text-xs text-muted-foreground"
              htmlFor="wh-clear"
            >
              <Checkbox
                id="wh-clear"
                checked={clearSecret}
                onCheckedChange={(checked) => {
                  setClearSecret(checked === true);
                  if (checked === true) setSecret('');
                }}
              />
              Clear the secret (deliveries go unsigned)
            </label>
          )}
        </div>

        <div className="flex flex-col gap-2">
          <Label>Events</Label>
          <label
            className="flex items-center justify-between gap-3 rounded-md border border-dashed border-primary/40 bg-primary/5 px-3 py-2"
            htmlFor="wh-sendall"
          >
            <span className="flex flex-col gap-0.5">
              <span className="text-sm font-medium">Send all events</span>
              <span className="text-xs text-muted-foreground">
                The <code className="font-mono">*</code> wildcard — includes events added in future
                releases.
              </span>
            </span>
            <Switch id="wh-sendall" checked={sendAll} onCheckedChange={setSendAll} />
          </label>

          {/* The per-event picker. The catalog is the server's source of truth;
              while it loads or if it fails, the wildcard toggle above still lets
              the operator subscribe — the failure surfaces inline, never silent. */}
          {catalogQuery.isLoading ? (
            <p className="rounded-md border border-border px-3 py-6 text-center text-xs text-muted-foreground">
              Loading events…
            </p>
          ) : catalogQuery.isError ? (
            <InlineError message="Couldn't load the event catalog. Use “Send all events” above, or close and reopen to retry." />
          ) : (
            eventGroups.map((group) => (
              <div key={group.family} className="overflow-hidden rounded-md border border-border">
                <div className="flex items-center justify-between bg-muted px-3 py-2 text-sm font-medium">
                  <span>{group.label}</span>
                  <span className="text-xs font-normal text-muted-foreground">
                    {sendAll
                      ? `${group.events.length} / ${group.events.length}`
                      : `${group.events.filter((e) => selected.includes(e.type)).length} / ${group.events.length}`}
                  </span>
                </div>
                <div className="flex flex-col gap-1 p-2">
                  {group.events.map((event) => {
                    const checked = sendAll || selected.includes(event.type);
                    return (
                      <label
                        key={event.type}
                        className={
                          sendAll
                            ? 'flex items-start gap-2.5 rounded px-2 py-1.5 opacity-60'
                            : 'flex cursor-pointer items-start gap-2.5 rounded px-2 py-1.5 hover:bg-muted'
                        }
                      >
                        <Checkbox
                          className="mt-0.5"
                          checked={checked}
                          disabled={sendAll}
                          onCheckedChange={(c) => toggleEvent(event.type, c === true)}
                        />
                        <span className="flex flex-col gap-0.5">
                          <span className="font-mono text-xs text-foreground">{event.label}</span>
                          <span className="text-xs text-muted-foreground">{event.description}</span>
                        </span>
                      </label>
                    );
                  })}
                </div>
              </div>
            ))
          )}
          <p className="text-xs text-muted-foreground">At least one event is required.</p>
        </div>

        {error && <InlineError message={error} />}
      </div>

      <DialogFooter className="items-center sm:justify-between">
        <p className="text-xs text-muted-foreground">
          {isEdit ? 'Changes apply immediately.' : 'Saving creates the subscription.'}
        </p>
        <div className="flex gap-2">
          <Button variant="ghost" onClick={onDone} disabled={isPending}>
            Cancel
          </Button>
          <Button onClick={handleSubmit} disabled={!canSubmit}>
            {isPending ? 'Saving…' : isEdit ? 'Save changes' : 'Create webhook'}
          </Button>
        </div>
      </DialogFooter>
    </>
  );
}
