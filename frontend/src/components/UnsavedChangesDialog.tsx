import { Dialog, DialogContent } from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { TriangleAlert } from 'lucide-react';

type UnsavedChangesAction = 'save' | 'discard' | 'cancel';

type UnsavedChangesDialogProps = {
  open: boolean;
  onAction: (action: UnsavedChangesAction) => void;
};

export function UnsavedChangesDialog({ open, onAction }: UnsavedChangesDialogProps) {
  return (
    <Dialog
      open={open}
      onOpenChange={(nextOpen) => {
        if (!nextOpen) onAction('cancel');
      }}
    >
      <DialogContent
        className="flex flex-col gap-0 p-0 sm:max-w-[420px]"
        showCloseButton={false}
      >
        <div className="px-6 py-5">
          <div className="mb-4 flex h-10 w-10 items-center justify-center rounded-full bg-accent/15">
            <TriangleAlert className="h-5 w-5 text-accent" />
          </div>
          <h2 className="text-base font-bold">Unsaved Changes</h2>
          <p className="mt-2 text-sm text-muted-foreground">
            You have unsaved changes that will be lost if you close this card.
          </p>
        </div>
        <div className="flex flex-col-reverse gap-2 border-t px-6 py-4 sm:flex-row sm:justify-end">
          <Button
            variant="outline"
            onClick={() => onAction('cancel')}
            className="text-muted-foreground"
          >
            Keep Editing
          </Button>
          <Button
            variant="outline"
            onClick={() => onAction('discard')}
            className="border-destructive text-destructive hover:bg-destructive/10"
          >
            Discard
          </Button>
          <Button onClick={() => onAction('save')}>
            Save & Close
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}

export type { UnsavedChangesAction };
