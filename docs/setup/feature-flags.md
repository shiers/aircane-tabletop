# Feature Flags

A feature flag switches a built feature off without deleting it. A flag is read once, when the frontend is built or
the dev server starts, from an environment variable. Every flag is **off** unless its variable is set to `true`
(`1`, `yes` and `on` also work, in any case). They are defined in
[`src/frontend/aircane-web/src/shared/features.ts`](../../src/frontend/aircane-web/src/shared/features.ts), and
[`.env.example`](../../src/frontend/aircane-web/.env.example) lists them.

| Flag                  | Environment variable      | Default | What it controls                                                                                          |
|-----------------------|---------------------------|---------|-----------------------------------------------------------------------------------------------------------|
| `features.characters` | `VITE_ENABLE_CHARACTERS`  | off     | The Characters UI: character management, creation and import, and assigning characters to players.        |

## Turning a flag on

Vite reads `.env` files from `src/frontend/aircane-web/`. Put the variable in `.env.local` (it is not committed):

```
VITE_ENABLE_CHARACTERS=true
```

Then restart `npm run dev`, or rebuild. Anything that builds the frontend picks it up: `npm run build`, `build.ps1` /
`build.sh` and the desktop build (`desktop/build.ps1` / `desktop/build.sh`) all run `npm run build`, so set the
variable in the environment of that command. For the Docker dev frontend, add it under the `frontend` service's
`environment:` in `docker-compose.override.yml`.

The flag is fixed at build time: a built app cannot switch it on or off.

## Characters

Character management and creation is a stretch goal ([backlog](../backlog.md#characters-and-character-creation)).
Aircane already works without it: group and solo adventures are played with players who have no character
assigned, and the AI treats character sheets as external.

With the flag **off**:

- The sidebar has no Characters item, and the dashboard has no Character Sheets card or Create Character action.
- `/characters` and `/characters/:characterId/field-review` redirect to the dashboard.
- The host's Participants panel has no Character column or assign dropdown, the group lobby does not show who plays
  which character, and the confirmations for removing a player or replacing a seat do not mention characters.
- A player's Party tab lists the party members and marks whose turn it is. It has no Character section and does not
  wait for a character assignment.

Nothing is deleted. The character code, routes, backend endpoints and database rows are all still there, so with
the flag **on** the application behaves exactly as it did before the flag existed.

## Tests

`npm test` runs with the Characters flag on (see `test.env` in `vite.config.ts`), so the character tests run as
before. The tests of the flag-off behaviour set `features.characters = false` themselves. For the Playwright
end-to-end tests, run with `VITE_ENABLE_CHARACTERS=true` to include the character specs; without it they are
skipped and the navigation spec checks that the Characters entry points are gone.
