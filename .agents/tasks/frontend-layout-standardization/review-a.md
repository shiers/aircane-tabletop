# Frontend layout standardization: shared shell, background removal, Dashboard/Join fixes (pass 3)

This change centralizes page layout into `AppLayout.vue` plus shared `@layer components` primitives in `main.css` (`.page-container`, `.page-sections`, `.app-hero*`, `.page-plain`), then retrofits every in-scope view to consume them instead of its own bespoke hero/width/background CSS. It removes full-page background art from five named views (replacing it with a solid `#0a0a1a` fill applied at the shell), swaps the Campaigns "New Campaign" button to the dashboard art asset, de-duplicates the "Your Campaigns" heading, adds About page breathing room, and reworks Join Session so the join form always renders independent of the lookup-error panel. This is a later-pass review: the two MEDIUM findings from the prior combined pass (`review.json`) — the Dashboard hero overlay position and the no-ID / stale-state Join flow — have both been addressed in commit `0a9ec8b`, and this pass verifies the fixes against the latest source.

Watch for: nothing blocking. Three carry-over LOW observations remain — a uniform 24px top gap (`pt-6` on `<main>`) added on top of the spec's `0` top padding (confirmed); the "Dice Roller" view named in requirement #1 has no standalone route to standardize (confirmed); and the `sessions` index is bundled into the plain-background set alongside `join-session` (confirmed, harmless). One new LOW: the AI Settings hero subtitle dropped its "API keys are stored server-side only" sentence (confirmed, cosmetic copy change).

**Verdict**: APPROVED

## High-level view

The two previously-blocking behaviors are now correct. The Dashboard hero no longer uses the center-right `.hero-heading` (that markup and its `top:50%; left:58%` CSS are deleted); it now renders its title and subtitle through the shared `.app-hero__overlay` / `__title` / `__subtitle` classes, so the title sits bottom-left with 24px padding like every other view while the hero keeps its taller allowed height. The `.hero` element retains `position:relative`, so the absolutely-positioned overlay anchors to it correctly.

Join Session is rebuilt around dedicated local lookup state (`lookupLoading`, `lookupError`) that is independent of the shared Pinia store. The `onMounted` lookup returns early when there is no route `sessionId`, so the `/sessions` index renders the form immediately instead of firing `fetchSession(undefined)` and hanging on the loading panel. The form now lives in an unconditional `v-else` branch and always renders first; the standalone "Session not found" panel renders below it only when `lookupError` is set, and `PlayerJoinForm`'s `hideError` prop keys off `lookupError` so a load failure shows the error only once (in the standalone panel) while a join-submit failure shows it only inside the form. The two error channels can no longer be confused by stale session data.

The rest of the shell is unchanged from the prior pass and still satisfies the spec: `AppLayout`'s `<main>` wraps `<slot/>` in a `.page-container` (max-width 1200px, centered, `0 32px 48px 32px`), per-view `mx-auto max-w-*` wrappers are removed everywhere, the shared `AppSidebar` is pinned to `220px` when expanded with `shrink-0`, all eight non-dashboard heroes use `.app-hero` (min-height 240px), the five de-backgrounded routes toggle `.page-plain` on `<main>`, the Campaigns button reuses the dashboard art recipe, "Your Campaigns" appears once, and About has 40px/32px/28px spacing.

Spot-checks confirm the branch has no residual full-page `bg-cover`/`bg-fixed`/`background-attachment` on the five view roots and exactly one "Your Campaigns" occurrence (in `CampaignList.vue`).

<details>
<summary>Issues (4)</summary>

1. **Top padding deviation (LOW)** — `<main>` keeps `pt-6` (24px) above the `.page-container` whose spec'd top padding is `0`, so content sits 24px lower than the literal `0 32px 48px 32px`. Uniform across all routes; leave as-is or drop `pt-6` for a strict spec match.
2. **Dice Roller has no view to standardize (LOW)** — requirement #1 lists "Dice Roller" as a main view, but there is no `/dice` route or `DiceRollerView`; the nav tile links to `/sessions`. Nothing to fix in this diff; confirm the spec item is obsolete.
3. **`sessions` index in the plain-background set (LOW)** — `plainBackgroundRoutes` includes `sessions` in addition to `join-session`. Harmless (only paints a solid dark fill under `JoinSessionView`, which `/sessions` also renders) but slightly beyond the named scope; confirm intended.
4. **AI Settings hero subtitle trimmed (LOW)** — the hero subtitle dropped the "API keys are stored server-side only and never sent to the browser" sentence when moving into the shared overlay. Cosmetic copy change, not a layout issue; restore the sentence elsewhere on the page if that reassurance is still wanted.

