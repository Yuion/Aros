<template>
  <main class="home">
    <h1>Aros</h1>
    <p class="platform">Platform: {{ platform }}</p>
    <button @click="ping">Ping API</button>
    <p v-if="response" class="response">{{ response }}</p>
    <p v-if="error" class="error">{{ error }}</p>
  </main>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import { api } from '@/services/api'

const platform = ref('unknown')
const response = ref('')
const error = ref('')

onMounted(async () => {
  try {
    const result = await api.get('/system/ping')
    platform.value = result.platform
  } catch {
    platform.value = 'API not reachable'
  }
})

async function ping() {
  error.value = ''
  response.value = ''
  try {
    const result = await api.get('/system/ping')
    response.value = JSON.stringify(result, null, 2)
  } catch (e) {
    error.value = e.message
  }
}
</script>

<style scoped>
.home {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  gap: 1rem;
  padding: 2rem;
}

h1 { font-size: 2rem; }
.platform { color: #666; }

button {
  padding: 0.6rem 1.4rem;
  font-size: 1rem;
  border: none;
  border-radius: 6px;
  background: #0066cc;
  color: white;
  cursor: pointer;
}

button:hover { background: #0052a3; }

.response {
  font-family: monospace;
  background: #eee;
  padding: 0.5rem 1rem;
  border-radius: 4px;
  white-space: pre;
}

.error { color: #cc0000; }
</style>
