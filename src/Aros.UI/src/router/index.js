import { createRouter, createWebHashHistory } from 'vue-router'
import HomeView from '@/views/HomeView.vue'
import VocabView from '@/views/vocab/VocabView.vue'

const routes = [
  { path: '/', name: 'home', component: HomeView },
  { path: '/vocab', name: 'vocab', component: VocabView },
]

export default createRouter({
  history: createWebHashHistory(),
  routes,
})
