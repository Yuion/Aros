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
    path: '/chinese-listening/play',
    name: 'chinese-listening-play',
    component: () => import('@/views/chinese/ListeningGameView.vue'),
  },
]

export default createRouter({
  history: createWebHashHistory(),
  routes,
})
