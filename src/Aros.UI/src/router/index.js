import { createRouter, createWebHashHistory } from 'vue-router'

const routes = [
  {
    path: '/',
    name: 'home',
    component: () => import('@/views/HomeView.vue'),
  },
  {
    path: '/vocab',
    name: 'vocab',
    component: () => import('@/views/vocab/VocabView.vue'),
    meta: { nav: true, label: 'Vocabulary Trainer', icon: '📖' },
  },
  {
    path: '/vocab/session',
    name: 'vocab-session',
    component: () => import('@/views/vocab/VocabSessionView.vue'),
  },
  {
    path: '/chinese-tts',
    name: 'chinese-tts',
    component: () => import('@/views/chinese/ChineseTtsView.vue'),
    meta: { nav: true, label: 'Chinese TTS', icon: '🗣️' },
  },
  {
    path: '/chinese-listening',
    name: 'chinese-listening',
    component: () => import('@/views/chinese/ListeningView.vue'),
    meta: { nav: true, label: 'Chinese Listening', icon: '👂' },
  },
  {
    // A shell with one tab per kind of test — a new area is a new child route
    path: '/stats',
    component: () => import('@/views/stats/StatsLayout.vue'),
    meta: { nav: true, label: 'Stats', icon: '📊' },
    children: [
      { path: '', redirect: '/stats/listening' },
      {
        path: 'listening',
        name: 'stats-listening',
        component: () => import('@/views/stats/ListeningStatsView.vue'),
      },
      {
        path: 'vocab',
        name: 'stats-vocab',
        component: () => import('@/views/stats/VocabStatsView.vue'),
      },
    ],
  },
  // Old bookmark from when stats were listening-only
  { path: '/chinese-stats', redirect: '/stats/listening' },
  {
    path: '/chinese-listening/play',
    name: 'chinese-listening-play',
    component: () => import('@/views/chinese/ListeningGameView.vue'),
  },
]

export default createRouter({
  history: createWebHashHistory(),
  routes,
})
