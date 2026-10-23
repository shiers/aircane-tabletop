/**
 * ESLint flat config for the frontend (ESLint 9, typescript-eslint, eslint-plugin-vue).
 *
 * `npm run lint` lints this folder with this file, and the Playwright specs in tests/e2e/playwright with that
 * folder's own eslint.config.mjs, which re-exports `e2eConfig` below (ESLint only lints files under the folder that
 * holds the config it uses). No type-aware rules: `npm run typecheck` is the type gate, and they would make lint slow.
 *
 * What lint is for here: real defects (unused variables, duplicate keys, bad v-for/v-model, `any`). Formatting is
 * not enforced: the project has no formatter, and re-indenting ~100 templates is out of scope (see `rules` below).
 */
import js from '@eslint/js'
import globals from 'globals'
import tseslint from 'typescript-eslint'
import pluginVue from 'eslint-plugin-vue'
import { defineConfig, globalIgnores } from 'eslint/config'

/** Vitest's globals (`vite.config.ts` sets `test.globals`), for the specs. */
const vitestGlobals = Object.fromEntries(
  ['describe', 'it', 'test', 'expect', 'vi', 'beforeEach', 'afterEach', 'beforeAll', 'afterAll'].map((name) => [name, 'readonly']),
)

/**
 * Every eslint-plugin-vue rule of type "layout" (whitespace, line breaks, quotes, self-closing tags...) is off.
 * Its recommended set switches 12 of them on as warnings, and on the first run they produced ~3,100 warnings in
 * 99 files, all of them style that a formatter would own. Derived from the plugin's own rule metadata, not a list.
 */
const vueLayoutRulesOff = Object.fromEntries(
  Object.entries(pluginVue.rules)
    .filter(([, rule]) => rule.meta?.type === 'layout')
    .map(([name]) => [`vue/${name}`, 'off']),
)

/** The parts every folder shares: ignores, the recommended sets, the Vue + TypeScript parser, rule decisions. */
export const shared = defineConfig([
  globalIgnores([
    '**/dist/**',
    '**/node_modules/**',
    '**/public/**', // vendored assets (public/desktop/qrcode.min.js)
    '**/art-source/**',
    '**/coverage/**',
    '**/playwright-report/**',
    '**/test-results/**',
    '**/*.generated.ts', // src/features/library/thumbnails/manifest.generated.ts
  ]),
  {
    name: 'aircane/recommended',
    files: ['**/*.{js,mjs,cjs,ts,tsx,mts,cts,vue}'],
    extends: [js.configs.recommended, tseslint.configs.recommended, pluginVue.configs['flat/recommended']],
  },
  {
    // <script lang="ts"> in .vue files is parsed with the TypeScript parser (vue-eslint-parser hands it over).
    name: 'aircane/vue-typescript',
    files: ['**/*.vue'],
    languageOptions: { parserOptions: { parser: tseslint.parser } },
  },
  {
    name: 'aircane/rules',
    rules: {
      ...vueLayoutRulesOff,
      // Style, and its autofix reorders attributes, which can change the precedence of `v-bind="object"`.
      'vue/attributes-order': 'off',
      // An underscore marks a parameter that is deliberately unused, as `noUnusedParameters` in tsconfig already
      // allows (e.g. `vi.fn((_message: string) => false)`, which exists to type the mock's calls).
      '@typescript-eslint/no-unused-vars': [
        'error',
        { argsIgnorePattern: '^_', varsIgnorePattern: '^_', caughtErrorsIgnorePattern: '^_', destructuredArrayIgnorePattern: '^_' },
      ],
    },
  },
])

/**
 * The config for the Playwright specs (tests/e2e/playwright/eslint.config.mjs re-exports it): the shared parts, plus
 * Node globals and browser globals for the callbacks passed to `page.evaluate`.
 */
export const e2eConfig = defineConfig([
  ...shared,
  { name: 'aircane/e2e', files: ['**/*.{ts,mjs,js}'], languageOptions: { globals: { ...globals.node, ...globals.browser } } },
])

export default defineConfig([
  ...shared,
  { name: 'aircane/app-globals', files: ['src/**/*.{ts,tsx,vue}'], languageOptions: { globals: globals.browser } },
  {
    name: 'aircane/specs',
    files: ['**/__tests__/**', '**/*.{test,spec}.{ts,mjs,js}'],
    languageOptions: { globals: { ...globals.browser, ...globals.node, ...vitestGlobals } },
    rules: {
      // Specs define several tiny stub components in one file.
      'vue/one-component-per-file': 'off',
    },
  },
  { name: 'aircane/node', files: ['scripts/**/*.{js,mjs}', '*.config.{js,mjs,ts}'], languageOptions: { globals: globals.node } },
])
