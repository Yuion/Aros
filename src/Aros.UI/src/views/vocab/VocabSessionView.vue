<template>
  <div class="session">
    <p v-if="loading" class="status">Building your round…</p>

    <p v-else-if="error" class="status error">
      {{ error }}
      <RouterLink to="/vocab">Back</RouterLink>
    </p>

    <!-- Score -->
    <section v-else-if="finished" class="scorecard">
      <p class="score-label">Round complete</p>
      <p class="score">{{ correctCount }}<span class="score-total">/{{ questions.length }}</span></p>

      <!-- The misses, while they are still fresh. A number alone teaches nothing. -->
      <div v-if="missed.length" class="review">
        <h2>Worth another look</h2>
        <ul class="missed">
          <li v-for="(miss, i) in missed" :key="i">
            <span class="m-chars" lang="zh">{{ miss.characters }}</span>
            <span class="m-expected">{{ miss.expected }}</span>
            <span class="m-direction">{{ label(miss.direction) }}</span>
          </li>
        </ul>
      </div>

      <div class="actions">
        <button v-if="missed.length" class="primary" @click="drill">
          Drill {{ missed.length === 1 ? 'it' : missed.length + ' of these' }}
        </button>
        <button class="secondary" @click="load">Again</button>
        <RouterLink to="/vocab" class="secondary">Done</RouterLink>
      </div>
    </section>

    <!-- A question -->
    <section v-else class="round">
      <header class="progress">
        <span>{{ index + 1 }} / {{ questions.length }}</span>
        <span class="tally">{{ correctCount }} correct</span>
      </header>

      <p class="direction">{{ current.promptLabel }} → {{ current.answerLabel }}</p>

      <p class="prompt" :lang="current.promptLabel === 'Characters' ? 'zh' : undefined">
        {{ current.prompt }}
      </p>

      <!-- Typed -->
      <form v-if="current.typed" class="typed" @submit.prevent="submitTyped">
        <input
          ref="field"
          v-model="text"
          :placeholder="current.answerLabel === 'Pinyin' ? 'ni3 hao3' : 'meaning'"
          :disabled="!!answer"
          autocapitalize="none"
          autocomplete="off"
          spellcheck="false"
        />
        <button v-if="!answer" type="submit" class="primary" :disabled="!text.trim()">Check</button>
      </form>

      <!-- Built from tiles: which characters, and in what order -->
      <div v-else class="tiles">
        <div class="built" :class="{ empty: !built.length }" lang="zh">
          <button
            v-for="(character, i) in built"
            :key="i"
            class="built-tile"
            :disabled="!!answer"
            title="Take it back"
            @click="takeBack(i)"
          >
            {{ character }}
          </button>
          <span v-if="!built.length" class="built-hint">Tap the characters in order</span>
        </div>

        <ul class="bank">
          <li v-for="(tile, i) in current.tiles" :key="i">
            <button
              class="tile"
              :disabled="!!answer || used.includes(i)"
              :class="{ used: used.includes(i) }"
              lang="zh"
              @click="place(i)"
            >
              {{ tile }}
              <!-- The number never moves, even once the tile is spent: a key that meant 早 a
                   moment ago must not come to mean 在 -->
              <span class="key">{{ i + 1 }}</span>
            </button>
          </li>
        </ul>

        <div class="tile-actions">
          <button v-if="!answer" class="ghost" :disabled="!built.length" @click="clearBuilt">Clear</button>
          <button v-if="!answer" class="primary" :disabled="!built.length" @click="submitBuilt">Check</button>
        </div>

        <p v-if="!answer" class="keys-hint">
          Keys <strong>1–{{ current.tiles.length }}</strong> place · <strong>Backspace</strong> takes
          back · <strong>Enter</strong> checks
        </p>
      </div>

      <!-- Right word, wrong form — one free retry, and nothing given away -->
      <p v-if="retry" class="retry">{{ retry }} Try again.</p>

      <!-- Feedback -->
      <div v-if="answer" class="feedback">
        <p :class="answer.correct ? 'right' : 'wrong'">
          {{ answer.correct ? '✓ Correct' : '✗ Not quite' }}
        </p>
        <p v-if="answer.note" class="note">{{ answer.note }}</p>
        <p v-if="!answer.correct" class="expected">
          <span lang="zh">{{ answer.characters }}</span> — {{ answer.expected }}
        </p>
        <button v-if="!autoAdvancing" ref="nextButton" class="primary" @click="next">
          {{ index + 1 === questions.length ? 'See score' : 'Next' }}
        </button>
      </div>
    </section>
  </div>
