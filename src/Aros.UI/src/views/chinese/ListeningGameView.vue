<template>
  <div class="game">
    <p v-if="loading" class="status">Building your round…</p>

    <p v-else-if="error" class="status error">
      {{ error }}
      <RouterLink to="/chinese-listening">Back</RouterLink>
    </p>

    <!-- Final score -->
    <section v-else-if="finished" class="scorecard">
      <p class="score-label">Round complete</p>
      <p class="score">{{ correctCount }}<span class="score-total">/{{ questions.length }}</span></p>
      <p class="score-note">{{ verdict }}</p>

      <!-- The misses, while they are still fresh. A number alone teaches nothing. -->
      <div v-if="missed.length" class="review">
        <h2>Worth another listen</h2>
        <ul class="missed">
          <li v-for="(miss, i) in missed" :key="i">
            <button class="m-play" title="Play it again" @click="playMiss(miss)">🔊</button>
            <span class="m-sentence" lang="zh">{{ miss.sentence }}</span>
            <span v-if="miss.expected" class="m-expected">{{ miss.expected }}</span>
          </li>
        </ul>
      </div>

      <div class="actions">
        <button v-if="missed.length" class="primary" @click="drill">
          Drill {{ missed.length === 1 ? 'it' : missed.length + ' of these' }}
        </button>
        <button class="secondary" @click="loadQuiz">Play again</button>
        <RouterLink to="/chinese-listening" class="secondary">Done</RouterLink>
      </div>
    </section>

    <!-- A question -->
    <section v-else class="round">
      <header class="progress">
        <span>Question {{ index + 1 }} / {{ questions.length }}</span>
        <span class="tally">{{ correctCount }} correct</span>
      </header>

      <div class="listen-row">
        <button class="listen" title="Play the clip" @click="replay()">🔊</button>
        <!-- Same recording, slower. Mishearing a tone is a hearing problem, not a knowledge one,
             and the fix is to hear it again with room between the syllables. -->
        <button class="slow" title="Play it slowly" @click="replay(SLOW)">🐢 {{ SLOW }}×</button>
      </div>

      <p class="mode-label">{{ MODE_LABELS[mode] }}</p>

      <!-- Build it from tiles: which characters, and in what order -->
      <div v-if="mode === 'Ordering'" class="tiles">
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
          <span v-if="!built.length" class="built-hint">Tap the characters in the order you heard them</span>
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
              <span v-if="i < 9" class="key">{{ i + 1 }}</span>
            </button>
          </li>
        </ul>

        <div class="tile-actions">
          <button v-if="!answer" class="ghost" :disabled="!built.length" @click="clearBuilt">Clear</button>
          <button v-if="!answer" class="primary" :disabled="!built.length" @click="submitBuilt">Check</button>
        </div>
      </div>

      <!-- Pick the sentence -->
      <ul v-if="!typed && mode !== 'Ordering'" class="options">
        <li v-for="option in current.options" :key="option.clipId">
          <button
            class="option"
            :class="optionClass(option)"
            :disabled="!!answer"
            lang="zh"
            @click="choose(option)"
          >
            {{ option.sentence }}
          </button>
        </li>
      </ul>

      <!-- 他 and 她 are one sound, so a translation is a coin flip without this -->
      <ul v-if="current.hints" class="hints">
        <li v-for="hint in current.hints" :key="hint.character">
          <span lang="zh" class="hint-char">{{ hint.character }}</span>
          <span class="hint-not">not <span lang="zh">{{ [...hint.alternatives].join(' / ') }}</span></span>
        </li>
      </ul>

      <!-- Write what you heard -->
      <form v-if="typed" class="typed" @submit.prevent="submitTyped">
        <input
          ref="field"
          v-model="text"
          :placeholder="mode === 'Pinyin' ? 'wo3 he1 shui3' : 'I drink water'"
          :disabled="!!answer"
          autocapitalize="none"
          autocomplete="off"
          spellcheck="false"
        />
        <button v-if="!answer" type="submit" class="primary" :disabled="!text.trim()">Check</button>
      </form>

      <div v-if="answer" class="feedback">
        <p :class="answer.correct ? 'right' : 'wrong'">
          {{ answer.correct ? '✓ Correct' : '✗ Not quite' }}
        </p>
        <p v-if="answer.note" class="note">{{ answer.note }}</p>
        <p v-if="typed" class="expected">
          <span lang="zh">{{ answer.correctSentence }}</span>
          <span class="expected-answer">{{ answer.expected }}</span>
        </p>
        <div class="feedback-actions">
          <!-- A stored translation is one wording of many, so the last word is yours -->
          <button v-if="canOverride" class="overrule" @click="overrule">I was right</button>

          <button v-if="!autoAdvancing" ref="nextButton" class="primary" @click="next">
            {{ index + 1 === questions.length ? 'See score' : 'Next' }}
          </button>
        </div>
      </div>
    </section>

    <audio ref="player" />
  </div>
