<template>
  <div class="session">
    <p v-if="loading && !cards.length" class="status">Building your session…</p>

    <p v-else-if="error && !cards.length" class="status error">
      {{ error }}
      <RouterLink to="/daily">Back</RouterLink>
    </p>

    <!-- The end of a daily session, or of a drill that followed one -->
    <section v-else-if="finished" class="scorecard">
      <p class="score-label">{{ drilling ? 'Drill complete' : 'Session complete' }}</p>
      <p class="score">{{ correctCount }}<span class="score-total">/{{ answered }}</span></p>
      <p class="score-note">{{ verdict }}</p>

      <div v-if="missed.length" class="review">
        <h2>Worth another look</h2>
        <ul class="missed">
          <li v-for="(miss, i) in missed" :key="i">
            <span class="m-track">{{ short(miss.label) }}</span>
            <span class="m-prompt" :lang="miss.chinese ? 'zh' : undefined">{{ miss.prompt }}</span>
            <span class="m-answer" lang="zh">{{ miss.expected }}</span>
          </li>
        </ul>
      </div>

      <div class="actions">
        <button v-if="missed.length" class="primary" @click="drill">
          Drill {{ missed.length === 1 ? 'it' : missed.length + ' mistakes' }}
        </button>
        <RouterLink to="/daily" class="secondary">Done</RouterLink>
      </div>
    </section>

    <!-- A card -->
    <section v-else-if="current" class="round">
      <header class="progress">
        <span v-if="endless">{{ answered }} answered · endless</span>
        <span v-else>{{ index + 1 }} / {{ cards.length }}{{ drilling ? ' · drill' : '' }}</span>
        <span class="tally">{{ correctCount }} correct</span>
      </header>

      <div class="bar" aria-hidden="true">
        <div class="fill" :style="{ width: progress }"></div>
      </div>

      <p class="track-label">{{ current.label }}</p>

      <!-- Listening: the clip is the question -->
      <div v-if="current.kind === 'listening'" class="listen-row">
        <button class="listen" title="Play the clip" @click="replay()">🔊</button>
        <button class="slow" title="Play it slowly" @click="replay(SLOW)">🐢 {{ SLOW }}×</button>
      </div>

      <!-- Grammar names the pattern being tested; vocabulary and grammar both show a prompt -->
      <p v-if="current.pattern" class="pattern">{{ current.pattern }}</p>

      <p
        v-if="current.prompt"
        class="prompt"
        :lang="current.promptLabel === 'Characters' ? 'zh' : undefined"
      >
        {{ current.prompt }}
      </p>

      <!-- Typed: pinyin or English -->
      <form v-if="current.typed" class="typed" @submit.prevent="submitTyped">
        <input
          ref="field"
          v-model="text"
          :placeholder="placeholder"
          :disabled="!!answer"
          autocapitalize="none"
          autocomplete="off"
          spellcheck="false"
        />
        <button v-if="!answer" type="submit" class="primary" :disabled="!text.trim()">Check</button>
      </form>

      <!-- Built from tiles -->
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
          <span v-if="!built.length" class="built-hint">{{ buildHint }}</span>
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

      <!-- 他 and 她 are one sound, so a translation is a coin flip without this -->
      <ul v-if="current.hints?.length" class="hints">
        <li v-for="hint in current.hints" :key="hint.character">
          <span lang="zh" class="hint-char">{{ hint.character }}</span>
          <span class="hint-not">not <span lang="zh">{{ [...hint.alternatives].join(' / ') }}</span></span>
        </li>
      </ul>

      <!-- Right word, wrong form — vocabulary gives one free retry -->
      <p v-if="retry" class="retry">{{ retry }} Try again.</p>

      <div v-if="answer" class="feedback">
        <p :class="answer.correct ? 'right' : 'wrong'">
          {{ answer.correct ? '✓ Correct' : '✗ Not quite' }}
        </p>
        <p v-if="answer.note" class="note">{{ answer.note }}</p>
        <p v-if="!answer.correct" class="expected">
          <span v-if="answer.characters" lang="zh">{{ answer.characters }}</span>
          <span v-else-if="answer.correctSentence" lang="zh">{{ answer.correctSentence }}</span>
          <span v-if="answer.expected" class="expected-text">{{ answer.expected }}</span>
        </p>

        <button v-if="canOverride" class="ghost" @click="override">I was right</button>
        <button v-if="!autoAdvancing" ref="nextButton" class="primary" @click="next">
          {{ lastCard ? 'See score' : 'Next' }}
        </button>
      </div>

      <p v-if="endless" class="stop-row">
        <button class="secondary" @click="stop">Stop here</button>
      </p>

      <p v-if="error" class="status error inline">{{ error }}</p>
    </section>

    <audio ref="player" preload="auto"></audio>
  </div>
