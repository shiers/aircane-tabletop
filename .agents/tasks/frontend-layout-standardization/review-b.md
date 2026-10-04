# Shared Vue page shell and standardized main-view presentation

This change moves the frontend’s main layout contract into `AppLayout.vue` and shared `main.css` primitives, then applies those primitives across every routed main view. The expanded sidebar is fixed at 220px, content is centered and capped at 1200px, non-dashboard heroes share a 240px bottom-left overlay treatment, and the five requested routes receive an opaque dark shell background while retaining hero and panel art. The Campaigns heading/button, About spacing, and Join Session form/error ordering are corrected, including the two MEDIUM findings from the prior pass.

Watch for: **confirmed, LOW** — the newly exposed no-ID Join form submits with an empty session ID and targets `/api/sessions//join`; **confirmed, LOW** — `pt-6` adds 24px above the container despite the specified zero top padding; **confirmed, LOW** — Campaigns copies Dashboard’s button CSS rather than sharing it; **confirmed, LOW** — browser-level layout and ordering checks remain unautomated.

**Verdict**: APPROVED

## High-level view

`App.vue` wraps all routes in `AppLayout`, which is now the width and sidebar authority. `.page-container` supplies the 1200px cap, centering, and horizontal/bottom padding, while `AppSidebar` uses one non-shrinking 220px expanded width on Dashboard and every other route.

Campaigns, Characters, Library, Rules Lookup, AI Settings, Adventure Forge, Join Session, and About use `.app-hero` plus the shared title/subtitle overlay; Dashboard now uses the same bottom-left overlay while retaining its separate height rule. Page-level section wrappers use 32px gaps, with About intentionally using the requested 40px minimum.

Rules Lookup, AI Settings, Adventure Forge, Join Session, and About remove their page-image directives and receive `#0a0a1a` from the route-aware `<main>`. This covers shell gutters while preserving route-local hero, card, form, QR, and error-panel art. Dashboard, Campaigns, Library, and Characters remain outside the plain-background route set.

Campaigns has one “Your Campaigns” heading above `CampaignList`, and its action uses the Dashboard image at `100% 100%` with matching 180px-by-44px minimum dimensions. About uses the requested banner and exact 40px, 32px, and 28px/32px spacing values. Join Session separates lookup state from the shared store, skips lookup without an ID, and renders form then QR card then lookup-error panel; submitting that no-ID form still sends an empty path identifier.

<details>
<summary>Issues (4)</summary>

1. **No-ID Join submission — confirmed, LOW** — The newly visible no-ID form passes `''` as the session ID, producing `/api/sessions//join`. Recover the session ID from the invite code or provide an invite-code-only join endpoint if this form is expected to submit successfully without a route ID.
2. **Effective top-padding deviation — confirmed, LOW** — `<main>` adds `pt-6`, so content starts 24px below the specified `padding: 0 32px 48px 32px` container. Remove `pt-6` if the literal zero top inset is required.
3. **Duplicated Campaign action styling — confirmed, LOW** — `HomeView.vue` and `CampaignsView.vue` carry matching scoped button rules that can drift. Move the recipe into shared CSS or a reusable component.
4. **Missing browser regression coverage — confirmed, LOW** — No browser-level check asserts the sidebar, container, hero, background, or Join ordering contracts. Add focused Playwright coverage for the exact visual and conditional-rendering requirements.

</details>

<details>
<summary>Details</summary>

### AppLayout owns width and plain-route backgrounds

The routed slot is wrapped once by `.page-container`, and the affected views remove their local `mx-auto max-w-*` constraints. Join Session’s nested `max-w-lg` remains a focused form width inside the common container rather than replacing it.

**Confirmed, LOW.** `<main>` still has `pt-6`, making the effective top inset 24px even though the specified container padding starts with zero. The deviation is centralized rather than route-specific, but removing `pt-6` would match the literal padding contract.

