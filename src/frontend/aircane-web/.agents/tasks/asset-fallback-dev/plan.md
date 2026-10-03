# Implementation Plan — Asset Fallback & Graceful Degradation

Retrofit every `/assets/...` art reference in the Aircane Tabletop frontend so a missing/corrupt/failed-to-load asset degrades to a clean functional state (no broken-image icon, no layout break, no checkerboard). Frontend root: `src/frontend/aircane-web/`.

## Conventions discovered during exploration (ground every item in these)

- Build/typecheck: `npm run build` (runs `vue-tsc && vite build`). Lint: `npm run lint`. Tests: `npm run test` (= `vitest --run`).
- Vitest config lives in `vite.config.ts` → `test: { globals: true, environment: 'jsdom', setupFiles: [] }`. `globals: true`, so `describe/it/expect/vi` are available without import, but every existing test still imports them explicitly from `vitest` — match that.
- Path alias `@` → `./src` (from `vite.config.ts`). Existing tests use **relative** imports (`../Foo.vue`), not `@`; match the surrounding file.
- Tests are **co-located in `__tests__/` sibling folders**, named `<Thing>.test.ts` (NOT `.spec.ts`; `.spec.ts` is Playwright e2e under `tests/e2e/`). Component tests use `mount` from `@vue/test-utils` and assert on `wrapper.find(...)`, `.classes()`, `.attributes('style')`, `.text()`. See `src/shared/components/__tests__/HealthStatusPill.test.ts`.
- `<script setup lang="ts">` everywhere. Props via `defineProps<{...}>()`, emits via `defineEmits<{...}>()`. Components are PascalCase. Scoped `<style scoped>` using CSS custom properties from `tokens.css` (`var(--border-gold)`, `var(--border-radius-md)`, etc.).
- `tokens.css` is imported in `main.ts` immediately after `./assets/main.css`. There are NO existing directives or composables directories (`src/directives/`, `src/composables/` do not exist yet — create them).
- Global button art already lives in `src/assets/main.css` `.btn-primary` / `.btn-secondary` (background-image on `/assets/ui/primary-button-glow.png` and `secondary-button-glow.png`) with Tailwind `@apply` base styling already present (color, bg, shadow). The CSS button base is therefore already art-independent for `.btn-primary`/`.btn-secondary`; the per-view art buttons (below) are the ones that need an art-independent base.

### Quirks to preserve (do NOT "fix" these)
- Ask Question button file is literally `ask-question-button-.png` (missing "art", trailing hyphen) in `RulesLookupView.vue`. Keep the exact filename.
- `DocumentStatusBadge.vue`: `error` and `ocr-required` have NO PNGs and are CSS-rendered. No image fallback needed — only `parsed`/`processing` paint a PNG, and they already have a CSS `background-color` fallback behind the text. Leave this component's fallback behavior intact (it already degrades gracefully); record it, do not add `v-bg-asset`.
- There is NO `icon-dashboard.png`. Dashboard nav item uses an inline SVG (`iconSrc` undefined in `AppSidebar.vue`). Leave the Dashboard SVG as-is. NavIcon applies ONLY to the 8 real `icon-*.png` nav items.
- API-key field (`ApiKeyField.vue` / AiSettingsView key input) must never log or echo its value. This retrofit must not add any logging that touches the key; asset `console.warn` logs only asset URLs.

---

## Authoritative reference inventory (complete; found via `/assets/` grep across `.vue`/`.ts`/`.css`)

Classified by fallback TYPE (A–I) and target mechanism. "scoped CSS" = `background-image` in a `<style scoped>` rule; "inline" = `:style`/`style` binding; "Tailwind arb" = `bg-[url(...)]` class.

**Global CSS (`src/assets/main.css`)**
- `/assets/ui/app-background.png` — body background (scoped: global `@layer base body`). Type A.
- `/assets/ui/primary-button-glow.png` — `.btn-primary` (global CSS). Type D (base already present).
- `/assets/ui/secondary-button-glow.png` — `.btn-secondary` (global CSS). Type D (base already present).
- `/assets/ui/section-divider-ornament.png` — only referenced inside a CSS comment (`.section-divider` usage hint); no live element. Type F — document, no change.