</template>

<script setup>
import { computed, nextTick, onMounted, onUnmounted, ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { api } from '@/services/api'

// CharactersToPinyin reads as an enum name, which is what it is — but not on a results page
function label(direction) {
  return (direction ?? '').replace(/([a-z])([A-Z])/g, '$1 $2').replace(' To ', ' → ')
}

// Long enough to register the ✓, short enough that typing does not stall on it
const CORRECT_PAUSE = 1000

const route = useRoute()

const questions = ref([])
const index = ref(0)
const answer = ref(null)
const retry = ref('')
const text = ref('')
const correctCount = ref(0)
const finished = ref(false)

// What was missed in this round, in the order it was missed, kept for the scorecard and for the
// drill that follows it
const missed = ref([])
const loading = ref(true)
const error = ref('')
const field = ref(null)
const nextButton = ref(null)

// Indexes into current.tiles, in the order they were tapped — indexes rather than characters, so
// two tiles showing the same character stay distinguishable
const used = ref([])

const built = computed(() => used.value.map((i) => current.value?.tiles?.[i] ?? ''))
let advance = null

const current = computed(() => questions.value[index.value])

// A right answer moves on by itself either way: there is nothing to read on a ✓
const autoAdvancing = computed(() => !!answer.value?.correct)

/** The ones just missed, asked again. Same round machinery, a different way of choosing it. */
async function drill() {
  const items = missed.value.map((m) => ({ wordId: m.wordId, direction: m.direction }))

  await load(() => api.post('/vocab/session/drill', { items }))
}

async function load(build = null) {
  clearTimeout(advance)
  advance = null
  loading.value = true
  error.value = ''
  finished.value = false
  answer.value = null
  retry.value = ''
  text.value = ''
  used.value = []
  index.value = 0
  correctCount.value = 0
  missed.value = []

  // Length is decided server-side: everything not resting, or a sample of it
  const params = new URLSearchParams()
  if (route.query.direction) params.set('direction', route.query.direction)
  if (route.query.tag) params.set('tag', route.query.tag)
  if (route.query.sweep === 'false') params.set('sweep', 'false')

  try {
    const session = await (build ? build() : api.post(`/vocab/session?${params}`))
    questions.value = session.questions
    await focusField()
  } catch (e) {
    error.value = e.message
  } finally {
    loading.value = false
  }
}

async function focusField() {
  await nextTick()
  field.value?.focus()
}

async function submitTyped() {
  if (answer.value || !text.value.trim()) return
  await send({ text: text.value })
}

function place(index) {
  if (answer.value || used.value.includes(index)) return
  used.value = [...used.value, index]
}

function takeBack(position) {
  if (answer.value) return
  used.value = used.value.filter((_, i) => i !== position)
}

function clearBuilt() {
  used.value = []
}

async function submitBuilt() {
  if (answer.value || !built.value.length) return
  await send({ text: built.value.join('') })
}

/**
 * A tile round with no mouse. Typed directions are left alone: the input has the focus there and
 * a digit is a tone number, so "hao3" must never be read as "place tile 3".
 */
function onKey(event) {
  if (loading.value || finished.value || answer.value || retry.value) return
  if (!current.value || current.value.typed) return
  if (event.ctrlKey || event.altKey || event.metaKey) return

  if (event.key >= '1' && event.key <= '9') {
    place(Number(event.key) - 1)
    event.preventDefault()
    return
  }

  if (event.key === 'Backspace') {
    takeBack(used.value.length - 1)
    event.preventDefault()               // otherwise the browser treats it as Back
    return
  }

  if (event.key === 'Escape') {
    clearBuilt()
    return
  }

  if (event.key === 'Enter') {
    submitBuilt()
    event.preventDefault()
  }
}

async function send(payload) {
  try {
    const result = await api.post('/vocab/answer', { token: current.value.token, ...payload })

    // A misread prompt is not a miss: the question stays open and nothing is scored
    if (result.retry) {
      retry.value = result.note
      text.value = ''
      await focusField()
      return
    }

    retry.value = ''
    answer.value = result

    if (!result.correct) {
      missed.value = [
        ...missed.value,
        {
          wordId: result.wordId,
          direction: current.value.direction,
          characters: result.characters,
          expected: result.expected,
        },
      ]
    }

    if (result.correct) {
      correctCount.value++

      // Right answers carry nothing to read, so hold the ✓ briefly and move on. A miss
      // waits: the expected answer is the whole point of showing it.
      advance = setTimeout(next, CORRECT_PAUSE)
    }

    // Enter now works the Next button, so a whole round needs no mouse
    if (!advance) await nextTick(() => nextButton.value?.focus())
  } catch (e) {
    error.value = e.message
  }
}


async function next() {
  clearTimeout(advance)
  advance = null

  answer.value = null
  retry.value = ''
  text.value = ''
  used.value = []

  if (index.value + 1 >= questions.value.length) {
    finished.value = true
    return
  }

  index.value++
  await focusField()
}

onMounted(() => {
  window.addEventListener('keydown', onKey)
  load()
})
onUnmounted(() => {
  window.removeEventListener('keydown', onKey)
  clearTimeout(advance)
})
</script>

<style scoped>
.session {
  max-width: 520px;
  margin: 0 auto;
}

.status {
  text-align: center;
  color: #6b7280;
  padding: 3rem 0;
}

.status.error {
  color: #b91c1c;
}

.status a {
  display: block;
  margin-top: 0.75rem;
  color: #6d5bd0;
}

.round {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1rem;
}

.progress {
  display: flex;
  justify-content: space-between;
  width: 100%;
  font-size: 0.8rem;
  color: #6b7280;
}

.tally {
  font-weight: 600;
  color: #6d5bd0;
}

.direction {
  font-size: 0.72rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.07em;
  color: #9ca3af;
  margin-top: 1rem;
}

.prompt {
  font-size: 2.4rem;
  font-weight: 600;
  line-height: 1.3;
  text-align: center;
  word-break: break-word;
}

.prompt:lang(zh) {
  font-size: 3rem;
}

.typed {
  display: flex;
  gap: 0.5rem;
  width: 100%;
  margin-top: 0.5rem;
}

.typed input {
  flex: 1;
  min-width: 0;
  padding: 0.7rem 0.85rem;
  font-family: inherit;
  font-size: 1.1rem;
  border: 2px solid #e5e7eb;
  border-radius: 10px;
  background: white;
  color: #1a1a1a;
}

.typed input:focus {
  outline: none;
  border-color: #cba6f7;
}








.feedback {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.6rem;
  margin-top: 0.5rem;
  text-align: center;
}

.feedback .right {
  color: #15803d;
  font-weight: 600;
}

.feedback .wrong {
  color: #b91c1c;
  font-weight: 600;
}

.tiles {
  display: flex;
  flex-direction: column;
  gap: 0.8rem;
}

/* What you have built so far, and the only place order is visible */
.built {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
  min-height: 3.6rem;
  padding: 0.5rem;
  border: 1px dashed #d8d5ea;
  border-radius: 10px;
  background: #fbfaff;
  align-items: center;
}

.built.empty {
  justify-content: center;
}

.built-hint {
  font-size: 0.78rem;
  color: #9ca3af;
}

.built-tile {
  font-family: inherit;
  font-size: 1.6rem;
  line-height: 1;
  padding: 0.35rem 0.55rem;
  border: 1px solid #6d5bd0;
  border-radius: 8px;
  background: white;
  color: #1a1a1a;
  cursor: pointer;
}

.built-tile:disabled {
  cursor: default;
}

.bank {
  list-style: none;
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  justify-content: center;
}

.tile {
  position: relative;
  font-family: inherit;
  font-size: 1.7rem;
  line-height: 1;
  padding: 0.5rem 0.7rem;
  border: 1px solid #e5e7eb;
  border-radius: 10px;
  background: white;
  color: #1a1a1a;
  cursor: pointer;
  transition: border-color 0.12s, transform 0.12s;
}

.tile:hover:not(:disabled) {
  border-color: #6d5bd0;
  transform: translateY(-1px);
}

/* Spent tiles hold their place: a gap that moves is a hint about what you took */
.tile.used {
  opacity: 0.25;
  cursor: default;
}

.tile:disabled {
  cursor: default;
}

/* Small enough to ignore when you are using the mouse, there when you want it */
.key {
  position: absolute;
  top: 0.1rem;
  right: 0.22rem;
  font-size: 0.6rem;
  font-weight: 600;
  color: #b8bcc4;
  line-height: 1;
}

.tile:hover:not(:disabled) .key {
  color: #6d5bd0;
}

.keys-hint {
  text-align: center;
  font-size: 0.7rem;
  color: #9ca3af;
}

.keys-hint strong {
  font-weight: 600;
  color: #6b7280;
}

.tile-actions {
  display: flex;
  gap: 0.5rem;
  justify-content: center;
}

.ghost {
  font-family: inherit;
  font-size: 0.85rem;
  padding: 0.5rem 0.9rem;
  border: 1px solid #e5e7eb;
  border-radius: 8px;
  background: white;
  color: #4b5563;
  cursor: pointer;
}

.ghost:disabled {
  opacity: 0.5;
  cursor: default;
}

.retry {
  font-size: 0.9rem;
  font-weight: 600;
  color: #92400e;
  background: #fffbeb;
  border: 1px solid #fde68a;
  border-radius: 8px;
  padding: 0.45rem 0.7rem;
  text-align: center;
}

.note {
  font-size: 0.85rem;
  color: #92400e;
  background: #fffbeb;
  border-radius: 6px;
  padding: 0.3rem 0.6rem;
}

.expected {
  font-size: 0.95rem;
  color: #4b5563;
}

.expected span {
  font-size: 1.15rem;
  font-weight: 600;
  color: #1a1a1a;
}

.scorecard {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.5rem;
  padding: 3rem 0;
  text-align: center;
}

.score-label {
  font-size: 0.85rem;
  color: #6b7280;
  text-transform: uppercase;
  letter-spacing: 0.08em;
}

.score {
  font-size: 4rem;
  font-weight: 700;
  color: #6d5bd0;
  line-height: 1;
}

.score-total {
  font-size: 2rem;
  color: #9ca3af;
}

.review {
  width: 100%;
  margin-bottom: 1.2rem;
}

.review h2 {
  font-size: 0.72rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: #9ca3af;
  margin-bottom: 0.5rem;
}

.missed {
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
  text-align: left;
}

.missed li {
  display: grid;
  grid-template-columns: auto 1fr auto;
  gap: 0.6rem;
  align-items: baseline;
  padding: 0.4rem 0.6rem;
  background: white;
  border: 1px solid #f0efec;
  border-radius: 7px;
}

.m-chars {
  font-size: 1.2rem;
  font-weight: 600;
}

.m-expected {
  font-size: 0.85rem;
  color: #6d5bd0;
}

.m-direction {
  font-size: 0.65rem;
  color: #b8bcc4;
}

.actions {
  display: flex;
  align-items: center;
  gap: 1rem;
  margin-top: 1.5rem;
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
  flex-shrink: 0;
}

.primary:disabled {
  background: #c7c4d6;
  cursor: not-allowed;
}

.secondary {
  font-size: 0.9rem;
  color: #6b7280;
  text-decoration: none;
}
</style>
