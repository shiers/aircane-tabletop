# Frontend Type-Check

`npm run typecheck` (in `src/frontend/aircane-web`) type-checks everything TypeScript sees in the frontend, and
`npm run build` runs it before `vite build`, so a type error fails the build. Frontend CI runs it as its own required
step before the build.

```bash
cd src/frontend/aircane-web
npm run typecheck
```

It takes about 25 seconds from a cold start and about 10 seconds when nothing changed (TypeScript keeps its cache in
`node_modules/.tmp`).

## What it covers

`tsconfig.json` is a solution-style root: it has `"files": []` and only **references** the projects below.
`npm run typecheck` runs `vue-tsc -b`, which builds every referenced project.

| Project | File | Covers |
|---|---|---|
| App | `tsconfig.app.json` | everything under `src/`: the app, every `.vue` file, and the vitest specs (`src/**/__tests__`) |
| Vite config | `tsconfig.node.json` | `vite.config.ts` |
| End-to-end | `tests/e2e/playwright/tsconfig.json` | the Playwright specs and `playwright.config.ts` |

The e2e folder has no complete install of its own, and CI does not install it, so its tsconfig resolves
`@playwright/test` and `@types/node` from the frontend's `node_modules`.

A new vitest spec under `src/**/__tests__/` is covered automatically. A TypeScript file outside those folders needs
an `include` in one of the projects, or a new project listed in `references` in `tsconfig.json`.

## Why not plain `vue-tsc`?

`vue-tsc` with no arguments reads `tsconfig.json`, finds no files and no references it follows, and passes while
checking nothing. That is how type errors piled up unseen, and how a stray `}` in a `.vue` file once got as far as
Vite. `vue-tsc -b` follows the references, and the guard below stops the references from being lost again.

## The coverage guard

`npm run typecheck` first runs `scripts/check-typecheck-coverage.mjs`. It asks `vue-tsc --listFilesOnly` which files
each referenced project contains (the list the checker really uses, `.vue` files included) and fails unless it
includes `src/main.ts`, a `.vue` file, a vitest spec, `vite.config.ts` and a Playwright spec. It also fails if
`tsconfig.json` references nothing. If it fails, fix an `include`, or put the missing project back in `references`.
Its own tests are in `scripts/check-typecheck-coverage.test.mjs`.

## Suppressing an error

Fix the cause: add or correct a type, narrow with a real check, or give a test mock the shape the real code has.
Avoid `any`, blanket `!` assertions, and casts through `unknown`. Where there really is no clean fix, use
`// @ts-expect-error` with a one-line reason, which also fails the check when the error goes away. There are no
suppressions in the frontend today.

## Where it runs

- **Frontend CI** (`.github/workflows/frontend.yml`): a "Type check" step before "Build", with no `continue-on-error`.
- **`npm run build`**, and so `build.ps1`, `build.sh` and the desktop build, all of which run it from a full checkout.
- **Not in `Dockerfile.production`.** The image builds the frontend from a copy of `src/frontend/aircane-web` alone,
  where the e2e specs do not exist, so it runs `npx vite build` and relies on CI for the check.
- `npm run dev` is unaffected.
