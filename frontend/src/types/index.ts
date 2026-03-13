export type Lane = { id: string; name: string; position: number };
export type CardItem = {
  id: string;
  number: number;
  name: string;
  descriptionMarkdown: string;
  laneId: string;
  position: number;
  status?: string;
  size: string;
};
