import { createRouter, createWebHistory } from 'vue-router'
import HomeView from '@/views/HomeView.vue'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      name: 'home',
      component: HomeView,
    },
    {
      path: '/campaigns',
      name: 'campaigns',
      // Lazy-loaded when the route is visited
      component: () => import('@/features/campaigns/index'),
    },
    {
      path: '/library',
      name: 'library',
      component: () => import('@/features/library/index'),
    },
    {
      path: '/characters',
      name: 'characters',
      component: () => import('@/features/characters/index'),
    },
    {
      path: '/characters/:characterId/field-review',
      name: 'character-field-review',
      component: () => import('@/features/characters/CharacterFieldReview.vue'),
      props: (route) => ({
        characterId: route.params.characterId as string,
        unmappedFields: route.query.unmappedFields
          ? JSON.parse(decodeURIComponent(route.query.unmappedFields as string))
          : [],
        warnings: route.query.warnings
          ? JSON.parse(decodeURIComponent(route.query.warnings as string))
          : [],
      }),
    },
    {
      path: '/settings/ai',
      name: 'ai-settings',
      component: () => import('@/features/ai/AiSettingsView.vue'),
    },
    {
      path: '/rules-lookup',
      name: 'rules-lookup',
      component: () => import('@/features/ai/RulesLookupView.vue'),
    },
    {
      path: '/sessions',
      name: 'sessions',
      component: () => import('@/features/sessions/index'),
    },
    {
      path: '/join/:sessionId',
      name: 'join-session',
      component: () => import('@/features/sessions/JoinSessionView.vue'),
      props: true,
    },
    {
      path: '/sessions/:sessionId/host',
      name: 'host-session',
      component: () => import('@/features/sessions/HostSessionView.vue'),
      props: true,
    },
    {
      path: '/sessions/:sessionId/play',
      name: 'play-session',
      component: () => import('@/features/sessions/PlayerSessionView.vue'),
      props: true,
    },
    {
      path: '/adventures/generate',
      name: 'adventure-generate',
      component: () => import('@/features/adventure-generation/AdventureGenerateView.vue'),
    },
    {
      path: '/adventures/:adventureId/review',
      name: 'adventure-review',
      component: () => import('@/features/adventure-generation/AdventureReviewView.vue'),
      props: true,
    },
  ],
})

export default router
