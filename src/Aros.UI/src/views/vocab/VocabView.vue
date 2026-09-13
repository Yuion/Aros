<template>
  <div class="vocab">
    <header>
      <h1>Vocabulary Trainer</h1>
      <p class="subtitle">
        Words are yours to add — one at a time, or a pasted table. Sentences in Chinese TTS no
        longer create any.
      </p>
    </header>

    <p v-if="error" class="error">{{ error }}</p>
    <p v-else-if="loading" class="placeholder">Loading…</p>

    <template v-else>
      <div class="start">
        <button class="play-button" :disabled="!selected.ready" @click="start">
          <span class="play-label">Start</span>
          <span class="play-sub">{{ roundLength }}</span>
        </button>

        <div class="start-options">
          <select v-model="direction" class="direction-select">
            <option value="">All directions</option>
            <option v-for="d in DIRECTIONS" :key="d.value" :value="d.value">
              {{ d.label }} — {{ readyFor(d.value) }} ready
            </option>
          </select>

          <!-- Everything, or a sample of it -->
          <div class="round-modes">
            <button
              v-for="option in ROUND_MODES"
              :key="option.id"
              class="round-mode"
              :class="{ active: sweep === option.sweep }"
              @click="sweep = option.sweep"
            >
              {{ option.label }}
            </button>
          </div>
        </div>
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

      <audio ref="player" />

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
        <h2>
          Words <span class="count">{{ shown.length }}</span>
          <span v-if="shown.length !== ready.length" class="of">of {{ ready.length }}</span>
        </h2>
        <p class="card-note">
          ▶ speaks the word. The first time costs one synthesis, then it is cached for good —
          {{ spoken }} of {{ ready.length }} have audio.
          <button
            v-if="silent"
            class="link-btn"
            :disabled="speakingAll"
            @click="speakMissing"
          >
            {{ speakingAll ? 'Speaking…' : `Speak the missing ${silent}` }}
          </button>
        </p>
        <LibraryTools
          v-model:search="search"
          v-model:filter="filter"
          v-model:sort="sort"
          v-model:descending="descending"
          :filters="FILTERS"
          placeholder="Find a word, reading or meaning"
        />

        <p v-if="!shown.length" class="placeholder">{{ nothingShown }}</p>

        <ul v-else class="word-list">
          <li v-for="word in shown" :key="word.id" :class="{ retired: word.retiredAt }">
            <!-- The row opens on click: the per-direction record and the retire button live
                 underneath, so the list stays one line per word until you ask for more -->
            <div class="word-head" @click="expand(word.id)">
              <button
                class="speak"
                :class="{ silent: !word.hasAudio }"
                :disabled="speaking === word.id"
                :title="word.hasAudio ? 'Play' : 'Speak it — costs one synthesis'"
                @click.stop="speak(word)"
              >
                {{ speaking === word.id ? '…' : '▶' }}
              </button>
              <span class="chars" lang="zh">{{ word.characters }}</span>
              <span class="pinyin">{{ word.pinyin }}</span>
              <span class="english">{{ word.english }}</span>
              <span v-if="word.retiredAt" class="tag">retired</span>
              <span v-else-if="word.correct || word.wrong" class="score">
                {{ word.correct }}✓ {{ word.wrong }}✗
              </span>
              <span class="caret">{{ opened === word.id ? '▾' : '▸' }}</span>
            </div>

            <div v-if="opened === word.id" class="word-body">
              <!-- Apart, not added up: recognising 水 and producing it are different skills -->
              <ul class="directions">
                <li v-for="d in word.directions" :key="d.key" class="direction">
                  <span class="dir-name">{{ DIRECTION_LABELS[d.key] ?? d.key }}</span>
                  <span class="dir-score">{{ d.correct }}✓ / {{ d.wrong }}✗</span>
                  <span class="dir-state" :class="d.state">{{ stateLabel(d) }}</span>
                </li>
              </ul>

              <div class="word-actions">
                <button class="retire-btn" :disabled="retiring === word.id" @click="retire(word)">
                  {{ word.retiredAt ? 'Put back in rotation' : 'Retire as mastered' }}
                </button>
                <button class="remove" title="Delete this word" @click="remove(word)">✕ Delete</button>
                <span class="added">added {{ day(word.createdAt) }}</span>
              </div>
            </div>
          </li>
        </ul>
      </section>
    </template>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { api } from '@/services/api'