</template>

<script setup>
import { computed, nextTick, onMounted, onUnmounted, ref } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { api } from '@/services/api'
import { clip, prefetch, release } from '@/services/audio'

/** Slow enough to separate the syllables, fast enough to still sound like speech. */
const SLOW = 0.7

// Long enough to register the ✓, short enough that a session does not stall on it
const CORRECT_PAUSE = 1000

// How many recent items endless practice keeps out of the next batch. Long enough that a word you
// keep missing does not come round every fourth question, short enough to still come back.
const COOLDOWN = 30

// Fetch the next batch this far before running out, so endless practice never pauses
const REFILL_AT = 4

const route = useRoute()
const router = useRouter()

const endless = route.query.mode === 'endless'

const cards = ref([])
const index = ref(0)
const answer = ref(null)
const retry = ref('')
const text = ref('')
const used = ref([])
const correctCount = ref(0)
const answered = ref(0)
const missed = ref([])
const finished = ref(false)
const drilling = ref(false)
const loading = ref(true)
const error = ref('')
const refilling = ref(false)

const player = ref(null)
const field = ref(null)
const nextButton = ref(null)
let advance = null

// A click on 🔊 while a clip is still being fetched must win over the fetch it interrupted
let playing = 0

// What endless practice has asked lately, newest last, so a batch can exclude it
const recent = ref([])

const current = computed(() => cards.value[index.value])
const built = computed(() => used.value.map((i) => current.value?.tiles?.[i] ?? ''))
const lastCard = computed(() => !endless && index.value + 1 >= cards.value.length)

const progress = computed(() =>
  endless ? '100%' : `${Math.round(((index.value + 1) / Math.max(1, cards.value.length)) * 100)}%`,
)

// A right answer moves on by itself; a wrong one waits, because the answer is the point
const autoAdvancing = computed(() => !!answer.value?.correct)

// Only a translation can be overruled: pinyin is marked exactly on purpose, and a built sentence
// has one right order with nothing to argue about
const canOverride = computed(
  () => current.value?.kind === 'listening' && current.value?.mode === 'English'
    && answer.value && !answer.value.correct,
)

const placeholder = computed(() => {
  if (current.value?.kind === 'listening') return current.value.mode === 'Pinyin' ? 'wo3 he1 shui3' : 'I drink water'
  return current.value?.answerLabel === 'Pinyin' ? 'ni3 hao3' : 'meaning'
})

const buildHint = computed(() =>
  current.value?.kind === 'listening'
    ? 'Tap the characters in the order you heard them'
    : 'Build the sentence',
)

const verdict = computed(() => {
  if (!answered.value) return ''
  const ratio = correctCount.value / answered.value
  if (ratio === 1) return '完美 — nothing missed.'
  if (ratio >= 0.8) return 'Solid. The misses come back sooner than the rest.'
  return 'Rough one — those items are now weighted to reappear.'
})

function short(label) {
  return (label ?? '').split('·')[0].trim()
}

// ----------------------------------------------------------------- the tiles

