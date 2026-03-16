import { useState } from 'react';

export function useEditableList() {
  const [editingId, setEditingId] = useState<string | null>(null);
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  const startEdit = (id: string) => {
    setEditingId(id);
    setConfirmDeleteId(null);
  };

  const cancelEdit = () => setEditingId(null);

  const confirmDelete = (id: string) => {
    setConfirmDeleteId(id);
    setDeleteError(null);
  };

  const cancelDelete = () => {
    setConfirmDeleteId(null);
    setDeleteError(null);
  };

  const clearDelete = () => {
    setConfirmDeleteId(null);
    setDeleteError(null);
  };

  return {
    editingId,
    confirmDeleteId,
    deleteError,
    startEdit,
    cancelEdit,
    confirmDelete,
    cancelDelete,
    clearDelete,
    setEditingId,
    setDeleteError,
  };
}