import { GAP_FILTERS, FILTERS as BASE_FILTERS, arrange } from '@/services/library'
import LibraryTools from '@/components/LibraryTools.vue'

// Words carry their own gap: one with no audio has never been spoken
const FILTERS = [...BASE_FILTERS.slice(0, -1), GAP_FILTERS.vocab, BASE_FILTERS.at(-1)]

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
const direction = ref('')
const loading = ref(true)
const error = ref('')
const edits = reactive({})

const ready = computed(() => words.value.filter((w) => !w.needsReview))
const review = computed(() => words.value.filter((w) => w.needsReview))

const DIRECTION_LABELS = Object.fromEntries(DIRECTIONS.map((d) => [d.value, d.label]))

const search = ref('')
// Words you have finished with are hidden to begin with: the list is for what is still being
// asked, and everything else is one dropdown away
const filter = ref('rotation')
const sort = ref('added')
const descending = ref(true)
const opened = ref(null)
const retiring = ref(null)

const shown = computed(() =>
  arrange(ready.value, {
    search: search.value,
    filter: filter.value,
    sort: sort.value,
    descending: descending.value,
  })
)

const nothingShown = computed(() =>
  search.value.trim()
    ? `Nothing matches “${search.value.trim()}”.`
    : 'Nothing here — try a different filter.'
)

function expand(id) {
  opened.value = opened.value === id ? null : id
}

function stateLabel(part) {
  if (part.state === 'retired') return 'retired'
  if (part.state === 'mastered') return 'mastered'
  if (part.state === 'resting') return 'resting · back ' + part.due

  return part.streak > 0 ? 'ready · ' + part.streak + ' in a row' : 'ready'
}

// Stored in UTC, read in your evening: slicing the ISO string would call last night's import
// yesterday's for half the day
function day(value) {
  return value ? new Date(value).toLocaleDateString() : ''
}

/** Out of the trainer without being deleted, and back again — the streaks are never touched. */
async function retire(word) {
  retiring.value = word.id
  error.value = ''

  try {
    Object.assign(word, await api.put(`/vocab/words/${word.id}/retired`, { retired: !word.retiredAt }))
    availability.value = await api.get('/vocab/availability')
  } catch (e) {
    error.value = e.message
  } finally {
    retiring.value = null
  }
}

const player = ref(null)
const speaking = ref(0)

const spoken = computed(() => ready.value.filter((w) => w.hasAudio).length)
const silent = computed(() => ready.value.length - spoken.value)

const speakingAll = ref(false)

// Every missing word at once. It spends real credit, so the count is in the question.
async function speakMissing() {
  if (speakingAll.value) return
  if (!window.confirm(`Speak ${silent.value} words? That is ${silent.value} synthesis calls.`)) return

  speakingAll.value = true
  error.value = ''

  try {
    const result = await api.post('/vocab/words/audio/missing')
    await load()

    if (result.failures.length) {
      error.value = `${result.spoken} spoken, ${result.failures.length} failed: ` +
        result.failures.map((f) => f.characters).join(' ')
    }
  } catch (e) {
    error.value = e.message
  } finally {
    speakingAll.value = false
  }
}

// One paid synthesis the first time, cached from then on — so the button both buys and plays
async function speak(word) {
  if (speaking.value) return
  speaking.value = word.id
  error.value = ''

  try {
    if (!word.hasAudio) {
      await api.post(`/vocab/words/${word.id}/audio`)
      word.hasAudio = true
    }

    if (player.value) {
      player.value.src = `${word.audioUrl}?v=${word.id}`
      await player.value.play().catch(() => {})
    }
  } catch (e) {
    error.value = e.message
  } finally {
    speaking.value = 0
  }
}

const ROUND_MODES = [
  { id: 'sweep', label: 'Everything', sweep: true },
  { id: 'sample', label: 'Short round', sweep: false },
]

const sweep = ref(true)
const availability = ref([])

// What Start is about to hand you, so the size of a sweep is never a surprise. A short
// round is capped per direction, so it is shorter than its nominal length once a pool runs low.
const roundLength = computed(() => {
  const ready = selected.value.ready
  if (!ready) return 'nothing ready'
  if (sweep.value) return `${ready} questions`

  const count = direction.value
    ? Math.min(10, ready)
    : availability.value.reduce((total, row) => total + Math.min(3, row.ready), 0)

  return `${count} questions`
})

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
    const [list, modes] = await Promise.all([
      api.get('/vocab/words'),
      api.get('/vocab/availability'),
    ])
    words.value = list
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
  const query = {}
  if (direction.value) query.direction = direction.value
  if (!sweep.value) query.sweep = 'false'

  router.push({ path: '/vocab/session', query })
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