function place(i) {
  if (answer.value || used.value.includes(i)) return
  used.value = [...used.value, i]
}

function takeBack(position) {
  if (answer.value) return
  used.value = used.value.filter((_, i) => i !== position)
}

function clearBuilt() {
  used.value = []
}

// ---------------------------------------------------------------- answering

/** Each kind is judged by its own trainer: this only decides which door to knock on. */
function endpoint(kind) {
  return { listening: '/listening/answer', vocab: '/vocab/answer', grammar: '/grammar/answer' }[kind]
}

async function send(payload) {
  if (answer.value) return

  try {
    const result = await api.post(endpoint(current.value.kind), {
      token: current.value.token,
      ...payload,
    })

    // Vocabulary's one free retry for answering the previous question's direction
    if (result.retry) {
      retry.value = result.note ?? ''
      text.value = ''
      used.value = []
      await nextTick(() => field.value?.focus())
      return
    }

    retry.value = ''
    answer.value = result
    answered.value++

    if (result.correct) correctCount.value++
    else remember(result)

    if (result.correct) advance = setTimeout(next, CORRECT_PAUSE)
    else await nextTick(() => nextButton.value?.focus())
  } catch (e) {
    error.value = e.message
  }
}

function submitTyped() {
  if (!text.value.trim()) return
  send({ text: text.value })
}

function submitBuilt() {
  if (!built.value.length) return
  send({ text: built.value.join('') })
}

/** What was missed, in the shape the drill and the cooldown both want. */
function remember(result) {
  const card = current.value

  missed.value = [
    ...missed.value,
    {
      kind: card.kind,
      id: result.wordId ?? result.correctClipId ?? result.pointId,
      mode: card.mode,
      direction: card.direction,
      label: card.label,
      prompt: card.prompt ?? result.correctSentence ?? card.pattern,
      chinese: card.promptLabel === 'Characters',
      expected: result.characters || result.correctSentence || result.expected,
    },
  ]
}

/** "I was right after all" — only for a translation the matcher rejected. */
async function override() {
  try {
    const result = await api.post('/listening/override', { token: current.value.token })
    answer.value = { ...answer.value, ...result, correct: true }
    correctCount.value++
    missed.value = missed.value.slice(0, -1)
  } catch (e) {
    error.value = e.message
  }
}

// ------------------------------------------------------------------ moving on

async function next() {
  clearTimeout(advance)
  advance = null

  answer.value = null
  retry.value = ''
  text.value = ''
  used.value = []

  if (index.value + 1 >= cards.value.length) {
    if (!endless) {
      finished.value = true
      return
    }

    await refill()
    if (index.value + 1 >= cards.value.length) return      // refill failed; the error is on screen
  }

  index.value++
  await open()

  if (endless && cards.value.length - index.value <= REFILL_AT) refill()
}

/** Whatever the new card needs before it can be answered: sound, or a cursor. */
async function open() {
  await nextTick()

  if (current.value?.kind === 'listening') {
    replay()
    warmNext()
  } else if (current.value?.typed) {
    field.value?.focus()
  }
}

function stop() {
  finished.value = true
}

// --------------------------------------------------------------------- audio

async function replay(rate = 1) {
  const el = player.value
  if (!el || current.value?.kind !== 'listening') return

  const mine = ++playing

  try {
    const src = await clip(current.value.audioUrl)
    if (mine !== playing) return          // a later card or click asked for something else

    el.pause()
    el.src = src
    el.playbackRate = rate
    await el.play()
  } catch {
    // A refused autoplay and a failed fetch look the same from here, and both end at the button
  }
}

/** The next clip, fetched now so its first syllable is never waited on. */
function warmNext() {
  const upcoming = cards.value.slice(index.value + 1, index.value + 4).find((c) => c.audioUrl)
  if (upcoming) prefetch(upcoming.audioUrl)
}

// ------------------------------------------------------------------- loading

function key(card) {
  return { kind: card.kind, id: 0, mode: card.mode, direction: card.direction }
}

