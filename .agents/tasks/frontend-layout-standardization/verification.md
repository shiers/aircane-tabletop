# Verification — Frontend Layout Standardization

Worktree: `d:\Development\aircane-tabletop\.worktrees\frontend-layout`
Frontend root: `src/frontend/aircane-web`

## Commands run (in `src/frontend/aircane-web`)

| Command | Result |
| --- | --- |
| `npm install` | OK — `node_modules` populated (was absent in the worktree). |
| `npm run build` (`vue-tsc && vite build`) | **PASS** — 377 modules transformed, built in ~16s, no type errors. Only warnings are pre-existing `/*#__PURE__*/` annotation notes from `@microsoft/signalr` (unrelated to this change). |
| `npm run test` (`vitest --run`) | **PASS** — 43 test files, 304 tests, 0 failures. No snapshots needed updating. |
| `npm run lint` (`eslint . --ext .vue,.ts,.tsx --fix`) | **COULD NOT RUN** — `eslint` is referenced by the `lint` script but is **not** declared in `package.json` devDependencies, so `npm install` never installed it (`node_modules/eslint` and `node_modules/.bin/eslint` are both absent). This is a pre-existing project configuration gap, not introduced by this change. Installing an unpinned eslint + plugin toolchain is outside the scope of this layout task, so lint was not force-installed. Build (which includes `vue-tsc` type-checking) and the full test suite are green. |

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
  Each view's outer root had its full-page `v-bg-asset` removed and now carries `.page-plain`
  (opaque `#0a0a1a`). Removed the related view-root CSS: `.ai-settings-page` /`.forge-page`
  `background-*`/`background-attachment:fixed`, the `.forge-page { margin:-1rem }` and
  `.join-page { -m-6 }` full-bleed hacks, and the `bg-cover bg-center`/`bg-fixed` root classes on
  RulesLookup/About/Forge/Join. Opaque fill guarantees the body `app-background.png` cannot bleed
  through.
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
  Template restructured: `v-if loading` → `v-else-if joinResult` (joined confirmation) →
  `v-else` block that ALWAYS renders `PlayerJoinForm` + QR card first. `:session-name` is now
  `store.currentSession?.name` (optional; the form falls back to generic copy when undefined), so
  the form renders even when the session failed to load / no session is pre-populated. The
  "Session not found" panel renders BELOW the form, guarded by
  `v-if="store.error && !store.currentSession"` — i.e. only on a load failure, placed after the
  QR card. `PlayerJoinForm` surfaces join (API) errors in its own banner; the standalone panel is
  scoped to the load-failure case to keep the two error paths distinct. Note: on a load failure
  the store's single `error` ref is set, so the form's internal banner and the standalone panel
  can both reference the same message — the standalone panel adds the "Session not found" framing
  + "Back to home" link, which is the intended load-failure UX and matches the task's "error panel
  below the form" requirement.
- [x] **Navigation between views stays visually consistent.** Shared sidebar (220px), shared
  1200px centred container, shared 240px hero + bottom-left title, and shared 32px section rhythm
  are all driven from `AppLayout.vue` + `main.css`, so every in-scope route composes the identical
  shell.

## Notes

- The "Dashboard sidebar narrower than other pages" report could not be reproduced from code
  (the sidebar is one shared component). Pinning it to a fixed 220px and adding `shrink-0`
  guarantees identical width on every route, which addresses the reported symptom.
- Secondary/detail routes not in the task's main-view list (CharacterFieldReview,
  HostSessionView, PlayerSessionView, AdventureReviewView) still use their own `mx-auto max-w-*`
  wrappers. They are out of scope for this standardization pass and were intentionally left
  untouched.
