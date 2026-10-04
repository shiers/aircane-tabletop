# Implementation Plan — Frontend Layout Standardization

All paths below are inside the worktree:
`d:\Development\aircane-tabletop\.worktrees\frontend-layout`
Frontend root (run all npm commands here):
`d:\Development\aircane-tabletop\.worktrees\frontend-layout\src\frontend\aircane-web`

Git ops must target the worktree, e.g.
`git -C d:\Development\aircane-tabletop\.worktrees\frontend-layout status`.

## Build / test facts (verified)
- Package manager: npm. Node v22, npm 10 confirmed available.
- `node_modules` is NOT present in the worktree frontend root — the coder MUST run
  `npm install` in `src/frontend/aircane-web` once before building (step 0).
- Build: `npm run build` (= `vue-tsc && vite build`, so typecheck is part of build).
- Tests: `npm run test` (= `vitest --run`).
- Lint: `npm run lint` (eslint with `--fix`).
- Working tree is clean on branch `frontend-layout-standardization`.

## Design decisions (made during planning — rationale kept brief)

1. **How views compose the shared layout.** `App.vue` renders `<AppLayout><RouterView/></AppLayout>`.
   `AppLayout.vue` has no hero/title slot or props; each view renders its own `.hero` markup
   with its own per-view `<style scoped>` (differing `min-height`, heading placement, etc.).
   A full slot/prop-based hero refactor would touch every view's script and template and exceeds
   the "keep changes scoped to layout/styling" constraint and risks regressions.
   **Decision:** centralize the shared RULES in two shared places and have every view consume them:
   - Structural shell rules (content max-width 1200px, centering, horizontal/bottom padding,
     32px section gap) go on `AppLayout.vue`'s `<main>`.
   - Reusable hero + title/subtitle + page-container classes go in `src/assets/main.css`
     under `@layer components` (`.app-hero`, `.app-hero__overlay`, `.app-hero__title`,
     `.app-hero__subtitle`). Every non-dashboard view replaces its bespoke `.hero`/`.hero-heading`
     (and local hero CSS) with these shared classes.
   This makes AppLayout + main.css the single source of truth and removes per-view divergence
   without a prop refactor. The Dashboard keeps its own taller hero markup (allowed to stay taller).

2. **Sidebar width = exactly 220px.** `AppSidebar.vue` currently uses Tailwind `w-60` (240px)
   when expanded and `w-16` (collapsed). 240px ≠ 220px and the task requires 220px on ALL routes
   including Dashboard. **Decision:** replace the expanded `w-60` with a fixed `220px` width
   (via an inline style or a dedicated CSS class on the `<aside>`, keeping the collapsed `w-16`
   behavior). The "Dashboard shows a narrower sidebar" report is almost certainly a visual artifact
   of the Dashboard's wider content pushing layout; the sidebar component is shared, so pinning it
   to 220px fixes width consistency everywhere. (See step 9 — verify at runtime.)

3. **Removing full-page backgrounds while body keeps `app-background.png`.** `src/assets/main.css`
   applies `app-background.png` to `body` globally (this is the Dashboard/root background and MUST
   stay). Simply deleting a view's `v-bg-asset` directive would let the body image show THROUGH the
   view, which is NOT the requested "solid #0a0a1a". **Decision:** on each targeted view's root
   element, remove the image `v-bg-asset` directive AND set an explicit opaque
   `background-color: #0a0a1a` (via a `.page-plain` shared class or inline style) so the solid dark
   fully covers the body image. Do NOT touch `main.css` body rules (Dashboard keeps its background).

4. **Hero height = 240px for non-dashboard views; Dashboard may stay taller.** The shared
   `.app-hero` class sets `min-height: 240px`. Dashboard's hero markup is left as-is.

5. **Title/subtitle overlay.** Shared `.app-hero__overlay` positions content bottom-left with 24px
   padding; `.app-hero__title` = white, 24px, bold; `.app-hero__subtitle` = grey (#9ca3af-ish /
   existing `text-gray-400`), 14px. Views that today have only a title (Library, Rules Lookup,
   About, Join Session, Adventure Forge) get the title in the overlay; a subtitle is added where the
   view already had descriptive subtext, otherwise omitted (title-only overlay is fine).