async function load() {
  loading.value = true
  error.value = ''

  try {
    const session = endless
      ? await api.post('/daily/endless', { count: 12, recent: recent.value })
      : await api.post('/daily/session')

    cards.value = session.cards
    index.value = 0
    if (endless) recent.value = session.cards.map(key).slice(-COOLDOWN)
    await open()
  } catch (e) {
    error.value = e.message
  } finally {
    loading.value = false
  }
}

/** Endless practice: another batch, minus what was just asked. */
async function refill() {
  if (refilling.value) return
  refilling.value = true

  try {
    const session = await api.post('/daily/endless', { count: 12, recent: recent.value })

    cards.value = [...cards.value, ...session.cards]
    recent.value = [...recent.value, ...session.cards.map(key)].slice(-COOLDOWN)
  } catch (e) {
    error.value = e.message
  } finally {
    refilling.value = false
  }
}

/** Everything missed, asked again — the same game, a different way of choosing it. */
async function drill() {
  loading.value = true
  error.value = ''

  try {
    const session = await api.post('/daily/drill', {
      misses: missed.value.map((m) => ({
        kind: m.kind,
        id: m.id,
        mode: m.mode,
        direction: m.direction,
      })),
    })

    cards.value = session.cards
    index.value = 0
    answer.value = null
    retry.value = ''
    text.value = ''
    used.value = []
    correctCount.value = 0
    answered.value = 0
    missed.value = []
    finished.value = false
    drilling.value = true
    await open()
  } catch (e) {
    error.value = e.message
  } finally {
    loading.value = false
  }
}

// ------------------------------------------------------------------ keyboard

