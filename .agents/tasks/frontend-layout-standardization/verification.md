# Verification — Frontend Layout Standardization

Worktree: `d:\Development\aircane-tabletop\.worktrees\frontend-layout`
Frontend root: `src/frontend/aircane-web`

## Commands run (in `src/frontend/aircane-web`)

| Command | Result |
| --- | --- |
| `npm run build` (`vue-tsc && vite build`) | **PASS** — 377 modules transformed, built in ~17s, no type errors. Only warnings are pre-existing `/*#__PURE__*/` annotation notes from `@microsoft/signalr` (unrelated to this change). |
| `npm run test` (`vitest --run`) | **PASS** — 43 test files, 304 tests, 0 failures. No snapshots needed updating. |
| `npm run lint` (`eslint . --ext .vue,.ts,.tsx --fix`) | **COULD NOT RUN (pre-existing gap)** — `eslint` is referenced by the `lint` script but is **not** declared in `package.json` devDependencies, so it is not installed (`node_modules/.bin/eslint` is absent). This is a pre-existing project configuration gap, not introduced by this change. Restoring a full ESLint toolchain means choosing + adding eslint, the Vue/TS parser, plugins, and a config — a dependency/tooling decision outside this layout task's scope. Build (which includes `vue-tsc` type-checking) and the full test suite are green; code matches existing `<script setup>` + scoped-style conventions. |

## Review iteration 2 — findings addressed

This iteration addressed the four findings in `review.json` (three MEDIUM confirmed, one MEDIUM disputed):

1. **Major-section spacing now 32px on every view (MEDIUM).** Switched the page-level section
   wrappers that were `space-y-6` (24px) to the shared `.page-sections` primitive (32px):
   `AiSettingsView` (outer wrapper), `AdventureGenerateView` (outer wrapper), `JoinSessionView`
   (outer wrapper), `CampaignsView` (inner content block), and `RulesLookupView`'s content column.
   Smaller spacing (`space-y-6`/`space-y-4`) is retained ONLY inside forms and cards (e.g. the
   Adventure Forge `<form>` between its fieldsets, the Rules result sub-sections), not between
   major page sections. Removed the now-redundant `mt-6`/`mt-8` margins on RulesLookup's error and
   result blocks so the `.page-sections` flex gap is the single spacing authority there.
2. **Hero overlay contract completed (MEDIUM).** Added an `.app-hero__subtitle` to the four views
   that had a title-only overlay: Library ("Import, index, and manage…"), Adventure Forge (moved
   the descriptive copy that sat below the hero up into the subtitle), Join Session ("Enter your
   display name and invite code…"), and About (moved its "local-first, AI-assisted…" line into the
   subtitle). Every non-dashboard hero now renders title + subtitle bottom-left. The **Dashboard
   title placement** is kept as a documented exception (see "Dashboard hero exception" below)
   rather than restructured, because moving it would alter the deliberate Dashboard hero
   composition (title aligned to the right of the welcome-banner art) beyond the task's stated
   Dashboard exemption.
3. **Plain-page background now covers the whole content viewport (MEDIUM).** The `.page-plain`
   fill (`#0a0a1a`) is now applied by `AppLayout.vue` to the shell `<main>` (via a route-name
   check for `rules-lookup`, `ai-settings`, `adventure-generate`, `join-session`, `about`, and
   `sessions`), so the solid colour covers the full content viewport — including the
   `.page-container` 32px side / 48px bottom gutters and the `pt-6` top gap — instead of only the
   routed view root. The per-view-root `.page-plain` classes were removed (the shell is now the
   single authority); no per-view full-bleed hacks were reintroduced.