</details>

<details>
<summary>Details</summary>

### Dashboard hero overlay — prior MEDIUM now resolved

`HomeView.vue` previously placed the title via a bespoke `.hero-heading` at `top:50%; left:58%; right:5%` with a vertical transform, which the prior pass flagged because requirement #1 exempts the Dashboard only from the 240px height, not from the bottom-left/24px title treatment. That markup and its CSS block are now deleted; the title and subtitle render through the shared `.app-hero__overlay` / `.app-hero__title` / `.app-hero__subtitle` classes. The surrounding `.hero` section keeps `position:relative` (and its own taller `min-height`), so the absolutely-positioned overlay (`inset:auto 0 0 0; padding:24px`) anchors bottom-left within the Dashboard hero exactly as on every other view. The height exemption is preserved; the geometry now matches. Finding cleared.

### Join Session no-ID / stale-state flow — prior MEDIUM now resolved

The flow was reworked away from the shared store's `loading`/`error`/`currentSession` triad:

```
onMounted: if (!sessionId) return            // /sessions index: no lookup, form renders
           else lookupLoading=true; try fetchSession catch lookupError=... finally lookupLoading=false

template:  v-if   lookupLoading   -> spinner
           v-else-if joinResult   -> joined/approval confirmation
           v-else                 -> PlayerJoinForm (+ QR card) ALWAYS
                                     then v-if lookupError -> "Session not found" panel BELOW
```

`sessionId` now defaults to `''`, and the early return means the `/sessions` index never fires `fetchSession(undefined)` or shows the loading panel — the form renders immediately, satisfying requirement #6. Lookup failure is tracked in the local `lookupError` ref rather than `store.error && !store.currentSession`, so stale `currentSession` from a prior flow can no longer misroute the error. `PlayerJoinForm` receives `:hide-error="!!lookupError"` and `:session-name="lookupError ? undefined : store.currentSession?.name"`, so on a load failure only the standalone panel shows the message (with generic form copy), while a join-*submit* failure (no `lookupError`) shows `store.error` inside the form and keeps the standalone panel hidden. The two channels are now driven by distinct state and are mutually exclusive. Finding cleared.

### Shell, sidebar, and hero contract (carried over, re-verified)

`AppLayout.vue`'s `<main>` wraps `<slot/>` in `.page-container` (`max-width:1200px; margin:0 auto; padding:0 32px 48px 32px`), and the per-view `mx-auto max-w-5xl/6xl/3xl/2xl` wrappers were removed from Campaigns, Characters, Library, RulesLookup, AiSettings, AdventureForge, About, and HomeView, so width/centering is decided in one place and applies to the Dashboard too. Join Session keeps a deliberately narrow inner `max-w-lg` nested inside the 1200px shell. `AppSidebar` replaces `w-60` (240px) with scoped `.sidebar-expanded { width:220px }` plus `shrink-0`, so the single shared component is 220px on every route. All eight non-dashboard views adopt `.app-hero` (min-height 240px) with the bottom-left overlay and delete their old 160–180px hero CSS. The height is `min-height:240px` rather than a fixed value, but the overlay is absolutely positioned (out of flow), so the hero renders at 240px in practice.

One literal deviation persists: `<main>` carries `pt-6` while `.page-container`'s shorthand sets a `0` top. Content therefore starts 24px below the top. It is applied uniformly through the shared shell so it does not reintroduce per-view divergence, but it is not the literal `0` top padding the requirement spells out.

### Background removal at the shell (carried over, re-verified)

The five routes are enumerated in `plainBackgroundRoutes` and `AppLayout` toggles `.page-plain` (`#0a0a1a`) on `<main>` when `route.name` matches, so the solid fill covers the full content viewport including the container gutters and the top gap. Each of the five views dropped its full-page `v-bg-asset` and `bg-cover`/`bg-fixed`/`background-attachment:fixed` root CSS, and the `margin:-1rem` / `-m-6` full-bleed hacks on Adventure Forge and Join Session are gone. A grep across the worktree's five view trees shows no residual full-page background rule survives — the only `bg-cover`/`background` references left are scoped panel/card/field/button art (ask-question-panel, citation cards, provider cards, forge panel, join panel + QR card, about license-card/about-panel), all keeping their gold borders. Dashboard/Campaigns/Library/Characters were not touched in this respect; the body `app-background.png` in `main.css` is intact.

