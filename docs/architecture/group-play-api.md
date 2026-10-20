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

### Coming back: the rejoin secret

A player's bearer token lives in the tab (`sessionStorage`), so closing the tab loses it. To come back to the same
seat, the browser makes a **rejoin secret** once per session (at least 128 random bits, base64url, kept in
`localStorage`) and sends it with the join request as `rejoinSecret`. The server stores only its SHA-256, compares it
in constant time, and never logs or returns it. A secret that is sent but malformed is a 400; joining again with a
secret that already holds a seat in the session is a 409 (rejoin instead).

**A display name and the invite code never take an existing seat.** Without the secret, joining again is a new
request, as before.

`POST /api/sessions/{id}/rejoin` with `{ "secret": "..." }`. Anonymous, and rate limited **in every mode, the LAN
included** (`RateLimiting:Rejoin`, 30 a minute per address; on the LAN the socket address, not a forwarding header).

| Result | When |
| --- | --- |
| 200 + `{ participantId, displayName, role, isApproved, status, characterId, sessionName, participantToken }` | the secret holds a seat, approved (`status: "approved"`) or still waiting (`"pending"`) |
| 403, `code: participant-denied` | the host declined that request: the usual refusal, and no token |
| 404, one body for all | an unknown or malformed secret, an ended session, a session that does not exist |

The participant is the same one: approval, character and turn position are unchanged, no new request is made and
nothing is broadcast. The token is fresh and **the previous one is revoked**, so a seat has one live login (an older
tab of the same browser has to come back the same way).

`POST /api/sessions/{id}/rejoin-status` with `{ "secret": "..." }`: the same access, limit and 404, but it mints no
token. 200 + `{ sessionName, displayName, status }`, where `status` is `approved`, `pending` or `denied`. The sessions
page uses it to show its "Rejoin" cards before anyone is logged in.

The secret stops working when the session **ends** (every hash is cleared) and when the seat is **replaced**. On a
**denied** request it can never mint a token; its hash is kept only so the browser is told it was declined and can
drop it. A new join from that browser with the same secret is a new request, and the secret moves to it.

### Same name, and giving a seat to a new request

A pending request whose display name matches (ignoring case) another participant already in the session carries
`sameNameAsParticipantId` in the host's `GET /api/sessions/{id}/participants` and in the `ParticipantJoined` event.
Players never see it.

`POST /api/sessions/{id}/replace-participant` with `{ "oldParticipantId": "...", "newParticipantId": "..." }`. Host
only, same pattern and rate limit as `approve-participant`. The new participant is approved and takes the old one's
character and place in every running group adventure's turn order (under the run's turn lock). If the old one held the
turn, the new one holds it, and a pending roll is cleared, as when the turn passes (the AI asks again). The old
participant leaves the roster (`ReplacedByParticipantId`), its token is revoked, its secret cleared and its
connections closed. The group hears `ParticipantApproved` (new), `ParticipantLeft` (old) and `AdventureStateChanged`
(reason `ParticipantReplaced`) per changed run.

| Result | When |
| --- | --- |
| 200 + `{ participant, replacedParticipantId, adventureRunIds, tookTheTurn }` | the seat moved |
| 400 | an id missing |
| 403 | the caller is not the host |
| 404 | either participant is not in this session |
| 409 | the new participant is already approved, denied or replaced; the old one has no seat (pending, denied, replaced); the same id twice; the session has ended |

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

## The live channel (SignalR, `/hubs/session`)

### Who is connected, and to what

The caller is whoever the connection's token says: session (`sid`), participant (`pid`), role and display name.
Nothing a client puts in a message changes that. With no token, a direct request from the host machine is the host
(the same trust rule as the REST API); anything else without a token cannot connect. An expired or revoked token
cannot connect either.

| Group | Who is in it |
| --- | --- |
| `session:{id}` (the table) | approved participants and the host |
| `session:{id}:host` | the host only |
| `session:{id}:participant:{pid}` | every connection of that participant, approved or not |

- A connection with a participant token is put in its groups as soon as it opens, so an automatic reconnect is back
  where it was before the client does anything. `JoinSession` does the same on request, and is how the host's own
  machine (no token) picks the session it is hosting. Its message's `sessionId` must be the token's session; the
  participant id, name and role in it are ignored. It is refused for another session, an ended session, someone not
  on the roster and someone denied.
- A participant still waiting for approval is in their own group only: they hear their own approval or denial and
  nothing of the table, and cannot chat.
- **Approval:** the server adds the participant's open connections to the table's group, then announces the approval
  there (so they hear it too).
- **Denial:** the host and the person denied are told, then the person's connections leave every group and are closed.
  Their token is revoked, so a reconnect is refused.
- **End of session:** every connection to it is closed; all of its tokens are revoked.
- **Chat:** the sender is the token's participant and name (the host's own messages carry the id the host's client
  chose, so it can recognise them). Only the table may chat. `LeaveSession` announces the token's identity.

### Who hears which event

