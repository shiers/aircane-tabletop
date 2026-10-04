# Shared Vue shell with incomplete Dashboard and Join behaviors

This change centralizes the main content column, expanded sidebar width, section rhythm, plain-page fill, and non-dashboard hero treatment across the Vue frontend. It delivers the Campaigns button/heading changes, About spacing, and the normal fresh-state Join error ordering, while the second pass resolves the previously raised spacing, subtitle, shell-gutter, and duplicate-error findings. Two behavioral requirements remain incomplete in Dashboard hero placement and Join Session’s no-ID/stale-store paths.

Watch for: **confirmed, MEDIUM** — `/sessions` fetches with an undefined session ID and hides the form behind loading, while stale session state moves lookup errors inside the form; **confirmed, MEDIUM** — Dashboard still places its title/subtitle center-right although its stated exception covers hero height only; **confirmed, LOW** — Campaigns duplicates Dashboard-scoped button CSS that must remain identical.

**Verdict**: NEEDS_CHANGES

## High-level view

`App.vue` places every routed view inside `AppLayout`, which now owns the specified centered 1200px content container. The shared sidebar is 220px when expanded and cannot shrink; `main.css` owns the 32px section rhythm, plain-page fill, and 240px non-dashboard hero contract.

The five targeted page-art backgrounds are removed, and route-aware shell styling now fills the entire `<main>` viewport with `#0a0a1a` on those pages. Hero and panel art remains scoped, every non-dashboard hero uses the shared bottom-left title/subtitle overlay, Campaigns has one list heading and the requested button art, and About has the requested 40px/32px/28px spacing values.

Join Session’s fresh failed-lookup path renders the form, QR card, and standalone error in that order without duplication. The no-ID route still starts an invalid lookup and suppresses the form while loading, and the error-channel split incorrectly assumes `currentSession` cannot contain stale state.

Dashboard remains a bespoke hero composition with its heading at `top: 50%` and `left: 58%`. That does not meet the required bottom-left, 24px-padded title treatment, and the verification note cannot grant an exception not present in the authoritative request.

<details>
<summary>Issues (4)</summary>

1. **No-ID and stale-state Join flow — confirmed, MEDIUM** — `/sessions` calls `fetchSession(undefined)` and hides the form while that invalid request loads; a failed lookup with stale `currentSession` shows the error inside the form and suppresses the below-form panel. Skip lookup without an ID and track lookup failure independently from prior session state.
2. **Dashboard overlay position — confirmed, MEDIUM** — Dashboard retains a center-right title/subtitle even though only hero height is exempted. Apply the required bottom-left 24px overlay or obtain an explicit product exception.
3. **Duplicated New Campaign styling — confirmed, LOW** — HomeView and CampaignsView contain matching scoped button and label rules that can drift. Move the shared visual contract into a reusable class or component.
4. **Join regression coverage — confirmed, LOW** — No view-level test or browser check covers `/sessions` without an ID, stale Pinia session state, or final form/QR/error ordering. Add focused coverage after correcting the flow.

</details>

<details>
<summary>Details</summary>

### Join Session still depends on a valid, empty session store

**Confirmed, MEDIUM.** `JoinSessionView.vue:40` calls `store.fetchSession(sessionId)` unconditionally, but `/sessions` has no `sessionId` parameter. The type assertion does not create a value, so this route looks up `undefined`; during that request, `store.loading && !store.currentSession` at line 70 selects the loading panel and prevents `PlayerJoinForm` from rendering. This misses the explicit requirement that the form remain visible when no session ID is pre-populated.

The new lookup-error placement depends on `!store.currentSession` at lines 105 and 130. `fetchSession` records an error but does not clear an existing `currentSession`, so a failed lookup after another flow populated the Pinia store leaves stale state behind. `hideError` is then false and the standalone panel is suppressed, placing the lookup error inside `PlayerJoinForm` before its fields and pairing it with the stale session name.

Skip the lookup when no route ID exists. For ID-backed lookups, use a dedicated lookup-error state or clear prior session state before loading so fetch failures consistently use the standalone below-form panel while submit failures remain inside the form.

### Dashboard remains outside the required overlay geometry

**Confirmed, MEDIUM.** `HomeView.vue:100-103` retains `.hero-heading`; its CSS at lines 242-248 uses `top: 50%`, `left: 58%`, `right: 5%`, and a vertical transform. The original request exempts Dashboard only from the 240px hero height, then says page title and subtitle are “always” positioned bottom-left with 24px padding.

The verification note’s visual-design rationale is not an approved product exception. Move the heading to the required geometry or obtain an explicit exception before treating the checklist as complete.

### Campaign button consistency is maintained by duplicated scoped CSS

**Confirmed, LOW.** The asset and dimensions are correct, but `.new-campaign-button` and `.new-campaign-label` are duplicated in `HomeView.vue:315-340` and `CampaignsView.vue:151-176`, including border, background, hover, and typography rules. A shared class or component would preserve the required identity without future route-specific drift.

### Verification does not exercise the remaining state combinations

**Confirmed, LOW.** The supplied evidence records a successful TypeScript/Vite build and 304 passing Vitest tests; lint was attempted but unavailable because the existing manifest lacks the referenced ESLint toolchain. Not tested: rendered navigation through the route checklist, `/sessions` without a route parameter, a failed lookup with stale `currentSession`, or the final form/QR/error ordering. Existing session tests mount `PlayerJoinForm` in isolation rather than `JoinSessionView`.

</details>

<details>
<summary>File map</summary>

- `src/assets/main.css` — shared content, spacing, plain-background, and hero primitives.
- `shared/components/AppLayout.vue` — shared container and route-aware plain viewport fill.
- `shared/components/AppSidebar.vue` — fixed 220px expanded sidebar.
- `views/HomeView.vue` — removes the local width cap but retains bespoke Dashboard hero placement.
- `features/campaigns/CampaignsView.vue` — shared hero, single list heading, and Dashboard button art.
- `features/characters/CharactersView.vue` — shared hero and section spacing.
- `features/library/LibraryView.vue` — shared hero and section spacing.
- `features/ai/RulesLookupView.vue` — removes page art and adopts shared hero/spacing.
- `features/ai/AiSettingsView.vue` — removes page art and adopts shared hero/spacing.
- `features/adventure-generation/AdventureGenerateView.vue` — removes page art/full-bleed hacks and adopts shared hero/spacing.
- `features/sessions/JoinSessionView.vue` — shared hero and reordered form/QR/error states.
- `features/sessions/PlayerJoinForm.vue` — conditional suppression of the shared store error.
- `features/about/AboutView.vue` — requested hero, gaps, and padding after page-art removal.
- `.agents/tasks/frontend-layout-standardization/plan.md` and `verification.md` — plan and supplied verification evidence.

Full diff: `git -C d:\Development\aircane-tabletop\.worktrees\frontend-layout diff dev...frontend-layout-standardization`

</details>
