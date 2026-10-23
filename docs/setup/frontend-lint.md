# Frontend Lint

`npm run lint` (in `src/frontend/aircane-web`) runs ESLint over the frontend and the Playwright specs, and fails on
any error. Frontend CI runs it as its own required step, before the type-check and the build.

```bash
cd src/frontend/aircane-web
npm run lint        # ESLint 9; fails on errors
npm run lint:fix    # the same, applying the autofixes ESLint offers (check the diff)
```

It takes about 15 seconds. Lint is for real defects (unused variables, duplicate keys, a missing `v-for` key, `any`).
Types are the job of [`npm run typecheck`](./frontend-typecheck.md), which is why none of the rules are type-aware.

## What it checks

- **Rules:** `eslint:recommended`, `typescript-eslint` recommended, and `eslint-plugin-vue`'s flat recommended set.
  `.vue` files use the Vue parser, with the TypeScript parser inside `<script lang="ts">`.
- **Files:** `src/` (with the vitest specs), `scripts/`, the config files, and the specs in `tests/e2e/playwright`.
  Ignored: `dist`, `node_modules`, `public` (vendored assets), `art-source`, `coverage`, Playwright's output, and
  generated files (`*.generated.ts`, such as `manifest.generated.ts`).
- **Globals:** browser for `src`, Vitest's for the specs, Node for `scripts/*.mjs` and the config files, and Node plus
  browser for the e2e specs.

## Two configs

`eslint.config.mjs` (in `src/frontend/aircane-web`) holds the rules. ESLint only lints files under the folder of the
config it uses, so the e2e specs have a one-line `tests/e2e/playwright/eslint.config.mjs` that re-exports the
frontend's e2e config, and `npm run lint` runs both. The e2e folder has no complete install, so ESLint and its plugins
come from the frontend's `node_modules`.

## Decisions

Each is commented where it is set, in `eslint.config.mjs`:

- **Formatting is not enforced.** There is no formatter in the project, and the first run produced about 3,100
  warnings in 99 files from `eslint-plugin-vue`'s formatting rules (indentation, attributes per line, line breaks,
  self-closing tags). Every rule of type `layout` in that plugin is off.
- `vue/attributes-order` is off: it is style, and its autofix reorders attributes, which can change the precedence
  of `v-bind="object"`.
- `vue/one-component-per-file` is off in specs, which define several small stub components in one file.
- An unused variable or parameter that starts with an underscore is allowed (`_message`), as `noUnusedParameters`
  in `tsconfig.app.json` already allows it.

## Suppressing a finding

Fix the cause where you can. Where you cannot, use `// eslint-disable-next-line <rule> -- <reason>` on the one line,
never a file-wide disable. There are none in the frontend today.

## Installing dependencies

Frontend CI, the production Docker image and the desktop build install with **`npm ci`**, which installs exactly what
`package-lock.json` says and fails if the lockfile and `package.json` disagree. Do the same for a clean, reproducible
install:

```bash
cd src/frontend/aircane-web
npm ci
```

Use `npm install <package>` only when you change a dependency, and commit the updated `package-lock.json` with it.
