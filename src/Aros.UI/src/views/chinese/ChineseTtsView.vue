<template>
  <div class="tts">
    <header>
      <h1>Chinese TTS</h1>
      <p class="subtitle">
        Every sentence is synthesized once, then cached — replaying an existing one is free.
      </p>
    </header>

    <form class="composer" @submit.prevent="speak">
      <textarea
        v-model="text"
        rows="3"
        placeholder="输入中文…"
        lang="zh"
        :disabled="loading"
        @keydown.ctrl.enter="speak"
      />
      <div class="composer-row">
        <button type="submit" class="primary" :disabled="loading || !text.trim()">
          {{ loading ? 'Synthesizing…' : 'Speak' }}
        </button>
        <span class="hint">Ctrl+Enter</span>
      </div>
    </form>

    <p v-if="error" class="error">{{ error }}</p>

    <p v-if="lastResult" class="result" :class="{ fresh: !lastResult.cached }">
      <span class="badge">{{ lastResult.cached ? 'Cached — no API call' : 'New — synthesized' }}</span>
      <span class="result-sentence" lang="zh">{{ lastResult.sentence }}</span>
    </p>

    <audio ref="player" controls class="player" />

    <section class="library">
      <h2>Library <span class="count">{{ clips.length }}</span></h2>

      <p v-if="!clips.length" class="empty">Nothing yet. Speak a sentence to start your library.</p>

      <ul v-else class="clip-list">
        <li v-for="clip in clips" :key="clip.id" class="clip">
          <button class="icon-btn" title="Play" @click="play(clip.audioUrl)">▶</button>
          <span class="clip-sentence" lang="zh">{{ clip.sentence }}</span>
          <span v-if="clip.correctCount || clip.wrongCount" class="score">
            {{ clip.correctCount }}✓ / {{ clip.wrongCount }}✗
          </span>
          <button class="icon-btn danger" title="Delete" @click="remove(clip)">✕</button>
        </li>
      </ul>
    </section>
  </div>
</template>

<script setup>
import { onMounted, ref } from 'vue'
import { api } from '@/services/api'

const text = ref('')
const clips = ref([])
const lastResult = ref(null)
const error = ref('')
const loading = ref(false)
const player = ref(null)

async function loadClips() {
  try {
    clips.value = await api.get('/tts/clips')
  } catch (e) {
    error.value = e.message
  }
}

async function speak() {
  if (loading.value || !text.value.trim()) return

  error.value = ''
  loading.value = true

  try {
    const result = await api.post('/tts/speak', { text: text.value })
    lastResult.value = result
    play(result.audioUrl)
    text.value = ''
    await loadClips()
  } catch (e) {
    error.value = e.message
  } finally {
    loading.value = false
  }
}

function play(url) {
  if (!player.value) return
  player.value.src = url
  player.value.play().catch(() => {})
}

async function remove(clip) {
  if (!confirm(`Delete "${clip.sentence}" and its audio file?`)) return

  try {
    await api.delete(`/tts/clips/${clip.id}`)
    if (lastResult.value?.id === clip.id) lastResult.value = null
    await loadClips()
  } catch (e) {
    error.value = e.message
  }
}

onMounted(loadClips)
</script>

<style scoped>
.tts {
  max-width: 680px;
  margin: 0 auto;
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
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

.composer {
  display: flex;
  flex-direction: column;
  gap: 0.6rem;
}

textarea {
  width: 100%;
  padding: 0.75rem;
  font-family: inherit;
  font-size: 1.15rem;
  line-height: 1.6;
  border: 1px solid #e5e7eb;
  border-radius: 10px;
  resize: vertical;
  background: white;
  color: #1a1a1a;
}

textarea:focus {
  outline: 2px solid #cba6f7;
  outline-offset: -1px;
}

.composer-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.primary {
  padding: 0.6rem 1.4rem;
  font-size: 0.9rem;
  font-weight: 600;
  color: white;
  background: #6d5bd0;
  border: none;
  border-radius: 8px;
  cursor: pointer;
}

.primary:disabled {
  background: #c7c4d6;
  cursor: not-allowed;
}

.hint {
  font-size: 0.75rem;
  color: #9ca3af;
}

.error {
  padding: 0.7rem 0.9rem;
  background: #fef2f2;
  border: 1px solid #fecaca;
  border-radius: 8px;
  color: #b91c1c;
  font-size: 0.85rem;
}

.result {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
}

.badge {
  font-size: 0.7rem;
  font-weight: 600;
  padding: 0.2rem 0.55rem;
  border-radius: 999px;
  background: #e0f2fe;
  color: #0369a1;
}

.result.fresh .badge {
  background: #fef3c7;
  color: #92400e;
}

.result-sentence {
  font-size: 1.1rem;
}

.player {
  width: 100%;
  height: 36px;
}

.library h2 {
  font-size: 1rem;
  font-weight: 600;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.75rem;
}

.count {
  font-size: 0.75rem;
  font-weight: 500;
  color: #6b7280;
  background: #e5e7eb;
  border-radius: 999px;
  padding: 0.1rem 0.5rem;
}

.empty {
  color: #9ca3af;
  font-size: 0.85rem;
}

.clip-list {
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}

.clip {
  display: flex;
  align-items: center;
  gap: 0.65rem;
  padding: 0.6rem 0.75rem;
  background: white;
  border: 1px solid #e5e7eb;
  border-radius: 8px;
}

.clip-sentence {
  flex: 1;
  font-size: 1.05rem;
  word-break: break-word;
}

.score {
  font-size: 0.7rem;
  color: #6b7280;
  white-space: nowrap;
}

.icon-btn {
  background: none;
  border: none;
  cursor: pointer;
  font-size: 0.9rem;
  color: #6d5bd0;
  padding: 0.2rem 0.35rem;
  border-radius: 6px;
  flex-shrink: 0;
}

.icon-btn:hover {
  background: #f3f0ff;
}

.icon-btn.danger {
  color: #9ca3af;
}

.icon-btn.danger:hover {
  background: #fef2f2;
  color: #b91c1c;
}
</style>