**`src/shared/components/AppSidebar.vue`**
- `/assets/ui/side-nav-background.png` — inline `style` background on sidebar. Type A.
- `/assets/ui/app-logo-mark.png` — `<img>` logo mark. Type E/logo (AircaneImg, fallback text "✦").
- `/assets/ui/icon-campaign.png`, `icon-library.png`, `icon-character.png`, `icon-rules.png`, `icon-ai.png`, `icon-adventure.png`, `icon-dice.png`, `icon-about.png` — 8 nav `iconSrc` entries (currently rendered how AppSidebar renders iconSrc). Type G → NavIcon. Dashboard has no iconSrc (SVG) — leave.

**`src/shared/components/AppLayout.vue`**
- `/assets/ui/topbar-background.png` — inline `style` background on header. Type A.

**`src/views/HomeView.vue`**
- `/assets/dashboard/document-library-thumbnail.png`, `campaign-management-thumbnail.png`, `character-sheets-thumbnail.png`, `dice-roller-thumbnail.png`, `ai-dm-runtime-thumbnail.png`, `adventure-generation-thumbnail.png` — feature card `<img class="feature-thumbnail">`. Type E → AircaneImg + ThumbnailPlaceholder.
- `/assets/dashboard/quick-action-start-ai-dm-card.png`, `quick-action-import-documents-card.png`, `quick-action-create-character-card.png`, `quick-action-roll-dice-card.png` — inline `:style backgroundImage` on `.quick-action-card`. Type E/card → v-bg-asset (card with label text always rendered; fallback `--fallback-card`/panel navy).
- `/assets/dashboard/dashboard-hero-background.png` — scoped CSS `.dashboard-hero`. Type A/hero → v-bg-asset `#0d0d2a`.
- `/assets/dashboard/dashboard-welcome-banner.png` — scoped CSS `.dashboard-welcome-banner`. Type B hero banner → v-bg-asset `#0d0d2a`.
- `/assets/dashboard/quick-actions-panel-art.png` — scoped CSS `.panel-header--quick-actions`. Type C panel → v-bg-asset `#0d0d2a` + gold border.
- `/assets/dashboard/recent-campaigns-panel-art.png` — scoped CSS `.panel-header--recent-campaigns`. Type C panel → v-bg-asset.
- `/assets/dashboard/new-campaign-button-art.png` — scoped CSS `.new-campaign-button`. Type D button → v-bg-asset + CSS button base.
- `/assets/dashboard/footer-quote-ornament.png` — `<img class="footer-quote-ornament">`. Type F ornament → plain `<img @error>` hide.

**`src/features/campaigns/CampaignsView.vue`**
- `/assets/campaigns/campaigns-hero-background.png` — scoped CSS hero. Type A/hero → v-bg-asset.
- `/assets/campaigns/campaign-overview-panel-art.png`, `recent-sessions-panel-art.png` — scoped CSS `.panel--overview`/`.panel--sessions`. Type C → v-bg-asset.
- `/assets/campaigns/continue-button-art.png` — scoped CSS `.continue-button`. Type D → v-bg-asset + CSS button base.

**`src/features/campaigns/components/CampaignList.vue`**
- `/assets/campaigns/campaign-card-shadow-vale-thumbnail.png`, `campaign-card-ironport-thumbnail.png`, `campaign-card-duskkeep-thumbnail.png`, `campaign-card-whispers-hollow-thumbnail.png` — placeholder array, rendered via `<img :src="placeholderThumbnail(index)" class="campaign-thumbnail">`. Type E → AircaneImg + ThumbnailPlaceholder `:label="campaign.name"`.
- `/assets/campaigns/campaign-empty-state-ornament.png` — `<img class="empty-state-ornament">`. Type F ornament → plain `<img @error>` hide.

**`src/features/characters/CharactersView.vue`**
- `/assets/characters/character-stat-tile-${tile.key}.png` — inline `:style backgroundImage` (dynamic key). Type C/panel-ish stat tile → v-bg-asset (dynamic url binding) `#0d0d2a`.
- `/assets/characters/characters-hero-background.png` — scoped CSS hero. Type A/hero → v-bg-asset.
- `/assets/characters/import-pdf-button-art.png`, `import-json-button-art.png` — scoped CSS `.import-button--pdf`/`--json`. Type D → v-bg-asset + CSS button base.
- `/assets/characters/character-detail-side-panel-art.png` — scoped CSS side panel. Type C → v-bg-asset.

