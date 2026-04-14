<template>
  <div class="home">
    <h1 class="title">Aros</h1>
    <p class="subtitle">Personal application platform</p>

    <div class="app-grid">
      <RouterLink
        v-for="item in navRoutes"
        :key="item.path"
        :to="item.path"
        class="app-card"
      >
        <span class="app-icon">{{ item.meta.icon }}</span>
        <span class="app-label">{{ item.meta.label }}</span>
      </RouterLink>
    </div>
  </div>
</template>

<script setup>
import { RouterLink, useRouter } from 'vue-router'

const router = useRouter()
const navRoutes = router.getRoutes().filter(r => r.meta?.nav)
</script>

<style scoped>
.home {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 3rem 1.5rem;
  gap: 1rem;
}

.title {
  font-size: 2rem;
  font-weight: 700;
  color: #1a1a1a;
}

.subtitle {
  color: #6b7280;
  font-size: 0.95rem;
  margin-bottom: 1.5rem;
}

.app-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
  gap: 1rem;
  width: 100%;
  max-width: 600px;
}

.app-card {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.6rem;
  padding: 1.5rem 1rem;
  background: white;
  border: 1px solid #e5e7eb;
  border-radius: 12px;
  text-decoration: none;
  color: #1a1a1a;
  transition: box-shadow 0.15s, transform 0.15s;
}

.app-card:hover {
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.08);
  transform: translateY(-2px);
}

.app-icon {
  font-size: 2rem;
}

.app-label {
  font-size: 0.85rem;
  font-weight: 500;
  text-align: center;
}
</style>
