import { DndContext, DragEndEvent } from '@dnd-kit/core';
import { useQuery } from '@tanstack/react-query';
import clsx from 'clsx';
import { useMemo, useState } from 'react';
import { ThemeToggle } from '../components/ThemeToggle';
import { CardItem, fetchBoard, Lane } from '../lib/api';

export function App() {
  const boardQuery = useQuery({ queryKey: ['board'], queryFn: fetchBoard });
  const [localCards, setLocalCards] = useState<CardItem[]>([]);

  const lanes = boardQuery.data?.lanes ?? [];
  const cards = localCards.length ? localCards : (boardQuery.data?.cards ?? []);

  const byLane = useMemo(() => {
    const map = new Map<string, CardItem[]>();
    lanes.forEach((lane) => map.set(lane.id, []));
    cards.forEach((card) => map.get(card.laneId)?.push(card));
    map.forEach((list) => list.sort((a, b) => a.position - b.position));
    return map;
  }, [lanes, cards]);

  const onDragEnd = (event: DragEndEvent) => {
    const cardId = String(event.active.id);
    const overLaneId = event.over?.id ? String(event.over.id) : null;
    if (!overLaneId) return;

    setLocalCards((prev) => {
      const source = (prev.length ? prev : cards).map((card) => ({ ...card }));
      const card = source.find((x) => x.id === cardId);
      if (!card) return source;
      card.laneId = overLaneId;
      return source;
    });
  };

  return (
    <main className="min-h-screen bg-slate-100 p-4 text-slate-900 dark:bg-slate-950 dark:text-slate-100">
      <header className="mb-6 flex items-center justify-between">
        <h1 className="text-3xl font-bold">Collaboard</h1>
        <ThemeToggle />
      </header>
      <DndContext onDragEnd={onDragEnd}>
        <section className="grid grid-cols-1 gap-4 md:grid-cols-3" aria-label="Kanban board">
          {lanes.map((lane) => (
            <LaneColumn key={lane.id} lane={lane} cards={byLane.get(lane.id) ?? []} />
          ))}
        </section>
      </DndContext>
    </main>
  );
}

function LaneColumn({ lane, cards }: { lane: Lane; cards: CardItem[] }) {
  return (
    <article id={lane.id} className="rounded-lg border bg-white p-3 shadow dark:bg-slate-900">
      <h2 className="mb-3 font-semibold">{lane.name}</h2>
      <div className="space-y-3">
        {cards.map((card) => (
          <div
            key={card.id}
            id={card.id}
            className={clsx('rounded border p-2', card.status === 'Blocked' ? 'border-red-500' : 'border-slate-300')}
          >
            <p className="text-xs text-slate-500">#{card.number}</p>
            <h3 className="font-medium">{card.name}</h3>
            <p className="mt-1 text-sm text-slate-600 dark:text-slate-300">{card.descriptionMarkdown}</p>
            <p className="mt-2 text-xs">Size: {card.size}</p>
          </div>
        ))}
      </div>
    </article>
  );
}