| Event | Audience | Notes |
| --- | --- | --- |
| `ChatMessageReceived` | table | |
| `RollRecorded` | table if public; host + the roller if private | |
| `RollRequested` | table | not sent by anything yet |
| `AINarrationStarted` / `Chunk` / `Completed` | table | narration is what the table is told; not sent by anything yet |
| `SceneChanged` | table | public description only; not sent by anything yet |
| `HandoutRevealed` | table | revealed on purpose; not sent by anything yet |
| `CombatTurnChanged` | table | whose turn and the round |
| `AdventureSceneAdvanced` | table | transition narration |
| `TurnAdvanced`, `ParticipantJoinedAdventure` | table | |
| `AdventureStateChanged` | table | a signal only; also sent when a run completes |
| `ParticipantApproved` | table | the approved person is in it by then |
| `ParticipantLeft` | table | also sent for the old participant when the host replaces a seat |
| `ParticipantJoined` | host | a new join request, before anyone has decided on it |
| `ParticipantDenied` | host + the person denied | |
| `AIProposalCreated` | host | |
| `StateUpdated` | host | the whole campaign state, NPC sheets included; not sent by anything yet |
| `ImportStatusUpdated` | host | library imports; not sent by anything yet (imports use the library hub) |
| `AdventureClueRevealed`, `AdventureSecretRevealed`, `AdventureNpcUpdated` | host | DM-facing text; the table hears the story through the narration |
| `AdventureCompleted` | host | the outcome notes; the table gets `AdventureStateChanged` (`Completed`) |

None of these carries an invite code or a token. `AdventureStateChanged` is a signal: clients read
`GET /api/sessions/{id}/adventure-run`, where the player-safe filtering lives. Approval is announced once: approving
someone who is already approved sends nothing.

## Passing the turn

`POST /api/adventure-sessions/{runId}/advance-turn[?fromParticipantId=]` moves a group run to the next participant
(wrapping to the first starts a new round). It clears the pending roll (it was owed by the player whose turn just
ended), sends the existing `AdventureStateChanged` signal (reason `TurnAdvanced`) and `TurnAdvanced` to the table, and
records who passed it: the event log entry and `TurnAdvanced.passedBy` say `Host`, `Player` or `Auto`.

| Caller | Result |
| --- | --- |
| The host | any turn; with `fromParticipantId` only if that participant still holds it (409 otherwise). Returns the full run state |
| The acting player (approved, not denied, `sid` and `pid` match) | their own turn only. Returns the table view (`adventure-run`) |
| Any other player | 403 "Not your turn", naming who has it |
| A waiting or denied participant, a token for another session | 403 (the usual refusal for that case) |

**A request that arrives after the turn already moved on** never moves it again. The check ("is the turn still where
the caller thinks it is?") and the move are made together under a lock on the run, so two requests for the same turn,
a double click, or a player and the host at once, move it once. The late one changes nothing: a player gets 403
"Not your turn. Waiting for <who has it now>"; a host who sent `fromParticipantId` gets 409. A host who sends no
`fromParticipantId` passes whoever holds the turn, as before.

### Optional auto-advance

`POST /api/adventure-sessions/start-group` takes an optional `turnAdvance`: `"ManualOnly"` (the default: the acting
player ends their turn, or the host passes it) or `"AfterEachAction"`. The run state and `adventure-run` report it as
`turnAdvance`. Ignored when the call resumes a run that is already going.

With `AfterEachAction`, once the AI has answered the acting player's action the turn moves to the next player
(`passedBy: "Auto"`), except:

- **The AI call failed:** nothing moved. The error goes back to the player, the turn is still theirs, and they can try again.
- **The AI asked for a roll** (a pending roll is set): the turn stays. The player's next message reports the result on
  the same turn, and that answer moves it on.
- **The turn already moved** (the host passed it while the AI was answering), or the run ended (the AI completed the
  adventure): nothing more happens.
- The action's narration is saved by the AI step itself, best effort (a failure to save the transcript is logged and
  does not fail the action), so the turn moves when the AI answered, not when the transcript write succeeded.

## Chat history

Chat is broadcast live (`ChatMessageReceived`) and also stored per play session, so late joiners and reloaded clients
can catch up.

`GET /api/sessions/{sessionId}/chat[?limit=100&before=<messageId>]` returns `{ messages, hasMore }`, oldest first. The
default `limit` is 100 and the most is 200. Pass the id of the first message you have as `before` to page back while
`hasMore` is true. Each message has `id`, `senderId`, `senderName`, `senderRole`, `text` and `sentAt`; the live event
carries the same `messageId`, so a client can tell which it already has.

- **Who:** the host and approved, non-denied participants of this session. A waiting participant gets 403 "Waiting for
  approval", a denied one 403 with `code: "participant-denied"`, a token for another session 403.
- **Who sent it:** the token's participant, display name and role; nothing in the message decides it. The host has no
  participant record, so its messages carry the id its client chose.
- **Nothing hidden:** there are no private or host-only messages. Every stored message is something every approved
  participant was shown live, and the history is the same for everyone allowed in.
- **Limits:** at most 500 characters per message; at most 10 messages per sender in 10 seconds (the hub refuses the
  rest); waiting participants cannot send.
- **Retention:** a session keeps its newest 1000 messages; older ones are deleted as new ones arrive, so the table is
  bounded however long the game runs. There is no timed expiry: an ended session keeps its (at most 1000) messages, as it
  keeps the rest of its record. Nothing deletes sessions today (deleting a campaign leaves its sessions); if one is ever
  deleted, its chat goes with it.

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
| `POST .../advance-turn` | **only the acting player, on their own turn** | see "Passing the turn" below; any other Player gets 403 |
| `POST .../participants`, `start`, `start-group`, `GET in-progress` | **403** | Host only, as before |
| `POST /api/sessions/{id}/ai/player-action` | seat required; 409 during a group run | when it does answer, `privateDmNote` is removed for a Player |

A **solo** run has no table to protect, so its one Player keeps full access (still needing a seat).
Adventure content (`/api/adventures`), campaigns, combat, proposals and campaign state are Host or DM-only by policy.

Live events follow the same line: see "Who hears which event" above.
