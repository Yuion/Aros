<template>
  <div class="vocab">
    <header>
      <h1>Vocabulary Trainer</h1>
      <p class="subtitle">
        Words are collected from the sentences you add in Chinese TTS, or added here by hand.
      </p>
    </header>

    <p v-if="error" class="error">{{ error }}</p>
    <p v-else-if="loading" class="placeholder">Loading…</p>

    <template v-else>
      <!-- Dictionary needs importing once -->
      <p v-if="!dictionaryEntries" class="notice">
        The dictionary is empty, so new words arrive without pinyin or meaning.
        <button class="link" :disabled="importing" @click="importDictionary">
          {{ importing ? 'Importing…' : 'Import CC-CEDICT' }}
        </button>
      </p>

      <div class="start">
        <button class="play-button" :disabled="!selected.ready" @click="start">
          <span class="play-label">Start</span>
          <span class="play-sub">{{ selected.ready }} ready</span>
        </button>

        <select v-model="direction" class="direction-select">
          <option value="">All directions</option>
          <option v-for="d in DIRECTIONS" :key="d.value" :value="d.value">
            {{ d.label }} — {{ readyFor(d.value) }} ready
          </option>
        </select>
      </div>

      <!-- Nothing to draw, and why -->
      <p v-if="!selected.ready && ready.length" class="notice resting">
        <template v-if="selected.resting">
          {{ direction ? 'This direction is' : 'Every direction is' }} resting —
          {{ selected.resting }} waiting, next due {{ selected.nextDue }}.
          <template v-if="direction"> Pick another direction, or come back then.</template>
        </template>
        <template v-else-if="selected.mastered">
          {{ direction ? 'This direction is' : 'Everything is' }} mastered.
          Add more vocabulary to keep going.
        </template>
        <template v-else>
          Nothing testable in that direction — those words may still be waiting for review.
        </template>
      </p>

      <form class="add-word" @submit.prevent="addWord">
        <input
          v-model="newWord"
          lang="zh"
          placeholder="水  or  中国"
          :disabled="adding"
          class="add-input"
        />
        <button type="submit" class="add-btn" :disabled="adding || !newWord.trim()">
          {{ adding ? 'Adding…' : 'Add word' }}
        </button>
      </form>

      <p v-if="added.length" class="added">
        Added {{ added.length }} for review:
        <span v-for="w in added" :key="w.id" class="added-chip" lang="zh">{{ w.characters }}</span>
      </p>

      <p v-if="!ready.length" class="placeholder">
        Nothing testable yet.
        <template v-if="review.length">Confirm some entries below to get started.</template>
        <template v-else>
          <RouterLink to="/chinese-tts">Add a sentence in Chinese TTS →</RouterLink>
        </template>
      </p>

      <!-- Review queue -->
      <section v-if="review.length" class="card">
        <h2>Needs review <span class="count">{{ review.length }}</span></h2>
        <p class="card-note">
          Everything new lands here first. A multi-character word is offered alongside its
          individual characters, since which of them you actually wanted is your call — delete the
          ones you do not, check the pinyin and meaning on the rest, then confirm. Nothing is
          tested until you do.
        </p>

        <ul class="review-list">
          <li v-for="word in review" :key="word.id" class="review-item">
            <div class="review-head">
              <span class="chars" lang="zh">{{ word.characters }}</span>
              <button class="remove" title="Delete this word" @click="remove(word)">✕</button>
            </div>

            <label>
              <span>Pinyin</span>
              <input v-model="edits[word.id].pinyin" spellcheck="false" autocapitalize="none" />
            </label>
            <label>
              <span>English</span>
              <input v-model="edits[word.id].english" />
            </label>

            <p v-if="word.readingAlternatives" class="alternatives">
              Other readings: {{ word.readingAlternatives }}
            </p>

            <button class="confirm" @click="save(word)">Confirm</button>
          </li>
        </ul>
      </section>

      <!-- The pool -->
      <section v-if="ready.length" class="card">
        <h2>Words <span class="count">{{ ready.length }}</span></h2>
        <ul class="word-list">
          <li v-for="word in ready" :key="word.id">
            <span class="chars" lang="zh">{{ word.characters }}</span>
            <span class="pinyin">{{ word.pinyin }}</span>
            <span class="english">{{ word.english }}</span>
            <span v-if="word.correct || word.wrong" class="score">
              {{ word.correct }}✓ {{ word.wrong }}✗
            </span>
            <button class="remove" title="Delete this word" @click="remove(word)">✕</button>
          </li>
        </ul>
      </section>

      <p class="attribution">
        Dictionary data from
        <a href="https://www.mdbg.net/chinese/dictionary?page=cedict" target="_blank" rel="noopener">CC-CEDICT</a>,
        used under CC BY-SA 4.0.
      </p>
    </template>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { api } from '@/services/api'

