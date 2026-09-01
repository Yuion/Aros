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

    <section class="homophones">
      <button class="disclosure" @click="showGroups = !showGroups">
        {{ showGroups ? '▾' : '▸' }} Sound-alike characters
        <span class="count">{{ groups.length }}</span>
      </button>

      <div v-if="showGroups" class="panel">
        <p class="panel-note">
          Characters that sound the same, like 他 / 她. Two sentences differing only inside a group
          are identical to the ear, so they never appear in the same question.
        </p>

        <form class="add-row" @submit.prevent="addGroup">
          <input v-model="newChars" lang="zh" placeholder="他她它" class="chars-input" />
          <input v-model="newReading" placeholder="tā (optional)" class="reading-input" />
          <button type="submit" class="add-btn" :disabled="!newChars.trim()">Add</button>
        </form>

        <p v-if="groupError" class="group-error">{{ groupError }}</p>

        <ul class="group-list">
          <li v-for="group in groups" :key="group.id">
            <span class="group-chars" lang="zh">{{ [...group.characters].join(' / ') }}</span>
            <span v-if="group.reading" class="group-reading">{{ group.reading }}</span>
            <button class="remove" title="Remove" @click="removeGroup(group)">✕</button>
          </li>
        </ul>
      </div>
    </section>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { api } from '@/services/api'

const router = useRouter()
const clipCount = ref(0)
const loading = ref(true)

const groups = ref([])
const showGroups = ref(false)
const newChars = ref('')
const newReading = ref('')
const groupError = ref('')

const ready = computed(() => clipCount.value >= 3)

async function loadGroups() {
  try {
    groups.value = await api.get('/homophones')
  } catch (e) {
    groupError.value = e.message
  }
}

async function addGroup() {
  groupError.value = ''

  try {
    await api.post('/homophones', { characters: newChars.value, reading: newReading.value })
    newChars.value = ''
    newReading.value = ''
    await loadGroups()
  } catch (e) {
    groupError.value = e.message
  }
}

async function removeGroup(group) {
  groupError.value = ''

  try {
    await api.delete(`/homophones/${group.id}`)
    await loadGroups()
  } catch (e) {
    groupError.value = e.message
  }
}

onMounted(async () => {
  try {
    const clips = await api.get('/tts/clips')
    clipCount.value = clips.length
  } catch {
    clipCount.value = 0
  } finally {
    loading.value = false
  }

  await loadGroups()
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

.homophones {
  width: 100%;
  max-width: 460px;
  margin-top: 2.5rem;
  text-align: left;
}

.disclosure {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  width: 100%;
  background: none;
  border: none;
  padding: 0.5rem 0;
  font-family: inherit;
  font-size: 0.85rem;
  font-weight: 600;
  color: #4b5563;
  cursor: pointer;
}

.count {
  font-size: 0.7rem;
  font-weight: 500;
  color: #6b7280;
  background: #e5e7eb;
  border-radius: 999px;
  padding: 0.1rem 0.45rem;
}

.panel {
  padding: 0.85rem 0 0.25rem;
}

.panel-note {
  font-size: 0.78rem;
  color: #6b7280;
  line-height: 1.5;
  margin-bottom: 0.85rem;
}

.add-row {
  display: flex;
  gap: 0.4rem;
  margin-bottom: 0.6rem;
}

.chars-input,
.reading-input {
  padding: 0.45rem 0.6rem;
  font-family: inherit;
  border: 1px solid #e5e7eb;
  border-radius: 7px;
  background: white;
  color: #1a1a1a;
  min-width: 0;
}

.chars-input {
  flex: 1;
  font-size: 1.05rem;
}

.reading-input {
  width: 110px;
  font-size: 0.8rem;
}

.add-btn {
  padding: 0.45rem 0.9rem;
  font-size: 0.8rem;
  font-weight: 600;
  color: white;
  background: #6d5bd0;
  border: none;
  border-radius: 7px;
  cursor: pointer;
  flex-shrink: 0;
}

.add-btn:disabled {
  background: #c7c4d6;
  cursor: not-allowed;
}

.group-error {
  font-size: 0.78rem;
  color: #b91c1c;
  margin-bottom: 0.6rem;
}

.group-list {
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
}

.group-list li {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  padding: 0.45rem 0.6rem;
  background: white;
  border: 1px solid #e5e7eb;
  border-radius: 7px;
}

.group-chars {
  flex: 1;
  font-size: 1rem;
}

.group-reading {
  font-size: 0.75rem;
  color: #6b7280;
}

.remove {
  background: none;
  border: none;
  color: #9ca3af;
  cursor: pointer;
  font-size: 0.8rem;
  padding: 0.15rem 0.3rem;
  border-radius: 5px;
}

.remove:hover {
  background: #fef2f2;
  color: #b91c1c;
}
</style>