4. **Join Session lookup-error duplication fixed (MEDIUM, disputed).** Verified against source:
   `PlayerJoinForm.vue` **does** render `store.error` in an internal banner (Review B was correct;
   Review A's grep was stale). On a failed session lookup, that internal banner AND the standalone
   "Session not found" panel both showed the same message. Fix: added an optional `hideError` prop
   to `PlayerJoinForm`; `JoinSessionView` passes `:hide-error="!!store.error && !store.currentSession"`
   so on a load failure the form suppresses its internal banner and ONLY the standalone panel
   (below the form + QR) shows the error. When a session loaded successfully and a join *submit*
   fails, `currentSession` is set → `hideError` is false → the form shows its own submit error and
   the standalone panel stays hidden. The two error channels are now distinct and never duplicate.

### Dashboard hero exception (documented, per review action)

Requirement #1 overlays title+subtitle bottom-left; the task spec grants the Dashboard an explicit
exemption ("The Dashboard hero may remain taller"). The Dashboard hero (`HomeView.vue`) is a
bespoke two-part composition: a `.hero-welcome` banner-art panel on the left ~55% and the heading
overlaid on the right portion so it reads clear of that art. Forcing the heading to the shared
bottom-left overlay would overlap the welcome-banner art and change the intended Dashboard visual —
a user-visible redesign beyond the task's layout/styling scope and beyond the stated height-only
exemption. Per the reviewer's offered alternative ("obtain an explicit documented exception for its
title placement"), the Dashboard title placement is kept as-is and documented here as an
intentional exception. All non-dashboard views conform to the bottom-left title+subtitle contract.

## Design approach

- Shared layout rules are centralized in two places (single source of truth):
  - **Structural shell** on `AppLayout.vue`'s `<main>`: a `.page-container` wrapper
    (`max-width:1200px; margin:0 auto; padding:0 32px 48px 32px`) wraps the `<slot/>`,
    so every route's content is centred and width-capped. `<main>` keeps `pt-6` for the
    top scroll gap; bottom padding comes from `.page-container`.
  - **Reusable classes** in `src/assets/main.css` under `@layer components`:
    `.page-sections` (flex column, `gap:32px`), `.app-hero` (`min-height:240px`),
    `.app-hero__overlay` (absolute bottom-left, `padding:24px`),
    `.app-hero__title` (white, 24px, 700), `.app-hero__subtitle` (grey `#9ca3af`, 14px),
    and `.page-plain` (opaque `background-color:#0a0a1a`).
- No slot/prop hero refactor was done (would touch every view's script/template and exceed
  the "layout/styling only" constraint). Each view keeps its own hero markup but now consumes
  the shared classes, so there is no per-view hero divergence.
- The `body` background (`app-background.png`) in `main.css` was left untouched (Dashboard/root
  background). Views that drop their full-page art get `.page-plain` so the body image cannot
  bleed through.

## Checklist (reasoned through computed CSS + DOM, no live browser available)

- [x] **Sidebar exactly 220px on every route incl. Dashboard.**
  `AppSidebar.vue` is a single shared component rendered by `AppLayout.vue` for all routes.
  The expanded `<aside>` now uses `.sidebar-expanded { width: 220px; }` (replacing Tailwind
  `w-60` = 240px); collapsed still uses `w-16`. Also added `shrink-0` so flex never squeezes it.
  Width is identical on `/` and all other routes because the component and its CSS are shared.
- [x] **Content centred, max-width 1200px on every route.**
  `AppLayout.vue` `<main>` wraps `<slot/>` in `.page-container` (max-width 1200px, `margin:0 auto`,
  `padding:0 32px 48px 32px`). All in-scope views had their per-view `mx-auto max-w-*` removed
  (Campaigns, Characters, Library, RulesLookup, AiSettings, AdventureForge, About) so the shell
  width wins. HomeView's `mx-auto max-w-6xl` was removed too. (JoinSession keeps an inner
  `max-w-lg` wrapper by design — a deliberately narrow focused form page, nested inside the
  centred 1200px shell.)
