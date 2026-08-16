// The board's single <DndContext> hosts two drag concerns — card
// reorder (use-board-dnd) and lane reorder (use-lane-dnd). Each drag handler is
// routed to the right hook by the active draggable's data.type. Lanes tag
// themselves 'lane' (LaneColumn's useSortable data); cards carry no type, so
// "not 'lane'" is the card path — keeping the existing card hook untouched.
type ActiveDragEvent = {
  active: { data: { current?: { type?: string } } };
};

export function isLaneDragEvent(event: ActiveDragEvent): boolean {
  return event.active.data.current?.type === 'lane';
}
