import { createRouter, createWebHashHistory } from 'vue-router'
import HomeView from '@/views/HomeView.vue'

const routes = [
  {
    path: '/',
    name: 'home',
    component: HomeView,
  },
]

// Hash history works reliably in WebView (no server needed)
export default createRouter({
  history: createWebHashHistory(),
  routes,
})