const DIRECTIONS = [
  { value: 'CharactersToPinyin', label: 'Characters → Pinyin' },
  { value: 'CharactersToEnglish', label: 'Characters → English' },
  { value: 'PinyinToEnglish', label: 'Pinyin → English' },
  { value: 'EnglishToPinyin', label: 'English → Pinyin' },
  { value: 'PinyinToCharacters', label: 'Pinyin → Characters' },
  { value: 'EnglishToCharacters', label: 'English → Characters' },
]

const router = useRouter()
const words = ref([])
const dictionaryEntries = ref(0)
const direction = ref('')
const loading = ref(true)
const importing = ref(false)
const error = ref('')
const edits = reactive({})
const newWord = ref('')
const adding = ref(false)
const added = ref([])

const ready = computed(() => words.value.filter((w) => !w.needsReview))
const review = computed(() => words.value.filter((w) => w.needsReview))

const availability = ref([])

function readyFor(id) {
  return availability.value.find((a) => a.direction === id)?.ready ?? 0
}

// What the Start button is about to draw on: one direction, or all six together
const selected = computed(() => {
  const rows = direction.value
    ? availability.value.filter((a) => a.direction === direction.value)
    : availability.value

  const due = rows.map((r) => r.nextDueAt).filter(Boolean).sort()[0]

  return {
    ready: rows.reduce((n, r) => n + r.ready, 0),
    resting: rows.reduce((n, r) => n + r.resting, 0),
    mastered: rows.reduce((n, r) => n + r.mastered, 0),
    nextDue: rows.find((r) => r.nextDueAt === due)?.nextDue ?? '',
  }
})

async function load() {
  try {
    const [list, status, modes] = await Promise.all([
      api.get('/vocab/words'),
      api.get('/vocab/dictionary/status'),
      api.get('/vocab/availability'),
    ])
    words.value = list
    dictionaryEntries.value = status.entries
    availability.value = modes

    for (const word of list) {
      if (word.needsReview) edits[word.id] = { pinyin: word.pinyin, english: word.english }
    }
  } catch (e) {
    error.value = e.message
  } finally {
    loading.value = false
  }
}

function start() {
  router.push({
    path: '/vocab/session',
    query: direction.value ? { direction: direction.value } : {},
  })
}

async function addWord() {
  if (adding.value || !newWord.value.trim()) return

  adding.value = true
  error.value = ''
  added.value = []

  try {
    const result = await api.post('/vocab/words', { characters: newWord.value })
    added.value = result.added
    newWord.value = ''
    await load()
  } catch (e) {
    error.value = e.message
  } finally {
    adding.value = false
  }
}

async function save(word) {
  error.value = ''

  try {
    await api.put(`/vocab/words/${word.id}`, edits[word.id])
    await load()
  } catch (e) {
    error.value = e.message
  }
}

async function remove(word) {
  if (!window.confirm(`Delete ${word.characters}?`)) return

  try {
    await api.delete(`/vocab/words/${word.id}`)
    await load()
  } catch (e) {
    error.value = e.message
  }
}

async function importDictionary() {
  importing.value = true
  error.value = ''

  try {
    const result = await api.post('/vocab/dictionary/import')
    dictionaryEntries.value = result.entries
  } catch (e) {
    error.value = e.message
  } finally {
    importing.value = false
  }
}

onMounted(load)
</script>