**`src/features/characters/components/CharacterList.vue`**
- `/assets/characters/character-portrait-${key}.png` (fighter/rogue/cleric/wizard) via `portraitUrl()` → `<img class="portrait">`. Type H portrait → AircaneImg + ThumbnailPlaceholder `:label="character.name"` (name-initial avatar).
- `/assets/characters/character-card-frame.png` — `<img class="frame">` decorative overlay. Type F ornament → plain `<img @error>` hide (frame is decorative; hide on failure so no broken icon).
- `/assets/characters/character-empty-state-ornament.png` — `<img>` empty state. Type F ornament → plain `<img @error>` hide.

**`src/features/characters/components/AbilityScoreTile.vue`**
- `/assets/characters/ability-score-tile.png` — scoped CSS `.ability-score-tile`. Type C/panel (bordered tile) → v-bg-asset `#0d0d2a` + gold-dim border; score/mod text always renders.

**`src/features/characters/components/RoleBadge.vue`**
- `/assets/characters/player-badge-art.png`, `npc-badge-art.png` — inline `:style backgroundImage` via `artUrl()`. Type C/panel badge → v-bg-asset (dynamic url) `#0d0d2a`; dot + label always render (already have CSS color fallback).

**`src/features/library/LibraryView.vue`**
- `/assets/library/document-library-hero-background.png` — scoped CSS hero. Type A/hero → v-bg-asset.
- `/assets/library/open-content-licenses-button-art.png` — scoped CSS button (`background-color: transparent` + art). Type D → v-bg-asset + CSS button base.

**`src/features/library/components/DocumentList.vue`**
- `/assets/library/document-thumbnail-srd.png`, `document-thumbnail-pf2e.png`, `document-thumbnail-lost-mine.png`, `document-thumbnail-homebrew-monsters.png` — via `thumbnailFor(doc)` → row `<img class="h-10 w-10 ...">`. Type E → AircaneImg + ThumbnailPlaceholder `:label="doc.title"`.
- `/assets/library/library-empty-state-ornament.png` — `<img>` empty state. Type F ornament → plain `<img @error>` hide.
- `/assets/library/document-detail-side-panel-art.png` — scoped CSS side panel. Type C → v-bg-asset.

**`src/features/library/components/DocumentStatusBadge.vue`**
- `/assets/library/document-status-parsed-badge.png`, `document-status-processing-badge.png` — inline `:style backgroundImage` (only when `config.image`). Type C-ish, BUT already has CSS `background-color` fallback + CSS-only `error`/`ocr-required`. **No change** — record as already-degrading. (Do not add `v-bg-asset`; the badge paints color behind the PNG and label text always renders.)

**`src/features/library/components/DocumentUploadForm.vue`**
- `/assets/library/upload-document-panel-art.png` — scoped CSS `.upload-panel`. Type C → v-bg-asset.

**`src/features/library/components/RegisterFolderForm.vue`**
- `/assets/library/add-watched-folder-button-art.png`, `register-folder-button-art.png` — scoped CSS `.btn-art-add`/`.btn-art-register`. Type D → v-bg-asset + CSS button base.

**`src/features/library/components/WatchedFolderSection.vue`**
- `/assets/library/watched-folders-banner.png` — scoped CSS `.watched-folders-banner`. Type C banner → v-bg-asset `#0d0d2a` + gold border.

**`src/features/ai/AiSettingsView.vue`**
- `/assets/ai-settings/provider-card-openai.png`, `provider-card-azure-openai.png`, `provider-card-bedrock.png`, `provider-card-ollama.png` — inline `:style backgroundImage` on `.provider-card` `<button>`. Type I provider card → v-bg-asset (dynamic url) `#0d0d2a`; provider name text + selectable state always render.
- `/assets/ai-settings/ai-provider-background.png` — scoped CSS `.ai-settings-page`. Type A → v-bg-asset.
- `/assets/ai-settings/ai-provider-settings-hero.png` — scoped CSS `.settings-hero`. Type A/hero → v-bg-asset.
- `/assets/ai-settings/save-configuration-button-art.png`, `test-connection-button-art.png` — scoped CSS `.art-button--save`/`--test`. Type D → v-bg-asset + CSS button base.
- `/assets/ai-settings/ai-runtime-panel-art.png` — scoped CSS panel. Type C → v-bg-asset.