`plainBackgroundRoutes` contains the five named route names plus `sessions`, which renders the same Join view. Those views delete their full-page image directives and fixed/full-bleed rules, while `body` retains `app-background.png`; the plain fill therefore masks body art across the content viewport without changing Dashboard, Campaigns, Library, or Characters.

### Shared heroes and section rhythm cover the named views

`.app-hero` establishes the 240px non-dashboard banner, and `.app-hero__overlay` anchors white 24px/700 title text and a grey 14px subtitle at the bottom with 24px padding. All eight non-dashboard views consume these classes and preserve their scoped hero asset. Dashboard’s old center-right heading is removed in the final commit and replaced with the same overlay classes, leaving only its allowed bespoke height.

The main wrappers use `.page-sections { gap:32px }`; About’s stronger contract uses a 40px gap, 32px parchment-card padding, and 28px vertical/32px horizontal dark-panel padding.

### Campaigns consistency depends on copied button CSS

The view-level “Your Campaigns” heading is removed, leaving `CampaignList.vue` as the sole occurrence above the card grid. The action uses `new-campaign-button-art.png` through `v-bg-asset` with `size:'100% 100%'`, and its dimensions match HomeView.

**Confirmed, LOW.** `.new-campaign-button` and `.new-campaign-label` remain duplicated in scoped styles in `HomeView.vue` and `CampaignsView.vue`. Shared CSS or a reusable button component would prevent future visual drift.

### Join ordering is fixed, but the no-ID submit path is incomplete

The normal branch renders `PlayerJoinForm`, the QR card, and then the standalone lookup-error panel. Local `lookupLoading`/`lookupError` state separates lookup failures from join-submit failures, and `hideError` prevents duplicate banners. A no-ID route skips lookup and immediately reaches this branch.

**Confirmed, LOW.** That route passes `sessionId === ''` into `store.joinSession`; the API constructs `/api/sessions/${sessionId}/join`, resulting in `/api/sessions//join`. The requested rendering behavior is fixed, but successful no-ID submission requires a way to resolve a session from the invite code.

### Pixel-level behavior remains code-reviewed rather than browser-tested

The supplied evidence reports a passing Vue typecheck/Vite build and 304 passing Vitest tests. Lint was attempted but unavailable because the existing manifest does not install the referenced ESLint toolchain.

**Confirmed, LOW.** No live-browser or view-level regression check asserts the 220px sidebar, 1200px container, 240px heroes, route fills, or final Join form/QR/error order. Focused Playwright coverage would protect these exact contracts.

</details>

<details>
<summary>File map</summary>

- `src/assets/main.css` — shared content, section, plain-background, and hero primitives.
- `src/shared/components/AppLayout.vue` — common container and route-aware plain viewport fill.
- `src/shared/components/AppSidebar.vue` — fixed 220px expanded sidebar.
- `src/views/HomeView.vue` — common title overlay and shell-owned width.
- `src/features/campaigns/CampaignsView.vue` — shared hero, single list heading, and Dashboard-art action.
- `src/features/characters/CharactersView.vue` — shared hero and section rhythm.
- `src/features/library/LibraryView.vue` — shared hero, subtitle, and section rhythm.
- `src/features/ai/RulesLookupView.vue` — page-art removal plus shared hero and spacing.
- `src/features/ai/AiSettingsView.vue` — page-art removal plus shared hero and spacing.
- `src/features/adventure-generation/AdventureGenerateView.vue` — page-art/full-bleed removal plus shared hero and spacing.
- `src/features/sessions/JoinSessionView.vue` — shared presentation and independent lookup/form/error state.
- `src/features/sessions/PlayerJoinForm.vue` — suppression of parent-owned lookup errors.
- `src/features/about/AboutView.vue` — requested hero and expanded spacing/padding.

Full diff: `git -C d:\Development\aircane-tabletop\.worktrees\frontend-layout diff dev...frontend-layout-standardization`

</details>