.start {
  display: flex;
  align-items: center;
  gap: 1rem;
  flex-wrap: wrap;
}

.play-button {
  font-family: inherit;
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

.caret {
  color: #9ca3af;
  font-size: 0.8rem;
}

/* A second reading is a decision, not an error — say what is held and let it be settled */

.start-options {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.round-modes {
  display: flex;
  gap: 0.4rem;
}

.round-mode {
  padding: 0.4rem 0.8rem;
  font-family: inherit;
  font-size: 0.8rem;
  font-weight: 600;
  color: #6b7280;
  background: white;
  border: 2px solid #e5e7eb;
  border-radius: 8px;
  cursor: pointer;
  transition: border-color 0.15s, color 0.15s;
}

.round-mode:hover {
  border-color: #cba6f7;
}

.round-mode.active {
  border-color: #6d5bd0;
  color: #6d5bd0;
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

.link-btn {
  font-family: inherit;
  font-size: inherit;
  font-weight: 600;
  color: #6d5bd0;
  background: none;
  border: none;
  padding: 0;
  margin-left: 0.35rem;
  cursor: pointer;
  text-decoration: underline;
}

.link-btn:disabled {
  color: #9ca3af;
  cursor: default;
}

.speak {
  padding: 0.2rem 0.45rem;
  font-size: 0.9rem;
  line-height: 1;
  color: #6d5bd0;
  background: white;
  border: 1px solid #ddd6fe;
  border-radius: 6px;
  cursor: pointer;
}

.speak:hover {
  border-color: #6d5bd0;
}

/* Never spoken: the button will spend money, so it does not look like a plain play button */
.speak.silent {
  color: #9ca3af;
  border-style: dashed;
}

.speak:disabled {
  cursor: default;
  opacity: 0.6;
}

.word-list li {
  border: 1px solid #f0efec;
  border-radius: 7px;
}

.word-list li.retired {
  background: #fafafa;
  border-style: dashed;
}

.word-head {
  display: grid;
  grid-template-columns: auto auto 7rem 1fr auto auto;
  align-items: center;
  gap: 0.6rem;
  padding: 0.45rem 0.55rem;
  cursor: pointer;
}

.word-list li.retired .chars {
  color: #6b7280;
}

.of {
  font-size: 0.72rem;
  font-weight: 500;
  color: #9ca3af;
}

.tag {
  font-size: 0.66rem;
  font-weight: 600;
  color: #15803d;
  background: #f0fdf4;
  border-radius: 999px;
  padding: 0.1rem 0.45rem;
  white-space: nowrap;
}

.caret {
  font-size: 0.7rem;
  color: #9ca3af;
}

.word-body {
  padding: 0.55rem 0.6rem 0.6rem 2.3rem;
  border-top: 1px solid #f3f4f6;
}

.directions {
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
}

.direction {
  display: grid;
  grid-template-columns: 11rem 5rem 1fr;
  gap: 0.5rem;
  align-items: baseline;
  font-size: 0.76rem;
  color: #6b7280;
}

.dir-name {
  color: #4b5563;
  font-weight: 600;
}

.dir-state.ready {
  color: #6d5bd0;
}

.dir-state.mastered,
.dir-state.retired {
  color: #15803d;
}

.dir-state.resting {
  color: #92400e;
}

.retire-btn {
  font: inherit;
  font-size: 0.78rem;
  padding: 0.3rem 0.7rem;
  border: 1px solid #ddd6fe;
  border-radius: 7px;
  background: white;
  color: #6d5bd0;
  cursor: pointer;
}

.retire-btn:hover:not(:disabled) {
  background: #f3f0ff;
}

.retire-btn:disabled {
  opacity: 0.6;
  cursor: default;
}

.word-actions {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  margin-top: 0.6rem;
  flex-wrap: wrap;
}

.added {
  margin-left: auto;
  font-size: 0.7rem;
  color: #9ca3af;
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

@media (max-width: 560px) {
  .direction {
    grid-template-columns: 1fr auto;
  }

  .dir-state {
    grid-column: 1 / -1;
  }

  .word-head {
    grid-template-columns: auto auto 1fr auto;
  }

  .english {
    display: none;
  }
}
</style>