Including `sessions` (the `/sessions` index) in the plain set is marginally broader than the named Join Session scope, but since that route renders `JoinSessionView` anyway it only paints the same solid fill — no visible regression.

### New Campaign button, duplicate heading, About spacing (carried over, re-verified)

The Campaigns button uses `v-bg-asset="{ url:'/assets/dashboard/new-campaign-button-art.png', size:'100% 100%' }"` with a `.new-campaign-button` recipe (180x44, gold border, `var(--color-purple)` as the art-independent base) matching HomeView's dashboard button; the old `.btn-primary` purple button with its inline SVG is removed. Because the art covers via `100% 100%` and purple is only a fallback base, this is the dashboard asset button, not the "plain CSS purple button" the spec warned against. The `.new-campaign-button`/`.new-campaign-label` CSS is duplicated verbatim between HomeView and CampaignsView — a minor maintainability smell (a shared class would prevent drift) but not a functional issue and below the MEDIUM bar.

`CampaignsView` lost its `<h2>Your Campaigns</h2>` from the page-header row (now `justify-end`, holding only the button); the single surviving occurrence is `CampaignList.vue`'s heading above the card grid. Grep confirms exactly one occurrence in the branch. About gets `.about-sections { gap:40px }`, `.license-card { padding:32px }`, and `.about-panel { padding:28px 32px }`, with the conflicting Tailwind `p-5` removed from each element so the scoped padding wins, plus the `.app-hero` + `about-hero-banner.png` bottom-left overlay. All five spacing/padding values match the requirement.

### Test coverage

The implementer reports `npm run build` (incl. `vue-tsc`) and `npm run test` (304 tests) green, and documents that `npm run lint` cannot run because `eslint` is absent from devDependencies — a pre-existing project gap, not introduced here. Per instruction these suites were not re-run. The changes are CSS/template-level, but there is no view-level test asserting the specific contracts reworked here (sidebar 220px, hero 240px, `/sessions` renders the form with no ID, lookup-vs-submit error channel split), so those are verified by code reading rather than regression tests. Worth adding a focused `JoinSessionView` test now that the error-channel logic has moved into the view, but not a blocker for this layout pass.

</details>

<details>
<summary>File map</summary>

- `src/shared/components/AppLayout.vue` — `.page-container` wrapper + route-based `.page-plain` toggle on `<main>`.
- `src/shared/components/AppSidebar.vue` — expanded width pinned to 220px, `shrink-0`.
- `src/assets/main.css` — shared primitives: `.page-container`, `.page-sections`, `.page-plain`, `.app-hero*`.
- `src/views/HomeView.vue` — removes `mx-auto max-w-6xl`; Dashboard title moved to shared bottom-left overlay, `.hero-heading` deleted.
- `src/features/campaigns/CampaignsView.vue` — app-hero, art New Campaign button, duplicate heading removed, page-sections.
- `src/features/characters/CharactersView.vue` — app-hero, old hero CSS removed.
- `src/features/library/LibraryView.vue` — app-hero + subtitle, old hero CSS removed.
- `src/features/ai/RulesLookupView.vue` — full-page bg removed, app-hero, nested page-sections, stray margins dropped.
- `src/features/ai/AiSettingsView.vue` — full-page bg + fixed attachment removed, app-hero, licenses link repinned, subtitle trimmed.
- `src/features/adventure-generation/AdventureGenerateView.vue` — full-page bg + `-1rem` hack removed, app-hero.
- `src/features/about/AboutView.vue` — full-page bg removed, app-hero, 40px/32px/28px spacing.
- `src/features/sessions/JoinSessionView.vue` — local lookup state, form in `v-else` (always renders), error panel below, no-ID early return.
- `src/features/sessions/PlayerJoinForm.vue` — `hideError` prop suppresses internal banner on lookup failure.

Full diff: `git -C d:\Development\aircane-tabletop\.worktrees\frontend-layout diff dev...frontend-layout-standardization`

</details>
