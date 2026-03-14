import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogDescription,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { createCard, fetchBoard } from '@/lib/api';
import type { Lane } from '@/types';

type CreateCardDialogProps = {
  lanes: Lane[];
  open: boolean;
  onOpenChange: (open: boolean) => void;
  defaultLaneId?: string;
};

export function CreateCardDialog({ lanes, open, onOpenChange, defaultLaneId }: CreateCardDialogProps) {
  const queryClient = useQueryClient();

  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [size, setSize] = useState('M');
  const [laneId, setLaneId] = useState(defaultLaneId ?? lanes[0]?.id ?? '');
  const [prevOpen, setPrevOpen] = useState(false);
  if (open !== prevOpen) {
    setPrevOpen(open);
    if (open) {
      setLaneId(defaultLaneId ?? lanes[0]?.id ?? '');
    }
  }

  // Fetch board data to calculate next position
  const boardQuery = useQuery({
    queryKey: ['board'],
    queryFn: fetchBoard,
  });

  const createMutation = useMutation({
    mutationFn: () => {
      const cards = boardQuery.data?.cards ?? [];
      const laneCards = cards.filter((c) => c.laneId === laneId);
      const maxPosition =
        laneCards.length > 0
          ? Math.max(...laneCards.map((c) => c.position))
          : 0;

      return createCard({
        name,
        descriptionMarkdown: description || undefined,
        size,
        laneId,
        position: maxPosition + 1,
      });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['board'] });
      resetForm();
      onOpenChange(false);
    },
  });

  const resetForm = () => {
    setName('');
    setDescription('');
    setSize('M');
    setLaneId(defaultLaneId ?? lanes[0]?.id ?? '');
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!name.trim()) return;
    createMutation.mutate();
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <form onSubmit={handleSubmit}>
          <DialogHeader>
            <DialogTitle>Create Card</DialogTitle>
            <DialogDescription>Add a new card to the board.</DialogDescription>
          </DialogHeader>

          <div className="mt-4 flex flex-col gap-4">
            {/* Name */}
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="new-card-name">Name *</Label>
              <Input
                id="new-card-name"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="Card name"
                required
              />
            </div>

            {/* Description */}
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="new-card-description">Description</Label>
              <Textarea
                id="new-card-description"
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                placeholder="Optional description (Markdown supported)"
                rows={4}
              />
            </div>

            {/* Size */}
            <div className="flex flex-col gap-1.5">
              <Label>Size</Label>
              <Select value={size} onValueChange={(v) => v && setSize(v)}>
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="S">S</SelectItem>
                  <SelectItem value="M">M</SelectItem>
                  <SelectItem value="L">L</SelectItem>
                  <SelectItem value="XL">XL</SelectItem>
                </SelectContent>
              </Select>
            </div>

            {/* Lane */}
            <div className="flex flex-col gap-1.5">
              <Label>Lane</Label>
              <Select value={laneId} onValueChange={(v) => v && setLaneId(v)}>
                <SelectTrigger>
                  <SelectValue placeholder="Select lane">
                    {lanes.find((l) => l.id === laneId)?.name ?? 'Select lane'}
                  </SelectValue>
                </SelectTrigger>
                <SelectContent>
                  {lanes.map((lane) => (
                    <SelectItem key={lane.id} value={lane.id}>
                      {lane.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>

          <DialogFooter className="mt-4">
            <Button
              type="button"
              variant="outline"
              onClick={() => onOpenChange(false)}
            >
              Cancel
            </Button>
            <Button type="submit" disabled={createMutation.isPending || !name.trim()}>
              {createMutation.isPending ? 'Creating...' : 'Create'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