<style scoped>
.vocab {
  max-width: 720px;
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

.error {
  padding: 0.7rem 0.9rem;
  background: #fef2f2;
  border: 1px solid #fecaca;
  border-radius: 8px;
  color: #b91c1c;
  font-size: 0.85rem;
}

.notice {
  padding: 0.7rem 0.9rem;
  background: #fffbeb;
  border: 1px solid #fde68a;
  border-radius: 8px;
  color: #92400e;
  font-size: 0.85rem;
}

/* Resting is a scheduled pause, not a problem — say it calmly */
.notice.resting {
  background: #eff6ff;
  border-color: #bfdbfe;
  color: #1e40af;
}

.placeholder {
  color: #9ca3af;
  font-size: 0.88rem;
}

.placeholder a {
  color: #6d5bd0;
}

.link {
  background: none;
  border: none;
  color: #92400e;
  font-family: inherit;
  font-size: 0.85rem;
  font-weight: 600;
  text-decoration: underline;
  cursor: pointer;
  padding: 0;
}

.start {
  display: flex;
  align-items: center;
  gap: 1rem;
  flex-wrap: wrap;
}

.play-button {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.15rem;
  width: 132px;
  height: 132px;
  border-radius: 50%;
  border: none;
  background: #6d5bd0;
  color: white;
  cursor: pointer;
  box-shadow: 0 8px 22px rgba(109, 91, 208, 0.3);
  transition: transform 0.15s, box-shadow 0.15s;
}

.play-button:hover:not(:disabled) {
  transform: translateY(-3px);
  box-shadow: 0 12px 28px rgba(109, 91, 208, 0.38);
}

.play-button:disabled {
  background: #c7c4d6;
  box-shadow: none;
  cursor: not-allowed;
}

.play-label {
  font-size: 1.15rem;
  font-weight: 700;
  letter-spacing: 0.03em;
}

.play-sub {
  font-size: 0.72rem;
  opacity: 0.85;
}

.add-word {
  display: flex;
  gap: 0.5rem;
}

.add-input {
  flex: 1;
  min-width: 0;
  max-width: 16rem;
  padding: 0.5rem 0.7rem;
  font-family: inherit;
  font-size: 1.1rem;
  border: 1px solid #e5e7eb;
  border-radius: 8px;
  background: white;
  color: #1a1a1a;
}

.add-input:focus {
  outline: 2px solid #cba6f7;
  outline-offset: -1px;
}

.add-btn {
  padding: 0.5rem 1rem;
  font-size: 0.82rem;
  font-weight: 600;
  color: white;
  background: #6d5bd0;
  border: none;
  border-radius: 8px;
  cursor: pointer;
  flex-shrink: 0;
}

.add-btn:disabled {
  background: #c7c4d6;
  cursor: not-allowed;
}

.added {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  flex-wrap: wrap;
  font-size: 0.82rem;
  color: #92400e;
  padding: 0.5rem 0.75rem;
  background: #fffbeb;
  border: 1px solid #fde68a;
  border-radius: 8px;
}

.added-chip {
  font-size: 1.05rem;
  font-weight: 600;
  color: #1a1a1a;
  background: white;
  border-radius: 5px;
  padding: 0.1rem 0.4rem;
}

.direction-select {
  padding: 0.5rem 0.7rem;
  font-family: inherit;
  font-size: 0.85rem;
  border: 1px solid #e5e7eb;
  border-radius: 8px;
  background: white;
  color: #1a1a1a;
}

.card {
  padding: 1.1rem 1.2rem;
  background: white;
  border: 1px solid #e5e7eb;
  border-radius: 10px;
}

.card h2 {
  font-size: 0.95rem;
  font-weight: 600;
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.count {
  font-size: 0.7rem;
  font-weight: 500;
  color: #4b5563;
  background: #f0efec;
  border-radius: 999px;
  padding: 0.1rem 0.5rem;
}

.card-note {
  font-size: 0.76rem;
  color: #4b5563;
  line-height: 1.5;
  margin: 0.3rem 0 0.9rem;
}

.review-list,
.word-list {
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.review-item {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
  padding: 0.7rem 0.8rem;
  border: 1px solid #fde68a;
  background: #fffdf5;
  border-radius: 8px;
}

.review-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.chars {
  font-size: 1.35rem;
  font-weight: 600;
}

.review-item label {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.75rem;
  color: #6b7280;
}

.review-item label span {
  width: 3.6rem;
  flex-shrink: 0;
}

.review-item input {
  flex: 1;
  min-width: 0;
  padding: 0.35rem 0.5rem;
  font-family: inherit;
  font-size: 0.85rem;
  border: 1px solid #e5e7eb;
  border-radius: 6px;
  background: white;
  color: #1a1a1a;
}

.alternatives {
  font-size: 0.72rem;
  color: #92400e;
  line-height: 1.45;
}

.confirm {
  align-self: flex-start;
  padding: 0.35rem 0.9rem;
  font-size: 0.78rem;
  font-weight: 600;
  color: white;
  background: #6d5bd0;
  border: none;
  border-radius: 6px;
  cursor: pointer;
}

.word-list li {
  display: grid;
  grid-template-columns: auto 7rem 1fr auto auto;
  align-items: center;
  gap: 0.6rem;
  padding: 0.45rem 0.55rem;
  border: 1px solid #f0efec;
  border-radius: 7px;
}

.word-list .chars {
  font-size: 1.15rem;
}

.pinyin {
  font-size: 0.8rem;
  color: #6d5bd0;
}

.english {
  font-size: 0.8rem;
  color: #4b5563;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.score {
  font-size: 0.7rem;
  color: #9ca3af;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
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

.attribution {
  font-size: 0.7rem;
  color: #9ca3af;
  text-align: center;
}

.attribution a {
  color: #6b7280;
}

@media (max-width: 560px) {
  .word-list li {
    grid-template-columns: auto 1fr auto;
  }

  .english {
    display: none;
  }
}
</style>
