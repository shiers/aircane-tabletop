# Frontend layout standardization: shared AppLayout shell, background removal, and view fixes

This change centralizes page layout for the Vue frontend into `AppLayout.vue` plus a set of shared `@layer components` primitives in `main.css` (`.page-container`, `.page-sections`, `.app-hero*`, `.page-plain`), then retrofits each in-scope view to consume them instead of its own bespoke hero/width/background CSS. It also removes full-page background art from five named views (replacing it with a solid `#0a0a1a` fill applied at the shell level), swaps the Campaigns "New Campaign" button to the dashboard art asset, de-duplicates the "Your Campaigns" heading, increases About page spacing, and reworks Join Session so the join form always renders independent of the lookup-error panel. The approach is genuinely centralizing rather than copy-pasting: the shell owns width/centering/padding and the per-view CSS is deleted, which is the right direction for the stated goal.

Watch for: a 24px top gap (`pt-6` on `<main>`) added on top of the spec'd `0` top padding in `.page-container` — a minor, uniform deviation, not a divergence (confirmed). The "Dice Roller" view named in requirement #1 has no standalone route/view in the app, so it could not be standardized (confirmed). `sessions` (the `/sessions` index) was added to the plain-background set alongside `join-session`, slightly beyond the literal Join Session scope (confirmed, harmless).

**Verdict**: APPROVED

## High-level view

The shell is the single source of truth. `AppLayout.vue`'s `<main>` wraps `<slot/>` in a `.page-container` (max-width 1200px, `margin:0 auto`, `padding:0 32px 48px 32px`), and every in-scope view had its own `mx-auto max-w-*` wrapper removed, so content width/centering is now decided in exactly one place and applies to Dashboard too. The sidebar fix is equally centralized: `AppSidebar` is one shared component, and `w-60` (240px) was replaced with a scoped `.sidebar-expanded { width:220px }` plus `shrink-0`, guaranteeing identical 220px width on every route including Dashboard.

