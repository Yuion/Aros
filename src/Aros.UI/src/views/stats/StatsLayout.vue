<template>
  <div class="stats">
    <header>
      <h1>Stats</h1>
      <p class="subtitle">How your practice is going.</p>
    </header>

    <!-- One tab per kind of test. Adding another area means adding a child route. -->
    <nav class="tabs">
      <RouterLink v-for="tab in TABS" :key="tab.to" :to="tab.to" class="tab">
        <span class="tab-icon">{{ tab.icon }}</span>{{ tab.label }}
      </RouterLink>
    </nav>

    <RouterView />
  </div>
</template>

<script setup>
import { RouterLink, RouterView } from 'vue-router'

const TABS = [
  { to: '/stats/listening', label: 'Listening', icon: '👂' },
  { to: '/stats/vocab', label: 'Vocabulary', icon: '📖' },
]
</script>

<style scoped>
.stats {
  max-width: 760px;
  margin: 0 auto;
  display: flex;
  flex-direction: column;
  gap: 1.1rem;
}

h1 {
  font-size: 1.5rem;
  font-weight: 700;
}

.subtitle {
  color: #6b7280;
  font-size: 0.85rem;
  margin-top: 0.25rem;
}

.tabs {
  display: flex;
  gap: 0.3rem;
  border-bottom: 1px solid #e5e7eb;
}

.tab {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.5rem 0.85rem;
  font-size: 0.85rem;
  font-weight: 500;
  color: #6b7280;
  text-decoration: none;
  border-bottom: 2px solid transparent;
  margin-bottom: -1px;
}

.tab:hover {
  color: #1a1a1a;
}

.tab.router-link-active {
  color: #6d5bd0;
  border-bottom-color: #6d5bd0;
  font-weight: 600;
}

.tab-icon {
  font-size: 0.95rem;
}
</style>
