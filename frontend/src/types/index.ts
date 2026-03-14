export type Lane = {
  id: string;
  name: string;
  position: number;
};

export type CardItem = {
  id: string;
  number: number;
  name: string;
  descriptionMarkdown: string;
  laneId: string;
  position: number;
  blocked?: string | null;
  size: string;
  createdByUserId: string;
  createdAtUtc: string;
  lastUpdatedByUserId: string;
  lastUpdatedAtUtc: string;
};

export type Label = {
  id: string;
  name: string;
  color?: string | null;
};

export type CardComment = {
  id: string;
  cardId: string;
  userId: string;
  contentMarkdown: string;
  lastUpdatedAtUtc: string;
};

export type BoardUser = {
  id: string;
  name: string;
  role: number;
  authKey: string;
  isActive: boolean;
};