</template>

<script setup>
import { computed, nextTick, onMounted, onUnmounted, ref } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { api } from '@/services/api'
import { clip, prefetch, release } from '@/services/audio'

/** Slow enough to separate the syllables, fast enough to still sound like speech. */
const SLOW = 0.7

// Long enough to register the ✓, short enough that typing does not stall on it
const CORRECT_PAUSE = 1000

const MODE_LABELS = {
  Characters: 'Pick what you heard',
  Ordering: 'Build what you heard',
  Pinyin: 'Write the pinyin',
  English: 'Write the English',
}

const route = useRoute()

const questions = ref([])
const index = ref(0)
const answer = ref(null)
const text = ref('')
const mode = ref('Characters')
const typed = ref(false)
const correctCount = ref(0)
const finished = ref(false)

// What was missed in this round, in the order it was missed, kept for the scorecard and the
// drill that follows it
const missed = ref([])

// Indexes into current.tiles, in the order they were tapped — indexes rather than characters, so
// two tiles showing the same character stay distinguishable
const used = ref([])

const built = computed(() => used.value.map((i) => current.value?.tiles?.[i] ?? ''))
const loading = ref(true)
const error = ref('')
const player = ref(null)
const field = ref(null)
const nextButton = ref(null)
let advance = null

// A click on 🔊 while a clip is still being fetched must win over the fetch it interrupted
let playing = 0

const current = computed(() => questions.value[index.value])

// A right answer in a writing mode moves on by itself; everything else waits for Next
const autoAdvancing = computed(() => !!answer.value?.correct && mode.value !== 'Characters')

// Only a translation can be overruled: pinyin is marked exactly on purpose, and picking the
// sentence has one right answer with nothing to argue about.
const canOverride = computed(() => mode.value === 'English' && answer.value && !answer.value.correct)

const verdict = computed(() => {
  const ratio = correctCount.value / questions.value.length
  if (ratio === 1) return '完美 — perfect round.'
  if (ratio >= 0.7) return 'Solid. The ones you missed will come back sooner.'
  return 'Rough round — those sentences are now weighted to reappear.'
})

/** The ones just missed, heard again. Same round machinery, a different way of choosing it. */
async function drill() {
  await loadQuiz(() =>
    api.post('/listening/quiz/drill', { clipIds: missed.value.map((m) => m.clipId), mode: mode.value })
  )
}

/** A clip from the scorecard: the round is over, so this plays it without asking anything. */
async function playMiss(miss) {
  const el = player.value
  if (!el) return

  try {
    el.pause()
    el.src = await clip(miss.audioUrl)
    el.playbackRate = 1
    await el.play()
  } catch {
    // Nothing to recover: the round is finished and the button can be pressed again
  }
}

async function loadQuiz(build = null) {
  clearTimeout(advance)
  advance = null
  loading.value = true
  error.value = ''
  finished.value = false
  answer.value = null
  text.value = ''
  index.value = 0
  correctCount.value = 0
  missed.value = []
  used.value = []

  try {
    // Length is decided server-side: every clip not resting, or ten of them
    const params = new URLSearchParams({ questions: '10', mode: route.query.mode ?? 'Characters' })
    if (route.query.sweep === 'false') params.set('sweep', 'false')

    const quiz = await (build ? build() : api.post(`/listening/quiz?${params}`))
    mode.value = quiz.mode
    typed.value = quiz.typed
    questions.value = quiz.questions
    await nextTick()
    replay()
    warmNext()
    field.value?.focus()
  } catch (e) {
    error.value = e.message
  } finally {
    loading.value = false
  }
}

