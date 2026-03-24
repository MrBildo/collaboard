# Conventions

## C# Style
- File-scoped namespaces (required)
- Primary constructors where appropriate
- No `sealed` as blanket convention — only use when inheritance would be genuinely harmful
- Pattern matching: `is null`, `is not null`
- No XML doc comments — comment only complex business logic
- `var` for all local variables where type is apparent
- Private fields: `_camelCase` with underscore prefix
- All other members: PascalCase
- Interfaces: `IPascalCase` (prefix with I)
- Guard clauses: `ArgumentNullException.ThrowIfNull()`
- Collection expressions: `[]` instead of `new List<>()`
- Expression-bodied members for one-liners
- `.editorconfig` enforced — run `dotnet format` before committing

## Endpoint Structure
- Endpoints are organized in static classes under `backend/Collaboard.Api/Endpoints/`
- Each resource has its own file: `BoardEndpoints.cs`, `UserEndpoints.cs`, etc.
- Extension methods on `RouteGroupBuilder` map to `api.MapXxxEndpoints()`
- Program.cs is a thin composition root (builder, services, middleware, endpoint registration)

## Frontend Style
- Functional components with hooks
- shadcn/ui components from `@/components/ui/`
- `cn()` utility for conditional classes (from `@/lib/utils`)
- TanStack Query for all API calls
- Axios instance from `@/lib/api`
- 2-space indentation

## Formatting
- `.editorconfig` is the source of truth for all formatting
- C#: 4 spaces, frontend: 2 spaces
- Null safety: non-nullable reference types, null violations are errors

## Git
- **NEVER** commit directly to main
- Branch naming: `feature/`, `bugfix/`, `hotfix/`
- Conventional commits: `feat:`, `fix:`, `chore:`, `docs:`, `refactor:`
- Squash merge to main
- All changes via feature branch + PR
- **Delete branches after merge** — delete feature/bugfix branches on GitHub and locally after PRs are merged. Don't let stale branches accumulate. Use `gh pr merge --delete-branch` or delete manually.

### Multi-Card Branch Strategy

For multi-card features (e.g., an archive system spanning cards #163-#170), use a parent feature branch:

1. Create a parent feature branch (e.g., `feature/card-archive`) from the release branch
2. Sub-card branches PR into the parent feature branch — not directly into the release branch
3. Create a PR for each feature branch into the release branch (not direct merges) so each feature can be reviewed independently
4. The final release branch -> main PR is separate and covers the full feature
5. Add a comment to the card with the PR link when creating PRs

### Parallel Work Safety

Branches that touch overlapping files **must** be sequential, not parallel. Create each branch from the release branch after the previous one merges.

When planning parallel work, check file overlap first. Backend cards with different files can run in parallel. Frontend cards touching the same components must be sequenced. The dependency analysis step must drive execution order — not just be documented and ignored. If in doubt, sequence; the time saved by parallelism is lost to conflict resolution.

## Testing
- xUnit with Shouldly assertions (WebApplicationFactory, real in-memory SQLite, no mocking)
- Arrange-Act-Assert pattern
- Test classes per resource: `*EndpointTests.Tests.cs`
- Shared infrastructure: `Infrastructure/CollaboardApiFactory.cs`, `TestAuthHelper.cs`
- **No Playwright testing** — don't use Playwright MCP for visual testing unless the user specifically asks. Auth, dynamic Aspire ports, etc. make it unreliable. Rely on TypeScript checks, Vite builds, and backend tests for validation. Let the user do visual/browser testing.
