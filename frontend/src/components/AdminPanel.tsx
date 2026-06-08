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
      <DialogContent className="sm:max-w-3xl h-[85vh] flex flex-col overflow-hidden p-6">
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

          {/* `data-[hidden]:hidden` lets the inactive (keepMounted) panels'
              `display:none` win over `flex` — without it every mounted panel
              flex-grows and the active panel collapses to a fraction of its
              height, breaking the inner scroll (#310). */}
          <TabsContent
            value="lanes"
            keepMounted
            className="flex min-h-0 flex-col p-1 data-[hidden]:hidden"
          >
            <LanesTab boardId={boardId} />
          </TabsContent>
          <TabsContent
            value="sizes"
            keepMounted
            className="flex min-h-0 flex-col p-1 data-[hidden]:hidden"
          >
            <SizesTab boardId={boardId} />
          </TabsContent>
          <TabsContent
            value="labels"
            keepMounted
            className="flex min-h-0 flex-col p-1 data-[hidden]:hidden"
          >
            <LabelsTab boardId={boardId} />
          </TabsContent>
          <TabsContent
            value="prune"
            keepMounted
            className="flex min-h-0 flex-col p-1 data-[hidden]:hidden"
          >
            <PruneTab boardId={boardId} />
          </TabsContent>
        </Tabs>
      </DialogContent>
    </Dialog>
  );
}
