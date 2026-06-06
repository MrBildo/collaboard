export const ROLES = {
  Administrator: 0,
  Human: 1,
  Agent: 2,
  'Agent Admin': 3,
} as const;

export type Role = (typeof ROLES)[keyof typeof ROLES];