Heroes are standardized through `.app-hero` (min-height 240px) with `.app-hero__overlay` (absolute, bottom-left, 24px padding), `.app-hero__title` (white 24px/700) and `.app-hero__subtitle` (grey #9ca3af 14px). All eight non-dashboard views adopt these classes and their old per-view `.hero`/`.library-hero`/`.forge-hero`/`.settings-hero` blocks are deleted. Dashboard keeps its taller bespoke hero as the documented exemption the spec allows.

Background removal is done at the shell, not per view. The five named routes are listed in a `plainBackgroundRoutes` set and `AppLayout` toggles `.page-plain` (`#0a0a1a`) on `<main>`, so the solid fill covers the full content viewport including gutters. Each view's full-page `v-bg-asset` and `bg-cover/bg-fixed` root classes are removed, while panel/card/button art (scoped elements with gold borders) is preserved.

The Campaigns button reuses the dashboard's exact `.new-campaign-button` recipe (same art, `size:100% 100%`, same 180x44 dimensions), the duplicate "Your Campaigns" heading is removed from `CampaignsView` leaving the single one in `CampaignList`, About gets 40px section gaps and 32px/28px panel padding, and Join Session's form now renders in a `v-else` branch independent of the error panel with a `hideError` prop preventing the previous double-rendered error.

<details>
<summary>Issues (3)</summary>

1. **Top padding deviation** — `<main>` keeps `pt-6` (24px) above the `.page-container` whose spec'd top padding is `0`, so content sits 24px lower than the literal `0 32px 48px 32px`. Uniform across all routes and arguably desirable; leave as-is or drop `pt-6` if strict spec match is wanted.
2. **Dice Roller has no view to standardize** — requirement #1 lists "Dice Roller" as a main view, but there is no `/dice` route or `DiceRollerView`; dice lives inside session views and the dashboard tile links to `/sessions`. Nothing to fix in this diff; confirm the spec item is obsolete.
3. **`sessions` index pulled into plain-background set** — `plainBackgroundRoutes` includes `sessions` (the `/sessions` index) in addition to `join-session`. Harmless (only sets a solid dark fill) but slightly beyond the named scope; confirm intended.

</details>

<details>
<summary>Details</summary>

### Shell as the single source of truth for width and sidebar

The width requirement is solved by moving `max-width:1200px; margin:0 auto; padding:0 32px 48px 32px` into `.page-container` inside `AppLayout`'s `<main>`, and deleting the per-view `mx-auto max-w-5xl/6xl/3xl/2xl` wrappers from Campaigns, Characters, Library, RulesLookup, AiSettings, AdventureForge, About, and HomeView. Because the container wraps the `<slot/>`, Dashboard is width-capped at 1200px like every other route, which is the intended consistency. Join Session deliberately keeps an inner `max-w-lg` for its narrow focused form, nested inside the 1200px shell — a reasonable exception, not a divergence.

One behavioral nuance worth recording: `<main>` carries `pt-6` while `.page-container`'s padding shorthand specifies a `0` top. The effective content area therefore starts 24px below the top rather than flush. It is applied uniformly through the shared shell so it does not reintroduce per-view divergence, and a gap above the hero reads fine, but it is a literal deviation from the `0 32px 48px 32px` the requirement spells out.

The sidebar width symptom ("Dashboard narrower than other pages") could not exist from a shared component, so the fix pins the expanded width: `w-60` (240px) → `.sidebar-expanded { width:220px }`, plus `shrink-0` so flex never squeezes it. Since `AppSidebar` renders for all routes, 220px is now guaranteed everywhere including Dashboard.

### Hero standardization and the documented Dashboard exemption

```
.app-hero            min-height:240px; position:relative; overflow:hidden
  .app-hero__overlay inset:auto 0 0 0; padding:24px   (bottom-left)
    .app-hero__title 24px / 700 / #fff
    .app-hero__subtitle 14px / #9ca3af
```

Campaigns, Characters, Library, RulesLookup, AiSettings, AdventureForge, JoinSession, and About all switch to this markup and delete their old 160–180px hero CSS, so title+subtitle sit bottom-left at a uniform 240px with the banner art still supplied by the per-element `v-bg-asset`. The subtitle contract is completed for the four views that previously had title-only heroes (Library, Adventure Forge, Join Session, About pull their descriptive line up into the overlay). The height is `min-height:240px` rather than a fixed `240px`; with the small overlay content it renders at 240px, so the "exactly 240px" intent holds in practice.

HomeView keeps its own `.hero` and right-aligned heading. The spec grants the Dashboard a height exemption and the implementer documented keeping its title placement to avoid overlapping the welcome-banner art — a defensible call that stays inside the "Dashboard hero may remain taller" allowance.

### Background removal moved to the shell

Rather than fixing each view's background locally, the five routes are enumerated in `plainBackgroundRoutes` and `AppLayout` applies `.page-plain` (`background-color:#0a0a1a`) to `<main>` when `route.name` matches. This covers the entire content viewport — container gutters and the top gap included — so the body `app-background.png` cannot bleed through, which addresses a prior-pass gutter finding. Every route name in the set (`rules-lookup`, `ai-settings`, `adventure-generate`, `join-session`, `about`, `sessions`) matches the router definitions.

Each of the five views drops its full-page `v-bg-asset` and the `bg-cover bg-fixed bg-center` / `background-attachment:fixed` root CSS, and the full-bleed `margin:-1rem` / `-m-6` hacks on Adventure Forge and Join Session are removed. A grep of the five view trees confirms the only remaining `background`/`bg-cover` references are scoped panel, card, field, and button art (ask-question-panel, citation-card, provider cards, forge-panel, join-panel, QR card, about license-card/about-panel) — all preserved with their gold borders. No residual full-page background rule survives, and Dashboard/Campaigns/Library/Characters were not touched in that respect.

Including `sessions` (the `/sessions` index) in the plain set is slightly broader than the named Join Session scope. It only paints a solid dark fill under whatever that view renders, so there is no visible regression, but it is worth a glance to confirm it was intended.

### New Campaign button reuses the dashboard recipe

```
.new-campaign-button  min-width:180px; min-height:44px; padding:0 1.25rem;
                      border:var(--border-gold); border-radius:md;
                      background-color:var(--color-purple) [fallback base];
v-bg-asset url=/assets/dashboard/new-campaign-button-art.png size=100% 100%
```

The CSS and `v-bg-asset` are identical to HomeView's dashboard button (verified side by side — same dimensions, same art, same `size:100% 100%`, same `+ New Campaign` label span). The `background-color:var(--color-purple)` is only a base behind the art — the dashboard button uses the exact same fallback — so this is not the "plain CSS purple button" the spec warned against; it matches the dashboard asset-based button and degrades gracefully if the art fails to load. The old `.btn-primary` purple button and its inline SVG were removed.

### Duplicate heading and About spacing

`CampaignsView` loses the `<h2>Your Campaigns</h2>` from its page-header row (now `justify-end`, holding only the button); the surviving occurrence is `CampaignList.vue`'s heading above the card grid, so the heading appears once above the list and the Overview / Recent Sessions panels have none above them.

About gets `.about-sections { gap:40px }`, `.license-card { padding:32px }`, and `.about-panel { padding:28px 32px }`, with the conflicting Tailwind `p-5` removed from each element so the scoped padding wins, plus the `.app-hero` + `about-hero-banner.png` bottom-left overlay. All five spacing/padding values match the requirement exactly, and the `p-5` removal is the kind of override that would otherwise silently defeat the new padding — worth confirming it was dropped on every affected element (it was, on all three panels and the license cards).

### Join Session: form now renders independent of the error

This is the most behaviorally meaningful fix. Before, the template branched `v-else-if="store.error && !store.currentSession"` (error panel) and the form sat behind `v-else-if="store.currentSession"` — so a failed lookup showed the error *instead of* the form, and a route with no pre-populated/loadable session (`currentSession` null, no error) rendered *nothing*. 

```
v-if   loading        -> spinner
v-else-if joinResult  -> joined/approval confirmation
v-else                -> PlayerJoinForm (+ QR card) ALWAYS
                         then v-if store.error && !currentSession -> not-found panel BELOW
```

The form moved to the unconditional `v-else` and takes `:session-name="store.currentSession?.name"` (optional, falls back to generic copy), so it renders whether or not the session loaded. The standalone "Session not found" panel renders only on a load failure, below the form and QR card.

The duplicate-error path is handled: `PlayerJoinForm` renders `store.error` in its own banner, so a load failure would otherwise show the message twice. The new `hideError` prop (`:hide-error="!!store.error && !store.currentSession"`) suppresses the form's internal banner on load failure so only the standalone panel shows it; on a join *submit* failure `currentSession` is set, `hideError` is false, the form shows its own error and the standalone panel stays hidden (`!store.currentSession` is false). The two error channels are mutually exclusive — no duplicate, and the form is never hidden.

### Test coverage

The implementer reports `npm run build` (incl. `vue-tsc`) and `npm run test` (304 tests) green, and documents that `npm run lint` cannot run because `eslint` is absent from devDependencies — a pre-existing project gap, not introduced here. Per instruction these suites were not re-run. The changes are CSS/template-level and the existing session tests exercise the Join form and dice tray, but note there is no test asserting the specific layout contracts changed here (sidebar 220px, hero 240px, form-renders-before-error), so those are verified by code reading rather than by a regression test.

</details>

<details>
<summary>File map</summary>

- `src/shared/components/AppLayout.vue` — adds `.page-container` wrapper + route-based `.page-plain` toggle on `<main>`.
- `src/shared/components/AppSidebar.vue` — expanded width pinned to 220px, `shrink-0`.
- `src/assets/main.css` — new shared primitives: `.page-container`, `.page-sections`, `.page-plain`, `.app-hero*`.
- `src/views/HomeView.vue` — removes `mx-auto max-w-6xl`; Dashboard hero kept taller.
- `src/features/campaigns/CampaignsView.vue` — app-hero, art New Campaign button, duplicate heading removed, page-sections.
- `src/features/characters/CharactersView.vue` — app-hero, old hero CSS removed.
- `src/features/library/LibraryView.vue` — app-hero + subtitle, old hero CSS removed.
- `src/features/ai/RulesLookupView.vue` — full-page bg removed, app-hero, nested page-sections, stray margins dropped.
- `src/features/ai/AiSettingsView.vue` — full-page bg + fixed attachment removed, app-hero, licenses link repinned.
- `src/features/adventure-generation/AdventureGenerateView.vue` — full-page bg + `-1rem` hack removed, app-hero.
- `src/features/about/AboutView.vue` — full-page bg removed, app-hero, 40px/32px/28px spacing.
- `src/features/sessions/JoinSessionView.vue` — form in `v-else` (always renders), error panel below, `hideError` wiring, bg/hacks removed.
- `src/features/sessions/PlayerJoinForm.vue` — adds `hideError` prop to suppress internal banner.

Full diff: `git -C d:\Development\aircane-tabletop\.worktrees\frontend-layout diff dev...frontend-layout-standardization`

</details>
