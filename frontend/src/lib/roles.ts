export const ROLES = {
  Administrator: 0,
  Human: 1,
  Agent: 2,
  AgentAdmin: 3,
} as const;

export type Role = (typeof ROLES)[keyof typeof ROLES];
