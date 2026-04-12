<template>
  <div class="app-shell">
    <AppSidebar :isOpen="sidebarOpen" @close="sidebarOpen = false" />

    <div class="main-area">
      <header class="topbar">
        <button class="menu-btn" @click="sidebarOpen = true">☰</button>
        <span class="status-dot" :class="{ offline: !isOnline }" :title="isOnline ? 'Online' : 'Offline'">●</span>
        <span class="status-text">{{ isOnline ? 'Online' : 'Offline' }}</span>
      </header>

      <main class="content">
        <RouterView />
      </main>
    </div>
  </div>
</template>

<script setup>
import { ref } from 'vue'
import { RouterView } from 'vue-router'
import AppSidebar from '@/components/AppSidebar.vue'
import { useOnlineStatus } from '@/composables/useOnlineStatus'

const sidebarOpen = ref(false)
const { isOnline } = useOnlineStatus()
</script>

<style>
* {
  box-sizing: border-box;
  margin: 0;
  padding: 0;
}

body {
  font-family: system-ui, sans-serif;
  background: #f5f5f5;
  color: #1a1a1a;
}
</style>

<style scoped>
.app-shell {
  display: flex;
  height: 100vh;
}

.main-area {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  /* Desktop: offset for sidebar width */
  margin-left: 0;
}

@media (min-width: 768px) {
  .menu-btn {
    display: none;
  }
}

.topbar {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.6rem 1rem;
  background: white;
  border-bottom: 1px solid #e5e7eb;
  flex-shrink: 0;
}

.menu-btn {
  background: none;
  border: none;
  font-size: 1.3rem;
  cursor: pointer;
  padding: 0.2rem 0.4rem;
  margin-right: 0.5rem;
}

.status-dot {
  font-size: 0.6rem;
  color: #22c55e;
}

.status-dot.offline {
  color: #f59e0b;
}

.status-text {
  font-size: 0.75rem;
  color: #6b7280;
}

.content {
  flex: 1;
  overflow-y: auto;
  padding: 1.5rem;
}
</style>
