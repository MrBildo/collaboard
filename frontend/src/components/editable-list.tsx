import type { ReactNode } from 'react';
import { Pencil, Trash2 } from 'lucide-react';
import { Button } from '@/components/ui/button';

type ItemActionsProps = {
  isConfirmingDelete: boolean;
  isDeleting: boolean;
  onEdit: () => void;
  onDelete: () => void;
};

export function ItemActions({ isConfirmingDelete, isDeleting, onEdit, onDelete }: ItemActionsProps) {
  return (
    <div className="flex gap-1">
      <Button size="xs" variant="ghost" onClick={onEdit} title="Edit">
        <Pencil className="h-3.5 w-3.5" />
      </Button>
      <Button
        size="xs"
        variant="ghost"
        className="text-destructive hover:text-destructive"
        onClick={onDelete}
        disabled={isDeleting}
        title={isConfirmingDelete ? 'Confirm delete' : 'Delete'}
      >
        {isConfirmingDelete ? 'Confirm' : <Trash2 className="h-3.5 w-3.5" />}
      </Button>
    </div>
  );
}

type EditFormActionsProps = {
  onSave: () => void;
  onCancel: () => void;
  isPending: boolean;
};

export function EditFormActions({ onSave, onCancel, isPending }: EditFormActionsProps) {
  return (
    <div className="ml-2 flex gap-1">
      <Button size="xs" onClick={onSave} disabled={isPending}>
        Save
      </Button>
      <Button size="xs" variant="outline" onClick={onCancel}>
        Cancel
      </Button>
    </div>
  );
}

type EditableListContainerProps = {
  children: ReactNode;
  error?: string | null;
};

export function EditableListContainer({ children, error }: EditableListContainerProps) {
  return (
    <>
      <div className="flex flex-col divide-y divide-border rounded-lg border">
        {children}
      </div>
      {error && <p className="text-sm text-destructive">{error}</p>}
    </>
  );
}

type EditableListRowProps = {
  children: ReactNode;
};

export function EditableListRow({ children }: EditableListRowProps) {
  return (
    <div className="flex items-center justify-between px-4 py-3 transition-colors hover:bg-muted/50">
      {children}
    </div>
  );
}