async function replay(rate = 1) {
  const el = player.value
  if (!el || !current.value) return

  const mine = ++playing

  try {
    const src = await clip(current.value.audioUrl)
    if (mine !== playing) return          // a later question or click asked for something else

    el.pause()
    el.src = src
    el.playbackRate = rate
    // Autoplay can be refused before the page has seen a gesture — the 🔊 button is the fallback
    await el.play()
  } catch {
    // A refused autoplay and a failed fetch look the same from here, and both end at the button
  }
}

/** The clip after this one, fetched now so its first syllable is never waited on. */
function warmNext() {
  const upcoming = questions.value[index.value + 1]
  if (upcoming) prefetch(upcoming.audioUrl)
}

async function choose(option) {
  if (answer.value) return
  await send({ selectedClipId: option.clipId })
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
 * Building a sentence with no mouse. Only the first nine tiles get a key — a long sentence runs
 * past the digits, and inventing a second row of keys for the tail would be worse than clicking it.
 */
function onKey(event) {
  if (loading.value || finished.value || answer.value) return
  if (mode.value !== 'Ordering') return
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
    const result = await api.post('/listening/answer', { token: current.value.token, ...payload })
    answer.value = { ...result, selectedClipId: payload.selectedClipId }

    if (!result.correct) {
      missed.value = [
        ...missed.value,
        {
          clipId: result.correctClipId,
          sentence: result.correctSentence,
          expected: result.expected,
          audioUrl: current.value.audioUrl,
        },
      ]
    }

    if (result.correct) {
      correctCount.value++

      // Right answers carry nothing to read, so hold the ✓ briefly and move on. A miss
      // waits: the sentence and its expected answer are the whole point of showing it.
      if (mode.value !== 'Characters') advance = setTimeout(next, CORRECT_PAUSE)
    }

    // Enter now works the Next button, so a whole round needs no mouse
    if (!advance) await nextTick(() => nextButton.value?.focus())
  } catch (e) {
    error.value = e.message
  }
}

async function overrule() {
  try {
    const result = await api.post('/listening/override', { token: current.value.token })
    answer.value = { ...answer.value, ...result }
    correctCount.value++

    advance = setTimeout(next, CORRECT_PAUSE)
  } catch (e) {
    error.value = e.message
  }
}

function optionClass(option) {
  if (!answer.value) return ''
  if (option.clipId === answer.value.correctClipId) return 'right'
  if (option.clipId === answer.value.selectedClipId) return 'wrong'
  return 'dimmed'
}

async function next() {
  clearTimeout(advance)
  advance = null

  answer.value = null
  text.value = ''
  used.value = []

  if (index.value + 1 >= questions.value.length) {
    finished.value = true
    return
  }

  index.value++
  await nextTick()
  replay()
  warmNext()
  field.value?.focus()
}

onMounted(() => {
  window.addEventListener('keydown', onKey)
  loadQuiz()
})
onUnmounted(() => {
  window.removeEventListener('keydown', onKey)
  clearTimeout(advance)
  release()
})
</script>