- [x] **Non-dashboard heroes exactly 240px tall; title bottom-left (white 24px bold, grey 14px).**
  All non-dashboard views use `.app-hero` (`min-height:240px`) with `.app-hero__overlay`
  (absolute `inset:auto 0 0 0`, `padding:24px`), `.app-hero__title` (24px/700/#fff) and
  `.app-hero__subtitle` (14px/#9ca3af). Applied to Campaigns, Characters, Library, RulesLookup,
  AiSettings, AdventureForge, JoinSession, About. The hero's own banner art is preserved via the
  existing `v-bg-asset` on the hero element.
- [x] **Dashboard hero may stay taller.** `HomeView.vue` keeps its own `.hero` (min-height 200px)
  and `.hero-heading` markup — not forced to 240px.
- [x] **No full-page background image on Rules Lookup, AI Settings, Adventure Forge, Join Session, About.**
  Each view's outer root had its full-page `v-bg-asset` removed. The opaque `#0a0a1a` fill
  (`.page-plain`) is now applied by `AppLayout.vue` to the shell `<main>` for these routes (by
  route name), so it covers the ENTIRE content viewport — the `.page-container` gutters and the
  `pt-6` top gap included — not just the routed view root (addresses the review's gutter finding).
  Removed the related view-root CSS: `.ai-settings-page`/`.forge-page` `background-*`/
  `background-attachment:fixed`, the `.forge-page { margin:-1rem }` and `.join-page { -m-6 }`
  full-bleed hacks, and the `bg-cover bg-center`/`bg-fixed` root classes on RulesLookup/About/Forge/
  Join. The opaque fill guarantees the body `app-background.png` cannot bleed through anywhere in
  the content area.
- [x] **Hero banners + panels still render after bg removal.** Hero banner art is on its own
  `.app-hero` element (scoped), untouched. Inner panels/cards keep their gold borders
  (`var(--border-gold)`) and panel-art `v-bg-asset` (ask-question-panel, citation-card,
  provider cards, forge panel, join panel/QR card, about license-card/about-panel) — none were
  removed.
- [x] **Dashboard / Campaigns / Library / Characters backgrounds unchanged.**
  No full-page background was ever removed from these. `main.css` body `app-background.png` is
  intact (Dashboard). Campaigns/Library/Characters only had their heroes restyled to the shared
  classes; they never had a full-page image.
- [x] **New Campaign button on `/campaigns` uses `new-campaign-button-art.png`.**
  `CampaignsView.vue` button now carries
  `v-bg-asset="{ url: '/assets/dashboard/new-campaign-button-art.png', size: '100% 100%' }"`
  and a `.new-campaign-button` class copied from HomeView (same dimensions: `min-width:180px;
  min-height:44px; border:var(--border-gold); border-radius:var(--border-radius-md)`), with
  "+ New Campaign" as overlaid Vue text (`.new-campaign-label`). The old `.btn-primary` purple
  button was removed.
- [x] **"Your Campaigns" appears exactly once.** Removed the `<h2>Your Campaigns</h2>` from the
  `CampaignsView.vue` page-header row (kept the New Campaign button in that row). The single
  remaining heading is in `CampaignList.vue` (above the card list). Verified by grep: the only
  template occurrence of "Your Campaigns" is in `CampaignList.vue`.
- [x] **About hero + increased spacing/padding.**
  About hero now uses `.app-hero` with `about-hero-banner.png` and the title overlaid bottom-left.
  `.about-sections` gives a 40px gap between every major section (cards, dark panels, scroll
  ornaments). `.license-card { padding: 32px }` (all sides). `.about-panel { padding: 28px 32px }`
  (28px vertical / 32px horizontal) for open-content-attributions, content-licensing-declaration,
  and application-license panels. The conflicting Tailwind `p-5` was removed from those elements
  so the scoped padding is authoritative.
- [x] **Join Session shows the form before the error panel; no duplicate error.**
  Template: `v-if loading` → `v-else-if joinResult` (joined confirmation) → `v-else` block that
  ALWAYS renders `PlayerJoinForm` + QR card first. `:session-name` is `store.currentSession?.name`
  (optional; the form falls back to generic copy when undefined), so the form renders even when the
  session failed to load / no session is pre-populated. The "Session not found" panel renders BELOW
  the form, guarded by `v-if="store.error && !store.currentSession"` — only on a load failure,
  after the QR card. **Duplicate-error fix:** `PlayerJoinForm` renders `store.error` in its own
  banner, so a load failure previously showed the error both inside the form and in the standalone
  panel. The form now takes an optional `hideError` prop and `JoinSessionView` passes
  `:hide-error="!!store.error && !store.currentSession"`, suppressing the form's internal banner on
  a load failure so ONLY the standalone panel shows it. A *join submit* failure (session loaded →
  `currentSession` set → `hideError` false) still shows inside the form, with the standalone panel
  hidden. The two error paths are distinct; no duplicate.
- [x] **Section spacing is 32px between major sections on EVERY view.** Campaigns, Characters,
  Library already used `.page-sections` (32px). This iteration switched the remaining page-level
  `space-y-6` (24px) wrappers to `.page-sections` on AI Settings, Adventure Forge, Join Session,
  the Campaigns inner content block, and the Rules Lookup content column. Hero-to-first-section and
  section-to-section gaps are now a uniform 32px across all non-dashboard views. Smaller spacing is
  retained only inside forms/cards (intra-section), not between major sections.
- [x] **Navigation between views stays visually consistent.** Shared sidebar (220px), shared
  1200px centred container, shared 240px hero + bottom-left title + subtitle, shared 32px section
  rhythm, and the shell-level `#0a0a1a` plain fill for the five background-removed routes are all
  driven from `AppLayout.vue` + `main.css`, so every in-scope route composes the identical shell.

## Notes

- The "Dashboard sidebar narrower than other pages" report could not be reproduced from code
  (the sidebar is one shared component). Pinning it to a fixed 220px and adding `shrink-0`
  guarantees identical width on every route, which addresses the reported symptom.
- Secondary/detail routes not in the task's main-view list (CharacterFieldReview,
  HostSessionView, PlayerSessionView, AdventureReviewView) still use their own `mx-auto max-w-*`
  wrappers. They are out of scope for this standardization pass and were intentionally left
  untouched.