**`src/features/ai/RulesLookupView.vue`**
- `/assets/rules/rules-lookup-background.png` — inline `style` page bg. Type A → v-bg-asset.
- `/assets/rules/rules-lookup-hero-banner.png` — inline `style` hero. Type B hero banner → v-bg-asset `#0d0d2a`.
- `/assets/rules/ask-question-panel-art.png` — inline `style` panel. Type C → v-bg-asset.
- `/assets/rules/ask-question-button-.png` (QUIRK: literal filename) — inline `style` button. Type D → v-bg-asset + CSS button base. Preserve exact filename.
- `/assets/rules/citation-card-art.png` — inline `style` on citation card (`v-for`). Type C card → v-bg-asset (dynamic/static url) `#0d0d2a`.

**`src/features/ai/components/GameSystemDropdown.vue`**
- `/assets/rules/game-system-field-art.png` — inline `style` field art. Type C field → v-bg-asset `#0d0d2a`.
- `/assets/ui/icon-dice.png` — decorative `<img>` dice icon inside the field. Type F ornament (decorative, `aria-hidden`) → plain `<img @error>` hide. (Not a nav icon, so NOT NavIcon.)

**`src/features/adventure-generation/AdventureGenerateView.vue`** (Tailwind arbitrary `bg-[url(...)]` classes — distinct mechanism)
- `/assets/adventure-forge/adventure-forge-background.png` — `.forge-page` class `bg-[url(...)]`. Type A → v-bg-asset (remove the `bg-[url(...)]` arbitrary class, keep `bg-cover bg-fixed bg-center`).
- `/assets/adventure-forge/adventure-forge-hero-banner.png` — `.forge-hero` class. Type B hero → v-bg-asset `#0d0d2a`.
- `/assets/adventure-forge/adventure-forge-panel-background.png` — `.forge-panel` form class. Type C → v-bg-asset.
- `/assets/adventure-forge/tone-dropdown-art.png`, `length-dropdown-art.png`, `difficulty-dropdown-art.png`, `setting-field-art.png` — `.field-art` dropdown/field classes. Type C field → v-bg-asset `#0d0d2a`.
- `/assets/adventure-forge/generate-adventure-button-art.png` — `.generate-button`. Type D → v-bg-asset + CSS button base.

**`src/features/about/AboutView.vue`**
- `/assets/about/about-credits-background.png` — inline `style` page bg. Type A → v-bg-asset.
- `/assets/about/about-hero-banner.png` — inline `style` hero (role=img). Type B hero banner → v-bg-asset `#0d0d2a`.
- `/assets/about/dnd-srd-license-card.png`, `pf2e-orc-license-card.png` — inline `:style backgroundImage` license cards (`v-for`). Type C card → v-bg-asset (dynamic url) `#0d0d2a`; card text always renders.
- `/assets/about/open-content-attributions-panel.png`, `content-licensing-declaration-panel.png`, `application-license-panel.png` — inline `style` panels. Type C → v-bg-asset.
- `/assets/about/attribution-badge.png` — inline `style` badge (`v-for`). Type C/attribution badge → v-bg-asset `#0d0d2a`; overlaid license-name text always renders.
- `/assets/about/legal-scroll-ornament.png` (×3 dividers) — `<img>` ornaments. Type F ornament → plain `<img @error>` hide.

**`src/features/sessions/JoinSessionView.vue`**
- `/assets/join-session/join-session-background.png` — scoped CSS `.join-page`. Type A → v-bg-asset.
- `/assets/join-session/join-session-hero-banner.png` — scoped CSS `.join-hero`. Type B hero banner → v-bg-asset `#0d0d2a`.
- `/assets/join-session/session-not-found-error-panel.png` — scoped CSS `.not-found-panel`. Type C → v-bg-asset.
- `/assets/join-session/join-by-qr-card.png` — scoped CSS `.qr-card`. Type C card → v-bg-asset.

**`src/features/sessions/PlayerJoinForm.vue`**
- `/assets/join-session/join-session-panel-art.png` — scoped CSS `.join-panel`. Type C → v-bg-asset.
- `/assets/join-session/join-session-button-art.png` — scoped CSS `.join-submit`. Type D → v-bg-asset + CSS button base.

**`src/features/sessions/components/InviteCodeField.vue` and `DisplayNameField.vue`**
- `/assets/join-session/friendly-error-ornament.png` — `<img>` ornament in each. Type F ornament → plain `<img @error>` hide.

> Note on CSS-class vs element mechanism: scoped-CSS `background-image` rules (e.g. `.join-page`, `.panel--overview`) cannot be driven by `v-bg-asset` while the URL stays in the stylesheet. For each such rule, the implementer moves the URL off the CSS rule and onto the element via `v-bg-asset="{ url, fallback, ... }"`, keeping the rule's non-image properties (size/position/radius/border). This is called out per-file in the steps below.

