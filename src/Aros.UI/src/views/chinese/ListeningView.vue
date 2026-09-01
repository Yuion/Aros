<template>
  <div class="landing">
    <h1>Chinese Listening</h1>
    <p class="subtitle">Ten clips. Pick the sentence you heard.</p>

    <button class="play-button" :disabled="!ready" @click="start">
      <span class="play-icon">▶</span>
      <span class="play-label">Play</span>
    </button>

    <p v-if="loading" class="note">Checking your library…</p>
    <p v-else-if="!ready" class="note warn">
      You need at least 3 sentences to play. Your library has {{ clipCount }}.
      <RouterLink to="/chinese-tts">Add some in Chinese TTS →</RouterLink>
    </p>
    <p v-else class="note">{{ clipCount }} sentences in your library.</p>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { api } from '@/services/api'

const router = useRouter()
const clipCount = ref(0)
const loading = ref(true)

const ready = computed(() => clipCount.value >= 3)

onMounted(async () => {
  try {
    const clips = await api.get('/tts/clips')
    clipCount.value = clips.length
  } catch {
    clipCount.value = 0
  } finally {
    loading.value = false
  }
})

function start() {
  router.push('/chinese-listening/play')
}
</script>

<style scoped>
.landing {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
  min-height: 60vh;
  text-align: center;
}

h1 {
  font-size: 1.6rem;
  font-weight: 700;
}

.subtitle {
  color: #6b7280;
  font-size: 0.9rem;
  margin-bottom: 1.5rem;
}

.play-button {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.4rem;
  width: 180px;
  height: 180px;
  border-radius: 50%;
  border: none;
  background: #6d5bd0;
  color: white;
  cursor: pointer;
  box-shadow: 0 8px 24px rgba(109, 91, 208, 0.3);
  transition: transform 0.15s, box-shadow 0.15s;
}

.play-button:hover:not(:disabled) {
  transform: translateY(-3px);
  box-shadow: 0 12px 30px rgba(109, 91, 208, 0.38);
}

.play-button:disabled {
  background: #c7c4d6;
  box-shadow: none;
  cursor: not-allowed;
}

.play-icon {
  font-size: 3rem;
  line-height: 1;
}

.play-label {
  font-size: 1rem;
  font-weight: 600;
  letter-spacing: 0.04em;
}

.note {
  margin-top: 1.25rem;
  font-size: 0.85rem;
  color: #6b7280;
}

.note.warn {
  color: #92400e;
}

.note a {
  color: #6d5bd0;
  margin-left: 0.35rem;
}
</style>
