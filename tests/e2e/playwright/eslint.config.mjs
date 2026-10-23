// ESLint config for the Playwright specs. The rules live in the frontend's config
// (src/frontend/aircane-web/eslint.config.mjs); ESLint can only lint files under the folder of the config it uses, so
// the specs need a config file of their own, and this just re-exports the frontend's. Run it with `npm run lint` in
// src/frontend/aircane-web. This folder has no complete install, so ESLint and its plugins come from there.
export { e2eConfig as default } from '../../../src/frontend/aircane-web/eslint.config.mjs'