## Steps

- [ ] 0. Install dependencies in the frontend root so build/test can run.
      Files: none (installs `node_modules`).
      Verify: run `npm install` in `src/frontend/aircane-web`; then `npm run build` succeeds
      (establishes a green baseline before any edits).

- [ ] 1. Add shared layout CSS to `src/assets/main.css` under `@layer components`:
      `.page-container` (`max-width:1200px; margin:0 auto; padding:0 32px 48px 32px;`),
      `.page-sections` (vertical flow with `gap:32px` — e.g. `display:flex;flex-direction:column;gap:32px;`
      or document that views should use it as the sections wrapper),
      `.app-hero` (`position:relative; width:100%; min-height:240px; overflow:hidden;
      border-radius:var(--border-radius-md); background-size:cover; background-position:center;`),
      `.app-hero__overlay` (`position:absolute; inset:auto 0 0 0; display:flex; flex-direction:column;
      padding:24px; text-shadow:0 2px 6px rgba(0,0,0,0.7);`),
      `.app-hero__title` (`font-size:24px; font-weight:700; color:#fff; line-height:1.2;`),
      `.app-hero__subtitle` (`font-size:14px; color:#9ca3af; margin-top:4px;`),
      `.page-plain` (`background-color:#0a0a1a;` opaque, covers body image).
      Do NOT modify the existing `body` rules (Dashboard background stays).
      Files: `src/frontend/aircane-web/src/assets/main.css`
      Verify: `npm run build` succeeds (CSS compiles, no Tailwind/PostCSS errors).

- [ ] 2. Centralize structural shell rules on the layout `<main>` so content is consistently
      constrained on every route. In `AppLayout.vue`, wrap the `<slot />` so the content area applies
      `max-width:1200px; margin:0 auto; padding:0 32px 48px 32px;` (keep the existing scroll container).
      Prefer adding a wrapper `<div class="page-container">` around `<slot />` inside `<main>` and
      drop/adjust the current `p-6` so padding is not doubled. Document that per-view roots should
      NOT re-impose their own `max-w-*`/`mx-auto` (those are removed in later steps).
      Files: `src/frontend/aircane-web/src/shared/components/AppLayout.vue`
      Verify: `npm run build` succeeds. Runtime (step 9): every route's content is centered and
      capped at 1200px.

- [ ] 3. Pin the sidebar to exactly 220px when expanded (keep `w-16` collapsed). In `AppSidebar.vue`
      replace the expanded `w-60` class with a fixed 220px width (add a `.sidebar-expanded { width:220px; }`
      scoped class applied when `!collapsed`, or bind an inline `style="width:220px"` when expanded).
      Ensure the width is identical regardless of route (component is shared, so this applies to
      Dashboard too).
      Files: `src/frontend/aircane-web/src/shared/components/AppSidebar.vue`
      Verify: `npm run build` succeeds. Runtime (step 9): sidebar measures 220px on `/` and on every
      other route; it does not change width when navigating Dashboard ↔ other pages.