---

## New files to create

- `src/composables/useAsset.ts` — `useAsset` composable + `AssetType`, `AssetOptions`, `FALLBACK_COLORS`.
- `src/directives/vBgAsset.ts` — `vBgAsset` directive + `BgAssetOptions`.
- `src/shared/components/AircaneImg.vue`
- `src/shared/components/NavIcon.vue`
- `src/shared/components/ThumbnailPlaceholder.vue`
- Co-located tests under `__tests__/` next to each (8 tests total, see item 13).

---

## Ordered implementation items

- [ ] 1. Add the global CSS fallback tokens to `tokens.css` `:root`.
      Add: `--fallback-bg:#0a0a1a; --fallback-panel:#0d0d2a; --fallback-hero:linear-gradient(180deg,#0d0d2a 0%,#0a0a1a 100%); --fallback-thumbnail:#0f0f1e; --fallback-card:#0d0d24;` under a new `/* Fallback layer */` comment. No existing tokens change.
      Files: `src/assets/styles/tokens.css`
      Verify: `npm run build` succeeds (CSS parses, vue-tsc unaffected).

- [ ] 2. Create the `useAsset` composable.
      Export `type AssetType = 'background'|'hero'|'panel'|'button'|'thumbnail'|'ornament'|'icon'|'portrait'|'provider-card'`; `interface AssetOptions { path: string; type: AssetType; fallbackColor?: string; fallbackEmoji?: string }`; `const FALLBACK_COLORS: Record<AssetType,string>` = { background:'#0a0a1a', hero:'#0d0d2a', panel:'#0d0d2a', button:'transparent', thumbnail:'#0f0f1e', ornament:'transparent', icon:'transparent', portrait:'#0d0d2a', 'provider-card':'#0d0d2a' }. `useAsset(options)` returns `{ failed: Ref<boolean>, onError(): void, backgroundStyle: ComputedRef<Record<string,string>>, imgSrc: ComputedRef<string>, fallbackEmoji?: string }`. `onError` sets `failed.value=true` and `console.warn(\`[Aircane] Asset failed to load: ${options.path}\`)`. `backgroundStyle` returns `{}` when `failed.value` OR `type==='ornament'`, else `{ backgroundColor: fallbackColor ?? FALLBACK_COLORS[type], backgroundImage: failed.value ? 'none' : \`url('${path}')\` }`. `imgSrc` returns `''` when failed else `path`. Never throws.
      Files: `src/composables/useAsset.ts`
      Verify: `npm run build` typechecks; covered by tests in item 13.

- [ ] 3. Create the `vBgAsset` directive.
      `interface BgAssetOptions { url: string; fallback?: string; size?: string; position?: string }`. `mounted(el, binding)`: read `{ url, fallback='#0a0a1a', size='cover', position='center' }`; set `el.style.backgroundColor = fallback` immediately; `const img = new Image(); img.onload = () => { el.style.backgroundImage = \`url('${url}')\`; el.style.backgroundSize = size; el.style.backgroundPosition = position }; img.onerror = () => console.warn(\`[Aircane] Background asset failed to load: ${url}\`); img.src = url`. `updated(el, binding)`: if `binding.value.url !== binding.oldValue?.url`, re-run the load. Store the last-applied url on the el (e.g. a `data-` attr or WeakMap) to avoid reloading when url is unchanged. Export default as `vBgAsset`. Never throws.
      Files: `src/directives/vBgAsset.ts`
      Verify: `npm run build` typechecks; covered by tests in item 13.

- [ ] 4. Register the directive in `main.ts`.
      Add `import vBgAsset from './directives/vBgAsset'` and, after `const app = createApp(App)`, `app.directive('bg-asset', vBgAsset)`. Do not reorder the existing `main.css` → `tokens.css` imports or the error-capture install.
      Files: `src/main.ts`
      Verify: `npm run build` succeeds; `npm run test` still green.

