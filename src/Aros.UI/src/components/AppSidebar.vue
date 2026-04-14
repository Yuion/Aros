<template>
  <!-- Mobile overlay -->
  <div v-if="isOpen" class="overlay" @click="$emit('close')" />

  <nav class="sidebar" :class="{ open: isOpen }">
    <div class="sidebar-header">
      <RouterLink to="/" class="logo" @click="$emit('close')">Aros</RouterLink>
      <button class="close-btn" @click="$emit('close')">✕</button>
    </div>

    <ul class="nav-list">
      <li v-for="item in navRoutes" :key="item.path">
        <RouterLink
          :to="item.path"
          class="nav-item"
          :class="{ active: route.path.startsWith(item.path) }"
          @click="$emit('close')"
        >
          <span class="nav-icon">{{ item.meta.icon }}</span>
          <span class="nav-label">{{ item.meta.label }}</span>
        </RouterLink>
      </li>
    </ul>
  </nav>
</template>

<script setup>
import { RouterLink, useRoute, useRouter } from 'vue-router'

defineProps({ isOpen: Boolean })
defineEmits(['close'])

const route = useRoute()
const router = useRouter()
const navRoutes = router.getRoutes().filter(r => r.meta?.nav)
</script>

<style scoped>
.overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.4);
  z-index: 99;
}

.sidebar {
  position: fixed;
  top: 0;
  left: 0;
  height: 100vh;
  width: 240px;
  background: #1e1e2e;
  color: #cdd6f4;
  display: flex;
  flex-direction: column;
  z-index: 100;
  transform: translateX(-100%);
  transition: transform 0.25s ease;
}

/* Desktop: always visible */
@media (min-width: 768px) {
  .sidebar {
    transform: translateX(0);
    position: sticky;
    top: 0;
    height: 100vh;
    flex-shrink: 0;
  }

  .overlay,
  .close-btn {
    display: none;
  }
}

/* Mobile: slide in when open */
.sidebar.open {
  transform: translateX(0);
}

.sidebar-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 1.25rem 1rem;
  border-bottom: 1px solid #313244;
}

.logo {
  font-size: 1.2rem;
  font-weight: 700;
  color: #cba6f7;
  text-decoration: none;
}

.close-btn {
  background: none;
  border: none;
  color: #cdd6f4;
  font-size: 1rem;
  cursor: pointer;
  padding: 0.25rem;
}

.nav-list {
  list-style: none;
  padding: 0.75rem 0;
  flex: 1;
}

.nav-item {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.75rem 1rem;
  color: #a6adc8;
  text-decoration: none;
  border-radius: 6px;
  margin: 0 0.5rem;
  transition: background 0.15s, color 0.15s;
}

.nav-item:hover {
  background: #313244;
  color: #cdd6f4;
}

.nav-item.active {
  background: #313244;
  color: #cba6f7;
}

.nav-icon {
  font-size: 1.1rem;
}
</style>
