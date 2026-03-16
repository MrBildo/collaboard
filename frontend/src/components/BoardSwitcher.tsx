import { useNavigate } from 'react-router-dom';
import type { Board } from '@/types';

type BoardSwitcherProps = {
  boards: Board[];
  currentSlug?: string;
};

export function BoardSwitcher({ boards, currentSlug }: BoardSwitcherProps) {
  const navigate = useNavigate();

  return (
    <select
      value={currentSlug ?? ''}
      onChange={(e) => navigate(`/boards/${e.target.value}`)}
      className="rounded-md border border-border bg-background px-2 py-1 text-sm text-foreground"
    >
      {boards.map((b) => (
        <option key={b.id} value={b.slug}>{b.name}</option>
      ))}
    </select>
  );
}
