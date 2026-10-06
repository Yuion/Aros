<!--
  Every text the tutor has written, kept.

  These are not listening sentences and never become them. A sentence is an item in a schedule —
  scored per mode, drawn by weight, retired when it has stuck — and a passage of a hundred and
  fifty characters would crowd that pool without ever belonging in it. A text is read, translated,
  and kept because you might want it again.
-->
<template>
  <div class="page">
    <header class="head">
      <div>
        <h1>Reading</h1>
        <p class="subtitle">
          Long texts to translate, written out of your own vocabulary.
          <RouterLink to="/tutor">Ask the tutor for another →</RouterLink>
        </p>
      </div>
    </header>

    <p v-if="error" class="error">{{ error }}</p>
    <p v-else-if="loading" class="placeholder">Loading…</p>

    <p v-else-if="!texts.length" class="placeholder">
      No texts yet. Open the tutor and start a <strong>Reading text</strong> session.
    </p>

    <template v-else>
      <p class="count">
        {{ texts.length }} {{ texts.length === 1 ? 'text' : 'texts' }} ·
        {{ withAudio }} with audio
      </p>

      <ReadingText
        v-for="text in texts"
        :key="text.id"
        :text="text"
        :speaking="speakingId === text.id"
        deletable
        @speak="speak"
        @delete="remove"
      />
    </template>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { api } from '@/services/api'
import ReadingText from '@/components/tutor/ReadingText.vue'

const texts = ref([])
const loading = ref(true)
const error = ref('')
const speakingId = ref(0)

const withAudio = computed(() => texts.value.filter((t) => t.hasAudio).length)

async function load() {
  try {
    texts.value = await api.get('/tutor/texts')
  } catch (e) {
    error.value = e.message
  } finally {
    loading.value = false
  }
}

/** One synthesis, paid once. The file is this text's alone and no trainer ever draws it. */
async function speak(text) {
  speakingId.value = text.id
  error.value = ''

  try {
    const updated = await api.post(`/tutor/texts/${text.id}/speak`)
    texts.value = texts.value.map((t) => (t.id === updated.id ? updated : t))
  } catch (e) {
    error.value = e.message
  } finally {
    speakingId.value = 0
  }
}

async function remove(text) {
  if (!window.confirm(`Delete "${text.title || 'this text'}"? The text and its translation go.`)) return

  try {
    await api.delete(`/tutor/texts/${text.id}`)
    texts.value = texts.value.filter((t) => t.id !== text.id)
  } catch (e) {
    error.value = e.message
  }
}

onMounted(load)
</script>

<style scoped>
.page {
  max-width: 760px;
  margin: 0 auto;
  display: flex;
  flex-direction: column;
  gap: 0.9rem;
  padding: 0 0 2rem;
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

.subtitle a {
  color: #6d5bd0;
}

.count {
  font-size: 0.75rem;
  color: #9ca3af;
}

.placeholder {
  color: #9ca3af;
  font-size: 0.88rem;
  padding: 1.5rem 0;
}

.error {
  padding: 0.6rem 0.8rem;
  background: #fef2f2;
  border: 1px solid #fecaca;
  border-radius: 8px;
  font-size: 0.8rem;
  color: #b91c1c;
}
</style>