/** A session with no mouse: digits place tiles, Enter checks, Backspace takes one back. */
function onKey(event) {
  if (loading.value || finished.value) return
  if (event.ctrlKey || event.altKey || event.metaKey) return

  if (answer.value) {
    if (event.key === 'Enter' && !autoAdvancing.value) {
      next()
      event.preventDefault()
    }
    return
  }

  if (current.value?.typed) return       // the field owns the keyboard while typing

  if (event.key >= '1' && event.key <= '9') {
    place(Number(event.key) - 1)
    event.preventDefault()
    return
  }

  if (event.key === 'Backspace') {
    takeBack(used.value.length - 1)
    event.preventDefault()
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

onMounted(() => {
  window.addEventListener('keydown', onKey)
  load()
})

onUnmounted(() => {
  window.removeEventListener('keydown', onKey)
  clearTimeout(advance)
  release()
})
</script>

<style scoped>
.session {
  max-width: 34rem;
  margin: 0 auto;
}

.status {
  color: #6b7280;
}

.status.error {
  color: #b91c1c;
}

.status.inline {
  font-size: 0.8rem;
  margin-top: 0.6rem;
}

.round,
.scorecard {
  background: #fff;
  border-radius: 14px;
  padding: 1.1rem 1.2rem 1.3rem;
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.06);
}

.progress {
  display: flex;
  justify-content: space-between;
  font-size: 0.75rem;
  color: #6b7280;
}

.bar {
  height: 4px;
  border-radius: 3px;
  background: #f1f3f6;
  overflow: hidden;
  margin: 0.4rem 0 0.9rem;
}

.fill {
  height: 100%;
  background: #6366f1;
  transition: width 0.2s ease;
}

.track-label {
  margin: 0 0 0.7rem;
  font-size: 0.72rem;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: #6b7280;
}

.listen-row {
  display: flex;
  gap: 0.5rem;
  align-items: center;
  margin-bottom: 0.9rem;
}

.listen,
.slow {
  border: none;
  border-radius: 10px;
  cursor: pointer;
  background: #eef2ff;
  color: #3730a3;
  font-size: 1.1rem;
  padding: 0.55rem 0.9rem;
}

.slow {
  font-size: 0.8rem;
}

.pattern {
  margin: 0 0 0.2rem;
  font-size: 0.8rem;
  color: #3730a3;
  font-weight: 600;
}

.prompt {
  margin: 0 0 0.9rem;
  font-size: 1.35rem;
  font-weight: 600;
}

.prompt[lang='zh'] {
  font-size: 2rem;
}

.typed {
  display: flex;
  gap: 0.5rem;
}

.typed input {
  flex: 1;
  padding: 0.6rem 0.7rem;
  border: 1px solid #d5d8de;
  border-radius: 9px;
  font: inherit;
  font-size: 1rem;
}

.built {
  min-height: 3rem;
  border: 1px dashed #d5d8de;
  border-radius: 10px;
  padding: 0.4rem;
  display: flex;
  flex-wrap: wrap;
  gap: 0.3rem;
  align-items: center;
  margin-bottom: 0.7rem;
}

.built-hint {
  color: #9ca3af;
  font-size: 0.8rem;
  padding: 0 0.3rem;
}

.built-tile {
  font-size: 1.5rem;
  padding: 0.25rem 0.55rem;
  border-radius: 8px;
  border: 1px solid #c7d2fe;
  background: #eef2ff;
  cursor: pointer;
}

.bank {
  list-style: none;
  margin: 0 0 0.7rem;
  padding: 0;
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
}

.tile {
  position: relative;
  font-size: 1.5rem;
  padding: 0.35rem 0.7rem;
  border-radius: 9px;
  border: 1px solid #d5d8de;
  background: #fff;
  cursor: pointer;
}

.tile.used {
  opacity: 0.3;
  cursor: default;
}

.key {
  position: absolute;
  top: 1px;
  right: 3px;
  font-size: 0.55rem;
  color: #9ca3af;
}

.tile-actions {
  display: flex;
  gap: 0.5rem;
}

.hints {
  list-style: none;
  margin: 0.8rem 0 0;
  padding: 0;
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  font-size: 0.75rem;
  color: #6b7280;
}

.hint-char {
  font-size: 1rem;
  margin-right: 0.2rem;
}

.retry {
  margin: 0.7rem 0 0;
  font-size: 0.85rem;
  color: #92400e;
}

.feedback {
  margin-top: 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  align-items: flex-start;
}

.right {
  color: #047857;
  font-weight: 600;
  margin: 0;
}

.wrong {
  color: #b91c1c;
  font-weight: 600;
  margin: 0;
}

.note {
  margin: 0;
  font-size: 0.85rem;
  color: #6b7280;
}

.expected {
  margin: 0;
  font-size: 1.1rem;
  display: flex;
  gap: 0.5rem;
  align-items: baseline;
  flex-wrap: wrap;
}

.expected-text {
  font-size: 0.9rem;
  color: #374151;
}

.stop-row {
  margin: 1rem 0 0;
}

.score-label {
  margin: 0;
  font-size: 0.75rem;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: #6b7280;
}

.score {
  margin: 0.2rem 0 0;
  font-size: 2.6rem;
  font-weight: 700;
  color: #3730a3;
}

.score-total {
  font-size: 1.2rem;
  color: #9ca3af;
}

.score-note {
  margin: 0.2rem 0 1rem;
  color: #374151;
  font-size: 0.9rem;
}

.review h2 {
  font-size: 0.85rem;
  margin: 0 0 0.4rem;
}

.missed {
  list-style: none;
  margin: 0 0 1rem;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}

.missed li {
  display: flex;
  gap: 0.5rem;
  align-items: baseline;
  flex-wrap: wrap;
  font-size: 0.85rem;
  border-bottom: 1px solid #f1f3f6;
  padding-bottom: 0.35rem;
}

.m-track {
  font-size: 0.68rem;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: #9ca3af;
  min-width: 5rem;
}

.m-prompt {
  color: #374151;
}

.m-answer {
  font-weight: 600;
}

.actions {
  display: flex;
  gap: 0.6rem;
  flex-wrap: wrap;
}
</style>