- [ ] 4. Standardize the Campaigns view to the shared layout + fix the duplicate heading + New Campaign
      button art.
      - Replace the per-view `.hero`/`.hero-heading` markup and its scoped CSS with the shared
        `.app-hero` + `.app-hero__overlay`/`__title`/`__subtitle` classes (keep the
        `v-bg-asset` campaigns hero background on the `.app-hero` element). Title "Campaigns",
        subtitle "Create, manage, and jump back into your adventures."
      - Remove the per-view root `mx-auto max-w-5xl` (shell now centralizes width); keep the
        `space-y`/sections wrapper or switch to `.page-sections` for the 32px gap.
      - **Duplicate "Your Campaigns":** the heading appears in `CampaignsView.vue` (page-header row,
        ABOVE the Overview/Recent Sessions panels) and in `CampaignList.vue` (ABOVE the card list).
        The task wants it to remain ONLY above the campaign card list. **Remove the `<h2>Your Campaigns</h2>`
        in `CampaignsView.vue`** (the page-header row) but KEEP the `+ New Campaign` button that sits in
        that same row (preserve the row/button, drop only the heading). Leave the `CampaignList.vue`
        heading intact.
      - **New Campaign button art:** the button in `CampaignsView.vue` currently uses `.btn-primary`
        (purple). Change it to use the dashboard art: `background-image: url('/assets/dashboard/new-campaign-button-art.png'); background-size:100% 100%;` and match the dashboard button dimensions
        (see HomeView `.new-campaign-button`: `min-width:180px; min-height:44px; border:var(--border-gold);
        border-radius:var(--border-radius-md);`). Keep the "+ New Campaign" label as overlaid Vue text
        (reuse HomeView's `.new-campaign-button`/`.new-campaign-label` pattern, e.g. via `v-bg-asset`
        with `size:'100% 100%'`). Do NOT leave the plain purple `.btn-primary`.
      Files: `src/frontend/aircane-web/src/features/campaigns/CampaignsView.vue`
      (and confirm `CampaignList.vue` keeps its single heading — no edit expected there).
      Verify: `npm run build` succeeds; `npm run test` passes (FilterPill/StatusBadge tests unaffected).
      Grep check for regression only: `rg "Your Campaigns" src/features/campaigns` returns exactly ONE
      template occurrence (in `CampaignList.vue`). Runtime (step 9): one heading, art button renders.

- [ ] 5. Standardize the Characters and Library views to the shared layout (no background removal —
      these keep their clean look). Apply the shared `.app-hero`/overlay/title/subtitle classes in
      place of each view's bespoke `.hero`/`.library-hero` markup + scoped CSS, keeping each view's
      existing hero `v-bg-asset` background. Remove per-view root `mx-auto max-w-5xl` and rely on the
      shell container; use `.page-sections` (or keep `space-y` adjusted to 32px) for section spacing.
      - Characters: title "Characters", subtitle "Build, import, and manage your party."
      - Library: title "Document Library" (currently in `.library-hero-title`); move it into the
        overlay. Library hero currently uses `align-items:flex-end; padding:24px 28px` — the shared
        overlay replaces that.
      Files:
      `src/frontend/aircane-web/src/features/characters/CharactersView.vue`,
      `src/frontend/aircane-web/src/features/library/LibraryView.vue`
      Verify: `npm run build` succeeds; `npm run test` passes. Runtime (step 9): heroes are 240px with
      bottom-left titles; content centered at 1200px.

- [ ] 6. Rules Lookup, AI Settings, Adventure Forge: standardize layout AND remove full-page
      background images (replace with solid `#0a0a1a`).
      - For each, on the OUTER view-root element: remove the full-page image `v-bg-asset`
        (`rules-lookup-background.png` / `ai-provider-background.png` / `adventure-forge-background.png`)
        and set an explicit opaque `background-color:#0a0a1a` (apply `.page-plain` or inline style).
        Also drop view-root scoped CSS that paints the page bg / `background-attachment: fixed`
        (`.ai-settings-page`, `.forge-page` `background-size/position`, and the RulesLookup root
        `bg-cover bg-center` classes). Remove the negative-margin page hacks tied to the old
        full-bleed bg where present (`.forge-page { margin:-1rem }`) since the shell now owns padding.
      - Convert each view's hero to the shared `.app-hero`/overlay classes (KEEP the hero's own
        `v-bg-asset` banner image — hero art is scoped to its element and must still render):
        - Rules Lookup: hero `.h-40` (160px) → `.app-hero` (240px); move the separate
          `<h1>Rules Lookup</h1>` + subtitle paragraph into the hero overlay (title "Rules Lookup",
          subtitle = existing descriptive sentence, trimmed to fit).
        - AI Settings: `.settings-hero` (min-height 120px, centered) → `.app-hero` (240px) with
          title "AI Provider Settings" + its subtitle in the overlay; keep the "Open Content Licenses"
          button (reposition as needed, it currently absolutely-positions inside the hero).
        - Adventure Forge: `.forge-hero` (160px) → `.app-hero` (240px); title "Generate Adventure".
      - Remove per-view root `mx-auto max-w-3xl`/`max-w-2xl` and rely on the shell container; keep the
        inner form panels/cards and their gold borders + panel art untouched (they must stay visually
        distinct against the plain dark bg).
      Files:
      `src/frontend/aircane-web/src/features/ai/RulesLookupView.vue`,
      `src/frontend/aircane-web/src/features/ai/AiSettingsView.vue`,
      `src/frontend/aircane-web/src/features/adventure-generation/AdventureGenerateView.vue`
      Verify: `npm run build` succeeds; `npm run test` passes (ModeToggle/ContentRatioSlider/
      GameSystemDropdown tests unaffected). Runtime (step 9): no full-page art, solid dark bg, hero
      banners + panels still render with gold borders.