<style scoped>
.game {
  max-width: 560px;
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
  gap: 1.5rem;
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

.listen-row {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.6rem;
}

.slow {
  font-family: inherit;
  font-size: 0.8rem;
  padding: 0.45rem 0.7rem;
  border: 1px solid #e5e7eb;
  border-radius: 999px;
  background: white;
  color: #4b5563;
  cursor: pointer;
  white-space: nowrap;
}

.slow:hover {
  border-color: #6d5bd0;
  color: #6d5bd0;
}

.listen {
  width: 88px;
  height: 88px;
  border-radius: 50%;
  border: none;
  background: #6d5bd0;
  color: white;
  font-size: 2rem;
  cursor: pointer;
  box-shadow: 0 6px 18px rgba(109, 91, 208, 0.28);
  transition: transform 0.15s;
}

.listen:hover {
  transform: translateY(-2px);
}

.mode-label {
  font-size: 0.72rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.07em;
  color: #9ca3af;
}

/* Not a giveaway but a fair chance: the sound alone cannot tell these apart */
.tiles {
  display: flex;
  flex-direction: column;
  gap: 0.8rem;
}

/* What you have built so far, and the only place order is visible */
.built {
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem;
  min-height: 3.4rem;
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
  font-size: 0.76rem;
  color: #9ca3af;
}

.built-tile {
  font-family: inherit;
  font-size: 1.4rem;
  line-height: 1;
  padding: 0.3rem 0.45rem;
  border: 1px solid #6d5bd0;
  border-radius: 8px;
  background: white;
  color: #1a1a1a;
  cursor: pointer;
}

.bank {
  list-style: none;
  display: flex;
  flex-wrap: wrap;
  gap: 0.45rem;
  justify-content: center;
}

.tile {
  position: relative;
  font-family: inherit;
  font-size: 1.5rem;
  line-height: 1;
  padding: 0.45rem 0.6rem;
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

.key {
  position: absolute;
  top: 0.08rem;
  right: 0.2rem;
  font-size: 0.55rem;
  font-weight: 600;
  color: #b8bcc4;
  line-height: 1;
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

.hints {
  list-style: none;
  display: flex;
  flex-wrap: wrap;
  justify-content: center;
  gap: 0.5rem;
}

.hints li {
  display: flex;
  align-items: baseline;
  gap: 0.4rem;
  padding: 0.35rem 0.7rem;
  background: #eff6ff;
  border: 1px solid #bfdbfe;
  border-radius: 999px;
}

.hint-char {
  font-size: 1.25rem;
  font-weight: 600;
  color: #1e40af;
}

.hint-not {
  font-size: 0.75rem;
  color: #60769c;
}

.typed {
  display: flex;
  gap: 0.5rem;
  width: 100%;
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

.note {
  font-size: 0.85rem;
  color: #92400e;
  background: #fffbeb;
  border-radius: 6px;
  padding: 0.3rem 0.6rem;
}

.expected {
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
  font-size: 0.95rem;
  color: #4b5563;
}

.expected span:first-child {
  font-size: 1.3rem;
  font-weight: 600;
  color: #1a1a1a;
}

.expected-answer {
  font-size: 1rem;
}

.options {
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 0.65rem;
  width: 100%;
}

.option {
  width: 100%;
  padding: 1rem;
  font-family: inherit;
  font-size: 1.25rem;
  line-height: 1.5;
  text-align: center;
  background: white;
  border: 2px solid #e5e7eb;
  border-radius: 10px;
  color: #1a1a1a;
  cursor: pointer;
  transition: border-color 0.15s, background 0.15s;
}

.option:hover:not(:disabled) {
  border-color: #cba6f7;
}

.option:disabled {
  cursor: default;
}

.option.right {
  border-color: #22c55e;
  background: #f0fdf4;
}

.option.wrong {
  border-color: #ef4444;
  background: #fef2f2;
}

.option.dimmed {
  opacity: 0.45;
}

.feedback {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.9rem;
}

.feedback-actions {
  display: flex;
  align-items: center;
  gap: 0.6rem;
}

.overrule {
  padding: 0.55rem 1rem;
  font-family: inherit;
  font-size: 0.85rem;
  font-weight: 600;
  color: #15803d;
  background: white;
  border: 2px solid #bbf7d0;
  border-radius: 8px;
  cursor: pointer;
}

.overrule:hover {
  border-color: #22c55e;
}

.feedback .right {
  color: #15803d;
  font-weight: 600;
}

.feedback .wrong {
  color: #b91c1c;
  font-weight: 600;
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

.score-note {
  color: #6b7280;
  font-size: 0.9rem;
  margin-top: 0.5rem;
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
  grid-template-columns: auto 1fr;
  gap: 0.5rem 0.6rem;
  align-items: baseline;
  padding: 0.4rem 0.6rem;
  background: white;
  border: 1px solid #f0efec;
  border-radius: 7px;
}

.m-play {
  background: none;
  border: none;
  cursor: pointer;
  font-size: 0.9rem;
  padding: 0;
  line-height: 1;
}

.m-sentence {
  font-size: 1.05rem;
}

.m-expected {
  grid-column: 2;
  font-size: 0.78rem;
  color: #6d5bd0;
}

.actions {
  display: flex;
  align-items: center;
  gap: 1rem;
  margin-top: 1.5rem;
}

.primary {
  font-family: inherit;
  padding: 0.6rem 1.4rem;
  font-size: 0.9rem;
  font-weight: 600;
  color: white;
  background: #6d5bd0;
  border: none;
  border-radius: 8px;
  cursor: pointer;
}

.secondary {
  font: inherit;
  font-size: 0.9rem;
  color: #6b7280;
  text-decoration: none;
  padding: 0.6rem 1rem;
  border: 1px solid #e5e7eb;
  border-radius: 8px;
  background: white;
  cursor: pointer;
}

.secondary:hover {
  border-color: #6d5bd0;
  color: #6d5bd0;
}
</style>
