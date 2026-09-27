# Open-Content Licensing & Compliance

This document is the compliance reference for the open-content rules text Aircane bundles and
retrieves (the built-in library content), covering CC BY 4.0 and the ORC License. It
applies to anyone sourcing or reviewing built-in content and to the UI that surfaces attribution.

> This is engineering/process guidance, not legal advice. For anything ambiguous, get a real
> legal review before shipping.

> **Note on OGL / Pathfinder 1e:** Aircane no longer ships any OGL v1.0a built-in content. The
> Pathfinder 1e PRD bundle was removed to avoid the OGL's Section 15 attribution-chain and
> content-identification burden ahead of possible commercialization (see `docs/backlog.md`). The
> remaining built-in bundles are CC BY 4.0 (D&D 5e SRD) and ORC (PF2e Remaster). The OGL guidance
> below is retained only as **forward-looking caution** in case OGL content is ever reconsidered;
> it does not describe anything currently shipped. Users may still import their own PF1e PDFs via
> the folder-watching Library.

---

## 1. Separate code from content

The ORC License (and open-content licenses generally) cover **game content**, not functional
software source code. Keep the two cleanly separated:

- **Application code** — license under a standard software license (MIT, Apache 2.0, GPLv3) or
  keep proprietary. Do **not** put code under ORC. (Note: the root `README.md` currently says
  "License TBD" — this needs to be decided and stated.)
- **Bundled content / rules text** — the parsed rules text, mechanics descriptions, and data
  strings live separately under `src/backend/Aircane.Workers/Resources/builtin/<bundle>/` as
  Markdown plus a per-bundle license file and `manifest.json`. This isolated content is what
  carries the license notice and operates under the content-license framework.

Aircane already follows this split: content is embedded resources under `Resources/builtin/`,
each bundle ships its own license file (`LICENSE-CC-BY-4.0.txt`, `LICENSE-ORC.txt`), and the
code is a separate concern.

---

## 2. ORC License requirements (for the PF2e Remaster bundle)

To legally use and distribute ORC content, satisfy all of the following:

- **ORC Notice** — include a visible, human-readable copy of the ORC Notice in the repository
  (the bundle's `LICENSE-ORC.txt`) **and** surface it prominently in the app's "About" / "Credits"
  UI. (Status: `LICENSE-ORC.txt` exists in the `pf2e_remaster` bundle; there is no dedicated
  About/Credits UI panel yet — the library "Open Content Licenses" modal is the closest surface.
  See open items below.)
- **Upstream attribution** — copy the exact upstream attribution text from the specific Paizo
  products the data is pulled from (e.g. *Pathfinder Player Core*, *Pathfinder GM Core*) into the
  notice. The bundle `manifest.json` `attributionText` and the ORC notice must reflect the actual
  products used.
- **Downstream declaration** — explicitly state what in Aircane is ORC Content versus Reserved
  Material. For example:
  - **ORC Content:** the game-mechanics rules text stored in the built-in content files.
  - **Reserved Material:** the application source code, UI design, visual assets, and brand/logo.

---

## 3. Compliance traps for software developers

- **Product Identity is Reserved Material — never ingest it.** Do not let content sourcing pull in
  Paizo lore, deity names, unique setting factions (e.g. Pathfinder Society), or iconic character
  names (e.g. Amiri, Merisiel). Use system-agnostic equivalents where needed (reference a deity by
  domain/trait, not by name). A bundle that needs Product Identity exclusions should record them in
  its own manifest before real content lands (the PF2e bundle must do this).
- **OGL vs. ORC split — do not mix them in one notice (forward-looking).** Aircane ships no OGL
  content today. If OGL v1.0a content is ever reconsidered, keep in mind that some PF2e-era content
  (e.g. *Secrets of Magic*, *Guns & Gears*, *Rage of Elements*) was published under **OGL v1.0a**,
  not ORC. OGL and ORC content must then be kept in separate bundles, each with its own
  cleanly-demarcated legal notice (with the OGL bundle carrying its own `OGL-1.0a.txt` +
  `SECTION-15.txt`). Aircane's per-bundle model supports this. Do not merge OGL-sourced text into
  the ORC bundle or vice versa.
- **Trademark / trade dress — separate from content license.** Do not use Paizo's official fonts,
  trade dress, or logos in the UI without a separate agreement. That's trademark law, not covered
  by ORC, and is prohibited. Keep Aircane's own visual identity distinct.

---

## 4. Per-bundle license summary

| Bundle | License | Notice file(s) | Notes |
|--------|---------|----------------|-------|
| `dnd5e_srd` | CC BY 4.0 | `LICENSE-CC-BY-4.0.txt` | Attribution required; SRD 5.1 by Wizards of the Coast. |
| `pf2e_remaster` | ORC | `LICENSE-ORC.txt` | Content is a placeholder pending real ORC-licensed text (see backlog). Must carry ORC Notice + Paizo product attribution + PI exclusions when populated. |

> The `pf1e_prd` (OGL v1.0a) bundle was **removed** ahead of possible commercialization; Aircane no
> longer ships OGL content.

---

## 5. Open compliance items

These are the concrete gaps to close for full compliance (tracked here so they aren't lost):

1. **Decide and state the application code license** — `README.md` says "License TBD". Pick a
   software license (or proprietary) and state it, keeping it distinct from the content licenses.
   *(Still open — this is a maintainer decision. The About/Credits page points users to the README
   for it.)*
2. ~~**About / Credits UI panel**~~ — ✅ **Done.** An in-app About & Credits page
   (`features/about/AboutView.vue`, route `/about`, sidebar link) prominently displays the ORC
   Notice / attributions (CC BY, ORC) sourced from the public `/api/library/licenses` API,
   alongside the library "Open Content Licenses" modal.
3. ~~**Downstream declaration**~~ — ✅ **Done.** The About/Credits page includes an explicit ORC
   Content vs. Reserved Material declaration.
4. **PF2e content** — source real ORC-licensed PF2e Remaster text (NOT the Foundry VTT data, which
   is Paizo Community Use / partnership, not ORC — see the backlog item), add
   `productIdentityExclusions` to the `pf2e_remaster` manifest, and copy the exact Paizo product
   attribution into its notice.