- [ ] 7. Join Session: standardize layout, remove full-page background, AND fix form-vs-error ordering.
      - Remove the `join-session-background.png` full-page `v-bg-asset` on the `.join-page` root and
        set solid `#0a0a1a`; drop the `.join-page { background-attachment:fixed; background-size/position }`
        and the `-m-6` full-bleed hack (shell owns padding). Keep the hero banner `v-bg-asset`.
      - Convert `.join-hero` (160px) to shared `.app-hero` (240px); the hero currently has no title
        overlaid (header `<h1>Join Session</h1>` sits below it) — move "Join Session" into the hero
        overlay to match other views, and remove the now-redundant separate header `<h1>` row.
      - **Bug fix (form before error):** the template is a `v-if`/`v-else-if` chain:
        `loading` → `error && !currentSession` (error panel) → `joinResult` (joined) →
        `currentSession` (form + QR). When `fetchSession` fails (session not found), `currentSession`
        is null and `error` is set, so ONLY the "Session not found" panel renders and the form never
        appears. Restructure so:
        1. The join form (`<PlayerJoinForm>` + QR card) renders whenever NOT loading and NOT already
           joined — i.e. gate it on `!joinResult` (and not during the initial load), NOT on
           `store.currentSession`. Pass `:session-name="store.currentSession?.name"` (optional prop,
           PlayerJoinForm already handles the undefined case with fallback copy).
        2. The joined/confirmation panel still shows when `joinResult` is set (keep as the first branch
           after loading).
        3. The "Session not found" error panel renders BELOW the form only when `store.error` is set
           (independent block, not an `v-else-if` that hides the form). Note: `PlayerJoinForm` already
           renders `store.error` internally; to avoid a duplicate error, prefer letting the form show
           the API error and keep the standalone "Session not found" panel only for the load-failure
           case — place it after the form, guarded by `v-if="store.error && !joinResult"`, and ensure
           it does not suppress the form. Confirm no double-render of the same error at runtime
           (step 9) and adjust (e.g. show the standalone panel only when `!store.currentSession`).
      Files:
      `src/frontend/aircane-web/src/features/sessions/JoinSessionView.vue`
      (PlayerJoinForm.vue likely needs no change — session-name is already optional.)
      Verify: `npm run build` succeeds; `npm run test` passes (PlayerJoinForm/InviteCodeField/
      DisplayNameField tests unaffected). Runtime (step 9): visiting a join URL for a missing session
      shows the form (invite code + display name + Join button + QR) FIRST, with the error below it.

- [ ] 8. About & Credits: standardize hero, remove full-page background, add breathing room.
      - Remove the `about-credits-background.png` full-page `v-bg-asset` on the root and set solid
        `#0a0a1a`; drop the root `bg-cover bg-center bg-no-repeat` classes.
      - Convert the hero (`.h-40`, 160px, centered title) to the shared `.app-hero` (240px) using
        `about-hero-banner.png` with the "About & Credits" title in the bottom-left overlay — same
        pattern as the other views.
      - **Breathing room:** increase vertical gap between sections to ≥40px (the root uses
        `space-y-8` = 32px → raise to `space-y-10` (40px) or a `.page-sections`-style gap of 40px
        for this view specifically). Parchment license cards (`.license-card`): padding 32px all sides
        (currently `p-5` = 20px → `p-8` / explicit `padding:32px`). Dark attribution panels
        (`.about-panel` — open-content-attributions, content-licensing-declaration, application-license):
        padding 28px vertical / 32px horizontal (currently `p-5`; add
        `padding:28px 32px;` to `.about-panel`). Keep the gold borders and panel art.
      Files:
      `src/frontend/aircane-web/src/features/about/AboutView.vue`
      Verify: `npm run build` succeeds; `npm run test` passes. Runtime (step 9): 240px hero with title,
      solid dark bg, visibly larger spacing/padding.