- [ ] 5. Create `AircaneImg.vue`.
      `<script setup lang="ts">` with `defineProps<{ src: string; alt?: string; type?: AssetType; fallbackEmoji?: string; fallbackText?: string; fallbackColor?: string }>()`, `defineOptions({ inheritAttrs: false })`. Use `useAsset({ path: src, type: type ?? 'thumbnail', fallbackColor })`. Template: a container with `:style="{ backgroundColor: fallbackColor ?? FALLBACK_COLORS[type ?? 'thumbnail'] }"`; inside, `<img v-if="!failed && src" :src="src" :alt="alt ?? ''" @error="onError" v-bind="$attrs">`; plus a `#fallback` slot shown when `failed`, defaulting to a `<span>` of `fallbackEmoji` else `fallbackText`. `$attrs` must land on the `<img>` (hence `inheritAttrs:false`).
      Files: `src/shared/components/AircaneImg.vue`
      Verify: `npm run build`; covered by tests in item 13.

- [ ] 6. Create `NavIcon.vue`.
      `defineProps<{ icon: string; label: string }>()`. `const ICON_FALLBACKS: Record<string,string> = { 'icon-library':'📚','icon-campaign':'🔖','icon-character':'👤','icon-rules':'📖','icon-ai':'✦','icon-adventure':'🌿','icon-dice':'🎲','icon-about':'ℹ','icon-settings':'⚙' }`. `const failed = ref(false)`. Template: 24×24 flex-centered span; `<img v-if="!failed" :src="\`/assets/ui/${icon}.png\`" :alt="label" @error="failed=true" style="object-fit:contain">` else `<span class="fallback">{{ ICON_FALLBACKS[icon] ?? '•' }}</span>`. Note: `icon` prop is the PNG basename (e.g. `icon-campaign`).
      Files: `src/shared/components/NavIcon.vue`
      Verify: `npm run build`; covered by tests in item 13.

- [ ] 7. Create `ThumbnailPlaceholder.vue`.
      `defineProps<{ label?: string }>()`. `const initials = computed(() => { const w = (label ?? '').trim().split(/\s+/).filter(Boolean); return w.length ? w.slice(0,2).map(s => s[0]!.toUpperCase()).join('') : '✦' })`. Template renders a ✦ gold ornament plus the initials, centered. `<style scoped>` uses `var(--fallback-thumbnail)` bg, `var(--border-gold-dim)` border, `var(--border-radius-sm)`, `var(--color-gold)`/`var(--color-gold-dim)` for the glyphs. When no label → shows only `✦`.
      Files: `src/shared/components/ThumbnailPlaceholder.vue`
      Verify: `npm run build`; covered by tests in item 13.

- [ ] 8. Retrofit full-page / hero / panel / button / card BACKGROUNDS to `v-bg-asset` across the view-level files that use scoped-CSS or inline background-image (batch; same pattern per file). For each listed background/hero/panel/button/card/field/provider-card/attribution/stat-tile reference: move the URL off the CSS rule (or off the inline `:style`/`bg-[url()]`) onto the element as `v-bg-asset="{ url: '<path>', fallback: '<#color>', size, position }"`, keep the rule's non-image props, and (Type D buttons) ensure an art-independent CSS base (background-color + border/glow via tokens, label text always visible) remains on the element so the button is fully usable with no art. Fallbacks: Type A `#0a0a1a`; Type B/hero-banner `#0d0d2a`; Type C/panel/card/field/provider-card/attribution/stat-tile `#0d0d2a` (+ `var(--border-gold)` for panels/banners); Type D button `transparent` over the CSS base. For dynamic URLs (stat tiles, provider cards, RoleBadge, license cards, citation cards, quick-action cards) bind `:url` to the existing expression. For `AdventureGenerateView.vue`, remove the `bg-[url('...')]` arbitrary class and keep `bg-cover`/positioning utilities. Preserve the `ask-question-button-.png` literal filename.
      Files: `src/assets/main.css` (body app-background → keep as-is OR convert body bg to color-first; see item 9), `src/shared/components/AppSidebar.vue` (side-nav bg), `src/shared/components/AppLayout.vue` (topbar bg), `src/views/HomeView.vue` (hero, welcome-banner, 2 panels, new-campaign button, 4 quick-action cards), `src/features/campaigns/CampaignsView.vue` (hero, 2 panels, continue button), `src/features/characters/CharactersView.vue` (hero, 2 import buttons, side panel, stat tiles), `src/features/characters/components/AbilityScoreTile.vue`, `src/features/characters/components/RoleBadge.vue`, `src/features/library/LibraryView.vue` (hero, licenses button), `src/features/library/components/DocumentList.vue` (side panel), `src/features/library/components/DocumentUploadForm.vue`, `src/features/library/components/RegisterFolderForm.vue` (2 buttons), `src/features/library/components/WatchedFolderSection.vue`, `src/features/ai/AiSettingsView.vue` (page bg, hero, 2 buttons, runtime panel, 4 provider cards), `src/features/ai/RulesLookupView.vue` (page bg, hero banner, panel, ask-question button, citation card), `src/features/ai/components/GameSystemDropdown.vue` (field art), `src/features/adventure-generation/AdventureGenerateView.vue` (page bg, hero, panel, 3 dropdown fields, setting field, generate button), `src/features/about/AboutView.vue` (page bg, hero banner, 2 license cards, 3 panels, attribution badge), `src/features/sessions/JoinSessionView.vue` (page bg, hero banner, not-found panel, qr card), `src/features/sessions/PlayerJoinForm.vue` (join panel, submit button)
      Verify: `npm run build` succeeds (no unused CSS errors; vue-tsc clean) and `npm run test` stays green. Manually sanity-check one view (e.g. HomeView) renders with background color before/after image load.

