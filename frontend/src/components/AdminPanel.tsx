import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
} from '@/components/ui/dialog';
import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/tabs';
import { LanesTab } from '@/components/LanesTab';
import { SizesTab } from '@/components/SizesTab';
import { LabelsTab } from '@/components/LabelsTab';
import { PruneTab } from '@/components/PruneTab';

type AdminPanelProps = {
  boardId: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
};

export function AdminPanel({ boardId, open, onOpenChange }: AdminPanelProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-3xl max-h-[85vh] flex flex-col overflow-hidden p-6">
        <DialogHeader>
          <DialogTitle>Board Configuration</DialogTitle>
          <DialogDescription>
            Manage lanes, sizes, labels, and prune cards for this board.
          </DialogDescription>
        </DialogHeader>

        <Tabs defaultValue="lanes" className="mt-2 flex min-h-0 flex-col gap-4">
          <TabsList variant="line" className="w-full justify-start gap-2 border-b pb-2">
            <TabsTrigger value="lanes">Lanes</TabsTrigger>
            <TabsTrigger value="sizes">Sizes</TabsTrigger>
            <TabsTrigger value="labels">Labels</TabsTrigger>
            <TabsTrigger value="prune">Prune</TabsTrigger>
          </TabsList>

          <TabsContent value="lanes" className="overflow-y-auto p-1">
            <LanesTab boardId={boardId} />
          </TabsContent>
          <TabsContent value="sizes" className="overflow-y-auto p-1">
            <SizesTab boardId={boardId} />
          </TabsContent>
          <TabsContent value="labels" className="overflow-y-auto p-1">
            <LabelsTab boardId={boardId} />
          </TabsContent>
          <TabsContent value="prune" className="overflow-y-auto p-1">
            <PruneTab boardId={boardId} />
          </TabsContent>
        </Tabs>
      </DialogContent>
    </Dialog>
  );
}