- [ ] 9. Runtime verification pass (dev server) against the full checklist. This step confirms the
      items that cannot be proven by build/tests alone — DO NOT skip any reported issue as
      "already correct" based on code reading; confirm each at runtime and fix if the symptom appears.
      Start the dev server (`npm run dev` in `src/frontend/aircane-web`) and visit each route:
      `/`, `/campaigns`, `/library`, `/characters`, `/rules-lookup`, `/settings/ai`,
      `/adventures/generate`, `/sessions` (note: the "Dice Roller" nav points to `/sessions`, whose
      component is `features/sessions/index` default-exporting `JoinSessionView` — a pre-existing
      routing quirk, out of scope; just verify it renders), `/about`, and a `/join/<bad-id>` URL.
      Confirm each checklist item:
      - Sidebar exactly 220px on EVERY route incl. Dashboard (measure in devtools).
      - Content centered, max-width 1200px on every route.
      - Non-dashboard heroes exactly 240px tall; titles bottom-left (white 24px bold, grey 14px sub).
      - No full-page background image on Rules Lookup, AI Settings, Adventure Forge, Join Session,
        About (solid #0a0a1a; body `app-background.png` NOT bleeding through).
      - Dashboard/Campaigns/Library/Characters backgrounds unchanged.
      - New Campaign button on `/campaigns` uses `new-campaign-button-art.png` (not purple CSS button).
      - "Your Campaigns" appears exactly once (above the card list).
      - About hero + increased spacing/padding present.
      - Join Session shows the form before the error panel for a missing session; no duplicate error.
      - Navigation between views stays visually consistent.
      Files: none (verification; fix regressions in the relevant view/shared file if any item fails).
      Verify: dev server renders all routes without console errors; every checklist item passes.
      Stop the dev server when done.

- [ ] 10. Final gate: lint, build, test together, then commit.
      Run `npm run lint`, `npm run build`, `npm run test` in `src/frontend/aircane-web`; fix any
      issues. Then stage and commit the changed files on the current branch (do NOT push):
      `git -C d:\Development\aircane-tabletop\.worktrees\frontend-layout add <changed files>` and
      `git -C d:\Development\aircane-tabletop\.worktrees\frontend-layout commit -m "..."`.
      Files: all files changed in steps 1-8.
      Verify: lint, build, and test all pass; `git -C ... status` shows a clean tree after commit.

## Notes / assumptions / needs-verification
- The reported "duplicate Your Campaigns heading" IS confirmed in code (CampaignsView.vue header row
  + CampaignList.vue). Removing the CampaignsView header-row heading (keeping its New Campaign button)
  satisfies "appears only once, above the card list."
- The reported "Dashboard sidebar narrower than other pages" could not be reproduced from code alone
  (the sidebar is a single shared component). Pinning it to 220px makes width identical everywhere;
  CONFIRM at runtime in step 9 and investigate further if widths still differ by route.
- The Join Session bug IS confirmed in code (the `v-else-if` chain hides the form when the session
  fails to load). Fix per step 7.
- `main.css` body background (`app-background.png`) is intentionally left untouched (Dashboard root bg).
  Non-dashboard "remove background" views get an OPAQUE `#0a0a1a` so the body image cannot bleed through.
- Do not rename components, change APIs, or refactor unrelated logic. Match existing `<script setup>`
  + scoped-style conventions and reuse existing classes/patterns (e.g. HomeView's new-campaign-button).
