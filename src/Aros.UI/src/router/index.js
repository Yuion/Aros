import { createRouter, createWebHashHistory } from 'vue-router'

const routes = [
  {
    path: '/',
    name: 'home',
    component: () => import('@/views/HomeView.vue'),
  },
  {
    path: '/tutor',
    name: 'tutor',
    component: () => import('@/views/tutor/TutorView.vue'),
    meta: { nav: true, label: 'Tutor', icon: '🧑‍🏫' },
  },
  {
    // The day's work in one game — everything due, mixed, weighted towards the weak spots
    path: '/daily',
    name: 'daily',
    component: () => import('@/views/daily/DailyView.vue'),
    meta: { nav: true, label: 'Daily Practice', icon: '🎯' },
  },
  {
    path: '/daily/session',
    name: 'daily-session',
    component: () => import('@/views/daily/DailySessionView.vue'),
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
    path: '/grammar',
    name: 'grammar',
    component: () => import('@/views/grammar/GrammarView.vue'),
    meta: { nav: true, label: 'Grammar Trainer', icon: '🧩' },
  },
  {
    path: '/grammar/round',
    name: 'grammar-round',
    component: () => import('@/views/grammar/GrammarRoundView.vue'),
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
    // The ear on its own: one syllable, which of the four tones was it
    path: '/tones',
    name: 'tones',
    component: () => import('@/views/tones/TonesView.vue'),
    meta: { nav: true, label: 'Tones', icon: '🎵' },
  },
  {
    // Practice for the hand rather than the memory, so it sits with the trainers - but it
    // records nothing the trainers read
    path: '/writing',
    name: 'writing',
    component: () => import('@/views/writing/WritingView.vue'),
    meta: { nav: true, label: 'Writing', icon: '✏️' },
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
      {
        path: 'grammar',
        name: 'stats-grammar',
        component: () => import('@/views/stats/GrammarStatsView.vue'),
      },
      {
        path: 'tones',
        name: 'stats-tones',
        component: () => import('@/views/stats/TonesStatsView.vue'),
      },
      {
        path: 'writing',
        name: 'stats-writing',
        component: () => import('@/views/stats/WritingStatsView.vue'),
      },
      {
        path: 'tutor',
        name: 'stats-tutor',
        component: () => import('@/views/stats/TutorStatsView.vue'),
      },
    ],
  },
  // Old bookmark from when stats were listening-only
  { path: '/chinese-stats', redirect: '/stats/listening' },
  {
    // Last in the list, so last in the sidebar: not study, but the thing that means a dead
    // disk costs a machine rather than a year
    path: '/backup',
    name: 'backup',
    component: () => import('@/views/backup/BackupView.vue'),
    meta: { nav: true, foot: true, label: 'Backup', icon: '💾' },
  },
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