- [ ] 9. Apply the color-first body background in `main.css`.
      Change the `@layer base body` rule so the solid fallback color is set first (`background-color: #0a0a1a`) and the image is layered via `background-image: url('/assets/ui/app-background.png')` with `background-size: cover`, `background-position: center`, `background-attachment: fixed`, `background-repeat: no-repeat` — so the color shows immediately and remains if the PNG fails. Keep the existing `@apply` utilities. (Body is global, not an element with a directive, so this is the color-first CSS pattern, not `v-bg-asset`.)
      Files: `src/assets/main.css`
      Verify: `npm run build`; load the app and confirm the dark `#0a0a1a` base is present under the background.

- [ ] 10. Replace THUMBNAIL / PORTRAIT / LOGO `<img>` references with `AircaneImg` + `ThumbnailPlaceholder` (batch; same pattern per file).
      - `HomeView.vue` feature cards: swap the 6 `<img class="feature-thumbnail">` for `<AircaneImg :src type="thumbnail">` with a `#fallback` `<ThumbnailPlaceholder :label="feature.title">`.
      - `CampaignList.vue`: swap `<img class="campaign-thumbnail">` for `<AircaneImg :src="placeholderThumbnail(index)" type="thumbnail">` + `<ThumbnailPlaceholder :label="campaign.name">`.
      - `DocumentList.vue`: swap the row thumbnail `<img>` for `<AircaneImg :src="thumbnailFor(doc)" type="thumbnail">` + `<ThumbnailPlaceholder :label="doc.title">`.
      - `CharacterList.vue`: swap the `<img class="portrait">` for `<AircaneImg :src="portraitUrl(character)" type="portrait">` + `<ThumbnailPlaceholder :label="character.name">` (name-initial avatar). Keep the `.portrait` sizing on the AircaneImg container.
      - `AppSidebar.vue` logo: swap `<img ... app-logo-mark.png>` for `<AircaneImg src="/assets/ui/app-logo-mark.png" alt="Aircane Tabletop" type="thumbnail" fallbackText="✦">`.
      Files: `src/views/HomeView.vue`, `src/features/campaigns/components/CampaignList.vue`, `src/features/library/components/DocumentList.vue`, `src/features/characters/components/CharacterList.vue`, `src/shared/components/AppSidebar.vue`
      Verify: `npm run build`; `npm run test` green; mount views and confirm placeholder shows when src is empty.

- [ ] 11. Replace the 8 real nav icons with `NavIcon` in `AppSidebar.vue`; leave Dashboard SVG untouched.
      For the 8 items that currently carry `iconSrc` (`icon-campaign`…`icon-about`), render `<NavIcon :icon="..." :label="item.label">` using the PNG basename (strip the `/assets/ui/` prefix and `.png`). Keep Dashboard's inline SVG (no `iconSrc`). The emoji map in NavIcon covers only real `icon-*.png` items.
      Files: `src/shared/components/AppSidebar.vue`
      Verify: `npm run build`; `npm run test` green; confirm Dashboard still renders its SVG and the other 8 render NavIcon.

