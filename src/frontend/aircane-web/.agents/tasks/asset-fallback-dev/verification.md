# Verification — Asset Fallback & Graceful Degradation

Frontend root: `src/frontend/aircane-web`. Branch: `dev` (in place, no worktree).
Backend untouched (frontend only). First iteration (no `review.json` present).

## What was run

### Production build — `npm run build` (vue-tsc + vite build)
- **Result: PASS.** `vue-tsc --noEmit` reported no type/template errors across all
  retrofitted views and the 3 new components, the composable, and the directive.
  `vite build` completed: `✓ built in 13.58s`, all chunks emitted (e.g.
  `RulesLookupView`, `AdventureGenerateView`, `AiSettingsView`, `AboutView`,
  `JoinSessionView` all built cleanly).

### Full unit suite — `npm test` (`vitest run`)
- **Result: PASS.** `Test Files 43 passed (43)`, `Tests 304 passed (304)`.
- Baseline before this task was **296** passing. This task adds **8** new fallback
  tests → **304** total. The original 296 stay green.
- `vue-tsc --noEmit` was also run standalone after each batch (all clean).

## The 8 new fallback unit tests (all passing)

File | Tests
--- | ---
`src/directives/__tests__/vBgAsset.test.ts` | (1) fallback colour applied on 404 via `img.onerror`, no `background-image`, warns `[Aircane] Background asset failed to load: …`; (2) `background-image` + size/position applied on `img.onload`
`src/shared/components/__tests__/AircaneImg.test.ts` | (3) `#fallback` slot shown when the img fires `error`, img removed, warns `[Aircane] Asset failed to load: …`; (4) img shown on load with correct `src`/`alt` and `$attrs` forwarded to the `<img>`
`src/shared/components/__tests__/NavIcon.test.ts` | (5) emoji fallback (`🔖`) when `icon-campaign` PNG fails; (6) PNG shown (`/assets/ui/icon-campaign.png`) when available
`src/shared/components/__tests__/ThumbnailPlaceholder.test.ts` | (7) initials `SV` from label `Shadow Vale`; (8) `✦` ornament only, no initials, when no label

Both the directive load/error branches are driven deterministically by stubbing the
global `Image` constructor (`vi.stubGlobal('Image', FakeImage)`) and firing
`onload` / `onerror`. The `<img>` branches use `@vue/test-utils` `trigger('error')`.

## Graceful-degradation behaviours VERIFIED by unit tests

- `vBgAsset`: sets the solid fallback colour synchronously on mount, swaps in the
  image only after it successfully preloads, keeps the fallback colour (and does
  **not** set `background-image`) when the preload 404s, and warns (not errors).
- `AircaneImg`: swaps `<img>` → `#fallback` slot on load failure; forwards `$attrs`
  to the `<img>` (via `inheritAttrs:false`); warns on failure.
- `NavIcon`: PNG → emoji map fallback on `error`; correct `src`/`alt` when present.
- `ThumbnailPlaceholder`: initials-from-name and `✦`-ornament-only (no label) paths.

Existing component tests updated to register the directive / assert behaviour after
the mechanism change (still green): `RoleBadge`, `AbilityScoreTile`,
`GameSystemDropdown`, `PlayerJoinForm`.

## Still needs MANUAL visual QA in a browser

Unit tests confirm the logic/branches but not the rendered pixels. A human should
force failures (e.g. DevTools → block `/assets/**`, or temporarily rename an asset
folder) and confirm, per view:

1. **No broken-image icon and no checkerboard anywhere** when assets 404.
2. **Type A full-page / view backgrounds** (`*-background.png`, `*-hero-background`):
   solid `#0a0a1a` shows immediately (no white flash) and remains on failure.
   Views: body (`main.css`), side-nav, topbar, Library/Characters/Campaigns heroes,
   AI settings page + hero, Rules Lookup page, Adventure Forge page, About page,
   Join Session page.
3. **Type B hero banners** (`*-hero-banner`, `dashboard-welcome-banner`): dark navy
   `#0d0d2a` fill with overlaid heading text still legible. (Rules Lookup, About,
   Adventure Forge, Join Session, Dashboard welcome banner.)
4. **Type C panels / banners / cards / fields / stat tiles / provider cards /
   attribution badge**: `#0d0d2a` fill with a gold (or gold-dim) border; overlaid
   text always renders. Check Dashboard panels, Campaign/Characters/Library side
   panels, AbilityScoreTile, RoleBadge, Watched-folders banner, citation cards,
   license cards, attribution badges, adventure-forge dropdown/field art, provider
   cards (still selectable with no art), stat tiles.
5. **Type D buttons** (`*-button-art`, incl. the literal `ask-question-button-.png`):
   CSS button base (purple/dark bg + gold border + glow for primary) is always
   present so the button is clickable and labelled with no art.
6. **Type E thumbnails / card art / document cover art**: `ThumbnailPlaceholder`
   (initials or `✦`) with navy bg + gold border. Feature cards, campaign cards
   (`:label="campaign.name"`), document rows (`:label="doc.title"`), logo mark
   (fallback text `✦`).
7. **Type F decorative ornaments** (`*-ornament`, `*-divider`,
   `legal-scroll-ornament`, decorative dice icon, card frame, friendly-error
   ornaments, footer quote, empty-state ornaments): element is hidden on failure
   (`@error` sets `display:none`) — no gap, no broken icon.
8. **Type G nav icons**: emoji fallback map renders at 24×24 when a PNG is missing;
   Dashboard keeps its inline SVG (there is no `icon-dashboard.png`).
9. **Type H character portraits**: name-initial placeholder fills the portrait slot
   at the same dimensions.
10. **Type I provider cards**: provider name text + selectable/pressed state work
    independently of the card art.
11. Confirm the **flash-free** behaviour (colour-first) specifically: on a slow
    network the solid colour should be visible before the image paints.

## Safety / spec compliance notes

- All asset-failure logging uses `console.warn("[Aircane] …")` with the asset URL
  only — never `console.error`, never throws/rethrows, never asset content.
- No asset-fallback code touches the API-key field; the API key is never logged or
  echoed (`ApiKeyField` behaviour unchanged; its tests still pass).
- The quirky filename `ask-question-button-.png` (missing "art", trailing hyphen)
  is preserved verbatim.
- `DocumentStatusBadge` already degrades (CSS `background-color` behind the PNG;
  CSS-only `error` / `ocr-required`). Left unchanged by design, per the plan.
- `section-divider-ornament.png` appears only in a CSS comment (no live element) —
  no change needed.
