import { useNavigate } from 'react-router-dom';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip';
import type { Board } from '@/types';

type BoardSwitcherProps = {
  boards: Board[];
  currentSlug?: string;
};

export function BoardSwitcher({ boards, currentSlug }: BoardSwitcherProps) {
  const navigate = useNavigate();
  const currentBoard = boards.find((b) => b.slug === currentSlug);

  return (
    <Select value={currentSlug ?? ''} onValueChange={(v) => navigate(`/boards/${v}`)}>
      <Tooltip>
        <TooltipTrigger
          render={<SelectTrigger size="sm" className="min-w-[7rem] max-w-[10rem] flex-1" />}
        >
          <SelectValue>{currentBoard?.name ?? 'Select board'}</SelectValue>
        </TooltipTrigger>
        <TooltipContent>{currentBoard?.name ?? 'Select board'}</TooltipContent>
      </Tooltip>
      <SelectContent>
        {boards.map((b) => (
          <SelectItem key={b.id} value={b.slug}>
            {b.name}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}
