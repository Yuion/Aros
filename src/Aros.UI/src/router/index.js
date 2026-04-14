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
]

export default createRouter({
  history: createWebHashHistory(),
  routes,
})
