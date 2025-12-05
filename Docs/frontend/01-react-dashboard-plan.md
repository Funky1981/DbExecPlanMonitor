# React Dashboard Plan – Step-by-Step Build

This plan walks you through building a polished React dashboard with AI as a coding assistant. Follow the phases in order. Each phase lists **what you do** and an **AI prompt** you can paste into Copilot Chat or ChatGPT to generate scaffolding while you stay in control.

## Phase 0 – Prerequisites & Project Init
1. **Set up tooling**: Install Node 18+, npm, VS Code, and Git. Create a new repo.
2. **Scaffold**: Run `npm create vite@latest dashboard -- --template react-ts` (or `npx create-next-app@latest` for Next.js). `cd dashboard`.
3. **Quality tools**: `npm i -D eslint prettier eslint-config-prettier eslint-plugin-react eslint-plugin-react-hooks @typescript-eslint/eslint-plugin @typescript-eslint/parser husky lint-staged vitest @testing-library/react @testing-library/jest-dom @types/node`.
4. **Scripts**: Add `lint`, `format`, `test`, and `prepare` (for Husky) to `package.json`; configure `lint-staged` for `*.ts,*.tsx,*.css`.
5. **AI prompt**: “Configure ESLint + Prettier for Vite React TS; give me `.eslintrc`, `.prettierrc`, and `package.json` scripts with lint-staged.”

## Phase 1 – Design System & Layout Shell
1. **Choose UI system**: Tailwind (recommended) or component library (MUI/Chakra). Install and configure theme tokens (colors, spacing, radii, typography).
2. **Layout**: Build `MainLayout` with header, collapsible sidebar, content area, and footer. Ensure keyboard and mobile support.
3. **Navigation**: Define route structure (React Router or Next.js app router) with protected routes placeholder.
4. **Theme toggle**: Add light/dark toggle persisted in `localStorage` using CSS variables or Tailwind theme switcher.
5. **AI prompt**: “Create a responsive `MainLayout` with a collapsible sidebar and top bar using Tailwind; include aria labels, focus management, and a theme toggle persisted to localStorage.”

## Phase 2 – Dashboard Overview Page
1. **KPI cards**: Implement reusable `KpiCard` showing label, value, delta arrow, and optional sparkline placeholder.
2. **Charts**: Install `recharts` (or `@nivo/*`). Add time-series line/area chart, categorical bar chart, and donut chart with mock data and responsive containers.
3. **Filters**: Add a filter bar with date range presets, segment dropdown, and status pills. Wire to component state.
4. **Lists/Tables**: Add “Recent Activity” list and “Top Items” table with sortable columns and badges.
5. **AI prompt**: “Build `DashboardPage` with KPI grid, Recharts line/bar/donut charts, filter bar, recent activity list, and sortable top-items table. Provide mock data and responsive layout.”

## Phase 3 – Data Table & Detail Views
1. **Reusable table**: Create `DataTable` supporting column definitions, sorting, pagination, row selection, and bulk actions.
2. **Row actions**: Add per-row actions (view/edit/disable) with confirmation modal; include skeleton/loading states.
3. **Detail template**: Build `DetailPage` layout with summary header, stat tiles, timeline/activity feed, and related items panel (drawer or side column).
4. **AI prompt**: “Generate a typed `DataTable` component with checkbox selection, bulk actions, and loading skeletons; add a `DetailPage` template with summary, timeline, and related items drawer.”

## Phase 4 – Forms & Validation
1. **Libraries**: `npm i react-hook-form zod @hookform/resolvers`.
2. **Inputs**: Create shared inputs under `src/components/form` (text, textarea, select, multiselect, date picker, toggle, file upload) with consistent labels/help/errors.
3. **Schemas**: Define Zod schemas for key forms (e.g., create/edit item). Integrate with React Hook Form and show inline validation.
4. **Async select**: Add debounced async search select with loading state and empty fallback.
5. **AI prompt**: “Create reusable form controls wired to React Hook Form + Zod; include inline errors, disabled submit while submitting, and an async select with debounced search.”

## Phase 5 – Data Layer & State Management
1. **HTTP client**: Install `axios` (or fetch wrapper) and set up `apiClient` with auth/error interceptors.
2. **React Query**: `npm i @tanstack/react-query`. Add provider at app root and create hooks (`useDashboardMetrics`, `useItems`, `useActivityFeed`). Configure retries and stale times.
3. **Mutations**: Implement optimistic updates for quick actions; surface toast notifications for success/failure (Radix/Headless UI/Toast component).
4. **AI prompt**: “Set up React Query provider; create `apiClient` with interceptors; add `useDashboardMetrics`/`useItems` hooks with mock endpoints and optimistic mutation for toggling status.”

## Phase 6 – Theming, Motion, and Polish
1. **Design tokens**: Finalize color/spacing/shadow/typography tokens; ensure WCAG contrast.
2. **Primitives**: Buttons (primary/secondary/ghost/loading), Badge, Card, Tooltip, Modal/Drawer, Tabs, Alert/Toast, Skeleton, Empty states.
3. **Motion**: Add Framer Motion for gentle transitions (sidebar toggle, modals, dropdowns) with `prefers-reduced-motion` respected.
4. **AI prompt**: “Implement design tokens and Tailwind theme config; create Button, Card, Badge, Modal, Tabs, Toast, and Skeleton components with motion hooks for open/close transitions.”

## Phase 7 – Quality, Accessibility, and Testing
1. **Accessibility**: Keyboard navigation, focus traps in modals, aria-labels, skip-to-content link, semantic headings, and color-contrast checks.
2. **Testing**: Write Vitest + RTL tests for components (KpiCard, DataTable, FilterBar, forms). Add simple integration test for DashboardPage.
3. **Performance**: Add code-splitting (`React.lazy`) for heavy routes, memoize expensive lists/charts, and audit bundle with `npm run build -- --stats`.
4. **AI prompt**: “Write RTL tests for KpiCard, FilterBar, and DataTable interactions; add accessibility checks and ensure focus trapping in Modal.”

## Phase 8 – Documentation & Delivery
1. **Docs**: Add `README` sections for running, testing, and building. Document environment variables and feature flags.
2. **Storybook**: Run `npx storybook@latest init`; add stories for primitives and dashboard sections.
3. **Deployment**: Create production build, set CSP/security headers, and deploy to Vercel/Netlify/S3+CloudFront. Add monitoring hooks (Sentry/LogRocket).
4. **AI prompt**: “Generate Storybook stories for KpiCard, DataTable, charts, and DetailPage; add deployment checklist for Netlify/Vercel.”

## Daily Workflow Tips
- Keep each phase in its own branch or set of small commits.
- After AI suggestions, rewrite anything unclear; ask “why” questions in Copilot Chat to understand.
- Run `npm run lint` and `npm run test` frequently; fix issues before moving on.
- Maintain a `CHANGELOG.md` for notable UI/UX changes and decisions.
