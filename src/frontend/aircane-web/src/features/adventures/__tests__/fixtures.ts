import type {
  AdventureNpcStateDto,
  AdventureSessionStateDto,
  AdventureSceneDto,
} from '../api'

export const RUN_ID = 'run-1'
export const PLAY_SESSION_ID = 'play-session-1'

export function makeNpc(overrides: Partial<AdventureNpcStateDto> = {}): AdventureNpcStateDto {
  return {
    npcId: 'npc-guard-captain',
    npcName: 'Guard Captain',
    role: 'villain',
    hasBeenMet: false,
    isAlive: true,
    revealedInfoIds: [],
    playerNotes: null,
    ...overrides,
  }
}

export function makeScene(overrides: Partial<AdventureSceneDto> = {}): AdventureSceneDto {
  return {
    sceneId: 'scene-2',
    title: 'The Sunken Library',
    description: 'Flooded shelves rise out of black water.',
    sceneType: 'exploration',
    readAloudText: 'Cold water laps at your boots.',
    objectives: ['Find the missing tome', 'Deal with the guard'],
    exits: [
      { sceneId: 'scene-3', title: 'The Grand Hall', sceneType: 'combat' },
      { sceneId: 'scene-4', title: 'The Hidden Passage', sceneType: 'roleplay' },
    ],
    npcs: [makeNpc(), makeNpc({ npcId: 'npc-archivist', npcName: 'Archivist', role: 'ally', hasBeenMet: true })],
    ...overrides,
  }
}

/** "Scene 2 of 7", two NPCs here, no clues yet. */
export function makeState(overrides: Partial<AdventureSessionStateDto> = {}): AdventureSessionStateDto {
  const currentScene = overrides.currentScene ?? makeScene()
  return {
    id: RUN_ID,
    generatedAdventureId: 'adventure-1',
    campaignId: 'campaign-1',
    sessionId: PLAY_SESSION_ID,
    characterId: null,
    adventureTitle: 'The Sunken Archive',
    status: 'InProgress',
    outcomeNotes: null,
    currentScene,
    currentSceneNumber: 2,
    totalScenes: 7,
    completedSceneCount: 1,
    completedSceneIds: ['scene-1'],
    npcStates: currentScene.npcs,
    revealedClues: [],
    revealedSecrets: [],
    encounterOutcomes: [],
    startedAt: '2026-10-12T10:00:00.000Z',
    completedAt: null,
    lastActivityAt: '2026-10-12T10:05:00.000Z',
    // A solo run has none of the group-play state.
    isGroup: false,
    activeParticipantId: null,
    activeParticipantName: null,
    currentRound: 0,
    turnOrder: [],
    ...overrides,
  }
}

/** Alice, Bob and Cara in that order, Alice acting, round 1. */
export function groupRunState(overrides: Partial<AdventureSessionStateDto> = {}): AdventureSessionStateDto {
  const names = ['Alice', 'Bob', 'Cara']
  const activeIndex = names.findIndex((n) => n === (overrides.activeParticipantName ?? 'Alice'))
  return makeState({
    isGroup: true,
    activeParticipantId: `p-${names[activeIndex].toLowerCase()}`,
    activeParticipantName: names[activeIndex],
    currentRound: 1,
    turnOrder: names.map((name, i) => ({
      participantId: `p-${name.toLowerCase()}`,
      participantName: name,
      isActive: i === activeIndex,
    })),
    ...overrides,
  })
}

/** A state one step later in time, so it is not discarded as stale. */
export function later(state: AdventureSessionStateDto, seconds = 10): AdventureSessionStateDto {
  return {
    ...state,
    lastActivityAt: new Date(Date.parse(state.lastActivityAt) + seconds * 1000).toISOString(),
  }
}