- [ ] 12. Add graceful hide-on-error to all decorative ORNAMENT `<img>` elements (batch; same one-line pattern).
      Add `@error="(e)=>((e.target as HTMLElement).style.display='none')"` to each decorative `<img>` so a failed load hides the element (no broken-image icon). Only on `<img>` elements, never non-img. Targets: `HomeView.vue` footer-quote-ornament; `CampaignList.vue` campaign-empty-state-ornament; `CharacterList.vue` character-card-frame + character-empty-state-ornament; `DocumentList.vue` library-empty-state-ornament; `GameSystemDropdown.vue` decorative `icon-dice.png`; `AboutView.vue` 3× legal-scroll-ornament; `InviteCodeField.vue` and `DisplayNameField.vue` friendly-error-ornament.
      Files: `src/views/HomeView.vue`, `src/features/campaigns/components/CampaignList.vue`, `src/features/characters/components/CharacterList.vue`, `src/features/library/components/DocumentList.vue`, `src/features/ai/components/GameSystemDropdown.vue`, `src/features/about/AboutView.vue`, `src/features/sessions/components/InviteCodeField.vue`, `src/features/sessions/components/DisplayNameField.vue`
      Verify: `npm run build`; `npm run test` green.

- [ ] 13. Add the 8 co-located Vitest unit tests (named `.test.ts`, in `__tests__/` next to each unit; import `describe/it/expect/vi` from `vitest`, `mount` from `@vue/test-utils`; stub `global.Image` or dispatch `load`/`error` events to drive both branches deterministically; spy on `console.warn`).
      1. `src/directives/__tests__/vBgAsset.test.ts` — fallback color applied on 404: mount an element with `v-bg-asset`, trigger `img.onerror`, assert `el.style.backgroundColor` equals the fallback and `backgroundImage` is not set; assert `console.warn` called with the `[Aircane] Background asset failed to load:` message.
      2. same file — background-image on load: trigger `img.onload`, assert `el.style.backgroundImage` contains the url and `backgroundSize`/`backgroundPosition` set.
      3. `src/shared/components/__tests__/AircaneImg.test.ts` — fallback slot on src fail: mount with a `#fallback` slot, trigger the img `error`, assert the img is gone and the fallback content renders; assert `console.warn` `[Aircane] Asset failed to load:`.
      4. same file — img shown on successful load: assert `<img>` present with correct `src`/`alt` and `$attrs` forwarded to the img.
      5. `src/shared/components/__tests__/NavIcon.test.ts` — emoji fallback on PNG fail: mount with `icon="icon-campaign"`, trigger img `error`, assert `.fallback` text is `🔖`.
      6. same file — PNG shown when available: assert `<img>` has `src="/assets/ui/icon-campaign.png"` and `alt` = label before any error.
      7. `src/shared/components/__tests__/ThumbnailPlaceholder.test.ts` — initials from label: `label="Shadow Vale"` → renders `SV`.
      8. same file — ornament-only when no label: no `label` → renders `✦` and no initials.
      Files: `src/directives/__tests__/vBgAsset.test.ts`, `src/shared/components/__tests__/AircaneImg.test.ts`, `src/shared/components/__tests__/NavIcon.test.ts`, `src/shared/components/__tests__/ThumbnailPlaceholder.test.ts`
      Verify: `npm run test` — all 8 new tests pass and the full suite stays green.

- [ ] 14. Full verification pass.
      Run the typecheck/build and the whole unit suite; fix any regressions in the retrofitted views (unused scoped-CSS rules, type errors from new props). Confirm no `console.error` is used for asset failures (warn only), no asset code touches the API key, and the `ask-question-button-.png` filename is unchanged.
      Files: n/a (verification)
      Verify: `npm run build` succeeds and `npm run test` reports all suites passing, including the 8 new tests.

---

## Assumptions / gaps noted

- `DocumentStatusBadge.vue` already degrades (CSS color behind PNG; CSS-only error/ocr-required). Left unchanged by design; recorded, not modified.
- The global `.btn-primary`/`.btn-secondary` in `main.css` already have an art-independent `@apply` base, so Type D "CSS button base" for those is already satisfied; the per-view art buttons get a base added as part of item 8.
- `section-divider-ornament.png` appears only inside a CSS comment (usage hint), with no live element — no change needed; recorded for completeness.
- `v-bg-asset` on elements whose background currently lives in **scoped CSS** requires moving the URL to the element; the non-image CSS (size/position/border/radius) stays in the stylesheet. Where a scoped rule becomes empty after the URL moves, remove the now-unused `background-image` line only (keep the rest).
- Tests use the project's existing explicit `vitest` imports even though `globals:true` is set, to match every existing test file.
