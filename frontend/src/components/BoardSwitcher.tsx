import { useNavigate } from 'react-router-dom';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import type { Board } from '@/types';

type BoardSwitcherProps = {
  boards: Board[];
  currentSlug?: string;
};

export function BoardSwitcher({ boards, currentSlug }: BoardSwitcherProps) {
  const navigate = useNavigate();

  return (
    <Select
      value={currentSlug ?? ''}
      onValueChange={(v) => navigate(`/boards/${v}`)}
    >
      <SelectTrigger size="sm" className="max-w-[12rem] sm:max-w-[16rem]">
        <SelectValue>
          {boards.find((b) => b.slug === currentSlug)?.name ?? 'Select board'}
        </SelectValue>
      </SelectTrigger>
      <SelectContent>
        {boards.map((b) => (
          <SelectItem key={b.id} value={b.slug}>{b.name}</SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}
