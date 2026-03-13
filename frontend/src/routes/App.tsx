import { DndContext, DragEndEvent, useDraggable, useDroppable } from '@dnd-kit/core';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import clsx from 'clsx';
import { useMemo } from 'react';
import ReactMarkdown from 'react-markdown';
import { ThemeToggle } from '../components/ThemeToggle';
import { api, CardItem, fetchBoard, Lane } from '../lib/api';

export function App() {
  const queryClient = useQueryClient();
  const boardQuery = useQuery({ queryKey: ['board'], queryFn: fetchBoard });

  const lanes = boardQuery.data?.lanes ?? [];
  const cards = boardQuery.data?.cards ?? [];

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

    const card = cards.find((c) => c.id === cardId);
    if (!card || card.laneId === overLaneId) return;

    queryClient.setQueryData<{ lanes: Lane[]; cards: CardItem[] }>(['board'], (old) => {
      if (!old) return old;
      return {
        ...old,
        cards: old.cards.map((c) => (c.id === cardId ? { ...c, laneId: overLaneId } : c)),
      };
    });

    api.patch(`/cards/${cardId}`, { laneId: overLaneId });
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
  const { setNodeRef, isOver } = useDroppable({ id: lane.id });

  return (
    <article
      ref={setNodeRef}
      className={clsx(
        'rounded-lg border bg-white p-3 shadow dark:bg-slate-900',
        isOver && 'ring-2 ring-blue-400',
      )}
    >
      <h2 className="mb-3 font-semibold">{lane.name}</h2>
      <div className="space-y-3">
        {cards.map((card) => (
          <DraggableCard key={card.id} card={card} />
        ))}
      </div>
    </article>
  );
}

function DraggableCard({ card }: { card: CardItem }) {
  const { attributes, listeners, setNodeRef, transform } = useDraggable({ id: card.id });
  const style = transform
    ? { transform: `translate(${transform.x}px, ${transform.y}px)` }
    : undefined;

  return (
    <div
      ref={setNodeRef}
      style={style}
      {...listeners}
      {...attributes}
      className={clsx(
        'cursor-grab rounded border bg-white p-2 dark:bg-slate-800',
        card.status === 'Blocked' ? 'border-red-500' : 'border-slate-300 dark:border-slate-600',
      )}
    >
      <p className="text-xs text-slate-500">#{card.number}</p>
      <h3 className="font-medium">{card.name}</h3>
      <ReactMarkdown className="mt-1 text-sm text-slate-600 dark:text-slate-300">
        {card.descriptionMarkdown}
      </ReactMarkdown>
      <p className="mt-2 text-xs">Size: {card.size}</p>
    </div>
  );
}
