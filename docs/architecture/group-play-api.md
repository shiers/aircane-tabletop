# Group play: lobby and adventure-run API

The backend half of the group lobby: who gets into a session, how the host lets them in or turns them away, which
play session a group adventure runs in, and what players can read while it runs. Callers are the host (the trusted
local host, or a Host token) and participants (a Player token issued when they joined).

## Joining and approval

`POST /api/campaigns/{campaignId}/start-session` takes `requireHostApproval` (default `true`). The setting is stored
on the session.

| `requireHostApproval` | A new joiner is... | The join response |
| --- | --- | --- |
| `true` | pending until the host approves | `isApproved: false` |
| `false` | approved on join | `isApproved: true` |

Sessions that existed before the setting was added keep `true` (the migration backfills it), which is how they behaved.

**Reconnecting never needs approval.** `POST /api/sessions/{id}/reconnect` succeeds for a pending participant, and its
response now includes `isApproved` so a reloaded waiting page knows where it stands. A pending participant is still
not allowed to act: the adventure-run read below refuses them (403).

### Denying a join request

`POST /api/sessions/{id}/deny-participant` with `{ "participantId": "..." }`. Host only, same pattern and rate limit
as `approve-participant`.

| Result | When |
| --- | --- |
| 200 + `ParticipantDto` (`isDenied: true`) | the request was denied, or was already denied (idempotent) |
| 400 | `participantId` missing |
| 403 | the caller is not the host |
| 404 | no such participant in this session |
| 409 | the participant is already approved (only a pending request can be denied) |

A denial **marks** the participant (`IsDenied`) rather than deleting the row, so the decision is recorded and a
denied participant cannot be approved by a stale button. They disappear from `GET /api/sessions/{id}/participants`.
Their token is revoked through the persistent revocation store, so it stops working on every request, including after
a restart. A request made with it is answered **403** with a `application/problem+json` body whose `code` is
`participant-denied` and whose title is "Join request declined", not a bare 401 that looks like an expired login.

A denied person **may join again with the same invite code**. That is a new request with a new participant id and
token; the host sees it as a new pending entry and can deny it again. The invite code is shared by everyone, so
blocking one person by name is not meaningful, and a mistaken denial is recoverable.

## Starting a group run in a specific play session

`POST /api/adventure-sessions/start-group` accepts an optional `playSessionId`:

```json
{ "generatedAdventureId": "...", "campaignId": "...", "participantIds": ["..."], "playSessionId": "..." }
```

- Absent: unchanged. The run belongs to the campaign's most recent play session that has not ended.
- Present: the run belongs to that session. 404 if it does not exist; 400 if it belongs to a different campaign or has
  ended (the message says which). Every id in `participantIds` must be an approved participant of *that* session.
- If the campaign already has this adventure running as a group in a *different* play session, the response is 409
  rather than silently returning that run, whose players are not the ones the host named. Naming the session the run
  is really in is the usual idempotent resume (200).

## Hub events

All events go to the session's group (`session:{id}`). None carries an invite code, a token, or adventure text.

| Event | Sent when | Payload |
| --- | --- | --- |
| `ParticipantJoined` | someone joins | the participant (`ParticipantDto`) |
| `ParticipantApproved` | the host approves, or a join was auto-approved | `sessionId`, `participantId`, `displayName`, `autoApproved` |
| `ParticipantDenied` | the host denies a request | `sessionId`, `participantId`, `displayName` |
| `AdventureStateChanged` | a turn advances, a scene advances, or a player acts | `sessionId` (the adventure run id), `reason` (`TurnAdvanced`, `SceneAdvanced`, `PlayerAction`), `changedAt` |

`AdventureStateChanged` is a signal only. Clients respond by reading `GET /api/sessions/{id}/adventure-run`, which is
where the player-safe filtering lives, so no event can leak what a player may not see. Approval is announced once:
approving someone who is already approved sends nothing.

## `GET /api/sessions/{sessionId}/adventure-run`

The adventure in progress in a play session, as a Player may see it. Needs no run id or campaign id.

- **200** the view below, with `Cache-Control: no-store`.
- **403** the caller is not the host or an approved participant of *this* session (another session's token, or a
  participant still waiting for approval).
- **404** the session has no adventure in progress.
- **401** no valid token (from the LAN; the trusted local host needs none).

Included: run id, adventure title, status, whether it is a group run; the current scene's **title and read-aloud
text**; scene number and total scenes; the turn order (names, who is active), the active participant, the round; a
pending roll (label and DC) **only for the participant whose turn it is**; and the last 20 saved exchanges (player
actions and the narration given to the table), oldest first.

Excluded: the scene's `description` (a synopsis of what happens, which can spoil it), DM notes, secrets and handouts,
NPCs, encounters and treasure, exits and the other scenes (so nothing about what comes next), objectives, the AI's
private DM note, and internal ids of scenes.

The full run state (`GET /api/adventure-sessions/{id}`) remains a host tool.

## Who may read or act in a run

A Player token is a **seat** only while the host has approved the participant and not denied them. Every
Player-visible adventure read and action checks that against the roster on each request (`PlayerSeat`), not just the
token's signature:

| Caller | Result |
| --- | --- |
| Host (Host token, or the trusted local host) | everything |
| Player with an approved seat at the run's play session | what the table below allows |
| Participant still waiting for approval | 403 `Waiting for approval` |
| Participant the host denied | 403 with `code: "participant-denied"` (their token is also revoked, which gives the same refusal) |
| Token for another session, no participant record, or a role other than Player | 403 |

What an approved Player may do with a **group** run:

| Endpoint | Player | Why |
| --- | --- | --- |
| `GET /api/sessions/{id}/adventure-run` | yes | the player-safe view, above |
| `POST /api/adventure-sessions/{runId}/player-action` | yes, on their turn | the response is the narration only (no run state, no action outcomes, no private DM note) |
| `GET /api/adventure-sessions/{runId}` (full state) | **403** | scene synopsis, objectives, exits, NPCs, revealed secrets, encounter outcomes |
| `GET .../history` | **403** | 50 entries and scene ids, where `adventure-run` has the latest 20 |
| `POST .../recap` | **403** | the AI is given the scene synopsis and objectives, and can repeat them |
| `GET .../available-scenes` | **403** | names and descriptions of the scenes that come next |
| `GET /api/adventure-sessions/active?campaignId=` | **403** | run ids and outcome notes |
| `POST .../resume`, `advance-scene`, `abandon` | **403** | the course of the game is the Host's |
| `POST .../advance-turn`, `.../participants`, `start`, `start-group`, `GET in-progress` | **403** | Host only, as before |
| `POST /api/sessions/{id}/ai/player-action` | seat required; 409 during a group run | when it does answer, `privateDmNote` is removed for a Player |

A **solo** run has no table to protect, so its one Player keeps full access (still needing a seat).
Adventure content (`/api/adventures`), campaigns, combat, proposals and campaign state are Host or DM-only by policy.

Events sent to the session's SignalR group (`AdventureSceneAdvanced`, `AdventureClueRevealed` and so on) reach every
connection in the group. They carry only what has been revealed to the table.
