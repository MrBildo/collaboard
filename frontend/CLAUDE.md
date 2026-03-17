# Frontend Design Conventions

## Colors

Use semantic Tailwind tokens, never hardcoded colors. The design system lives in `src/styles.css`.

```
bg-background    — page background
bg-card          — card/panel surfaces
bg-muted         — muted/recessed areas
bg-primary       — primary actions (cyan)
bg-accent        — highlights/badges (amber)
bg-destructive   — delete/error actions
text-foreground  — primary text
text-muted-foreground — secondary/helper text
border-border    — all borders
```

## Typography

- **Headings / UI labels:** Space Grotesk (font-sans via Tailwind)
- **Body / prose:** DM Sans (applied via prose contexts)
- Use Tailwind text utilities (`text-sm`, `text-base`, `text-lg`). No custom font sizes.

## Components

Always use shadcn/ui primitives from `@/components/ui/`. Never build raw HTML buttons, inputs, or dialogs.

- `<Button variant="default|outline|ghost|destructive" size="default|sm|lg">`
- `<Badge variant="default|secondary|outline">`
- `<Dialog>` / `<Sheet>` for modals and panels
- `<Input>`, `<Textarea>`, `<Select>` for form elements
- `<Tabs>`, `<Table>` for structured content
- `<Separator>` for visual dividers

## Cards

Kanban cards use: `rounded-lg shadow-sm border border-border bg-card p-3 hover:shadow-md transition-shadow`

## Icons

Use Lucide React (`lucide-react`). Import individual icons:
```tsx
import { Pencil, Trash2, Plus } from 'lucide-react';
```
Size: `className="w-4 h-4"` for inline, `"w-5 h-5"` for standalone.

## Dark Mode

Both light and dark themes are defined via CSS custom properties in `styles.css`. Dark mode activates via `data-theme="dark"` on the root element. Components should NOT use `dark:` Tailwind variants — the CSS vars handle everything automatically.

## Don'ts

- No inline styles except `image-rendering: pixelated` on the logo
- No `dark:` prefixed Tailwind classes — use the semantic tokens instead
- No new color values — if you need a color, it should come from the existing CSS vars
- No custom CSS classes — use Tailwind utilities composed via `cn()`
- No raw `<button>` or `<input>` elements — use shadcn components
