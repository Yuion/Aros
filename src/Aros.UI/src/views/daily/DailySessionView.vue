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
            <span
              v-if="miss.expected !== miss.prompt"
              class="m-answer"
              :lang="han(miss.expected) ? 'zh' : undefined"
            >{{ miss.expected }}</span>
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

        <p v-if="!answer" class="keys-hint">
          Keys <strong>1–{{ Math.min(9, current.tiles?.length ?? 0) }}</strong> place ·
          <strong>Backspace</strong> takes back · <strong>Enter</strong> checks
        </p>
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
import { RouterLink, useRoute } from 'vue-router'
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

/** Chinese needs the language tag for its font; pinyin and English must not get it. */
function han(text) {
  return /[一-鿿]/.test(text ?? '')
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
      // The question as it was put — a listening card has no prompt, so the sentence stands in
      prompt: card.prompt ?? result.correctSentence ?? card.pattern,
      chinese: card.promptLabel === 'Characters' || (!card.prompt && !!result.correctSentence),
      // ...and the answer it wanted, which for a listening card is the reading or the translation
      expected: result.expected || result.characters || result.correctSentence,
    },
  ]
}

/**
 * "I was right after all" — only for a translation the matcher rejected.
 *
 * Overruling makes the answer correct, which hides the Next button along with every other wrong
 * answer's, so it has to start the advance itself: otherwise the card sits there with no way on.
 */
async function override() {
  try {
    const result = await api.post('/listening/override', { token: current.value.token })
    answer.value = { ...answer.value, ...result, correct: true }
    correctCount.value++
    missed.value = missed.value.slice(0, -1)

    advance = setTimeout(next, CORRECT_PAUSE)
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
  max-width: 560px;
  margin: 0 auto;
  padding-top: 1rem;
}

.status {
  text-align: center;
  color: #6b7280;
  font-size: 0.9rem;
}

.status.error {
  color: #b91c1c;
}

.status.inline {
  margin-top: 0.8rem;
  font-size: 0.8rem;
}

.round,
.scorecard {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  align-items: center;
}

.progress {
  display: flex;
  justify-content: space-between;
  width: 100%;
  font-size: 0.78rem;
  color: #9ca3af;
}

.tally {
  color: #6d5bd0;
  font-weight: 600;
}

.bar {
  width: 100%;
  height: 3px;
  border-radius: 999px;
  background: #f3f2fa;
  overflow: hidden;
  margin-top: -0.6rem;
}

.fill {
  height: 100%;
  background: #6d5bd0;
  transition: width 0.2s ease;
}

/* Which kind of question this is — the one thing a mixed session must always say */
.track-label {
  font-size: 0.72rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: #9ca3af;
  text-align: center;
}

.listen-row {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.6rem;
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

.slow {
  font: inherit;
  font-size: 0.8rem;
  padding: 0.45rem 0.7rem;
  border: 1px solid #e5e7eb;
  border-radius: 999px;
  background: white;
  color: #4b5563;
  cursor: pointer;
  white-space: nowrap;
}

.pattern {
  font-size: 0.78rem;
  font-weight: 600;
  color: #6d5bd0;
  text-align: center;
}

.prompt {
  font-size: 1.25rem;
  font-weight: 600;
  text-align: center;
  line-height: 1.4;
}

.prompt[lang='zh'] {
  font-size: 2.2rem;
  letter-spacing: 0.05em;
}

.typed {
  display: flex;
  gap: 0.5rem;
  width: 100%;
}

.typed input {
  flex: 1;
  font: inherit;
  font-size: 1rem;
  padding: 0.6rem 0.75rem;
  border: 1px solid #e5e7eb;
  border-radius: 8px;
  background: white;
}

.typed input:focus {
  outline: none;
  border-color: #6d5bd0;
}

.tiles {
  display: flex;
  flex-direction: column;
  gap: 0.8rem;
  width: 100%;
}

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
  font-size: 0.78rem;
  color: #9ca3af;
}

.built-tile {
  font: inherit;
  font-size: 1.4rem;
  line-height: 1;
  padding: 0.3rem 0.45rem;
  border: 1px solid #6d5bd0;
  border-radius: 8px;
  background: white;
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
  font: inherit;
  font-size: 1.5rem;
  line-height: 1;
  padding: 0.45rem 0.6rem;
  border: 1px solid #e5e7eb;
  border-radius: 10px;
  background: white;
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

.hints {
  list-style: none;
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  justify-content: center;
  font-size: 0.75rem;
  color: #6b7280;
}

.hints li {
  display: flex;
  align-items: baseline;
  gap: 0.25rem;
  border: 1px solid #e5e7eb;
  border-radius: 999px;
  padding: 0.15rem 0.6rem;
  background: white;
}

.hint-char {
  font-size: 1rem;
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

.feedback {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.45rem;
}

.right {
  color: #15803d;
  font-weight: 600;
}

.wrong {
  color: #b91c1c;
  font-weight: 600;
}

.note {
  font-size: 0.8rem;
  color: #92400e;
}

.expected {
  display: flex;
  gap: 0.5rem;
  align-items: baseline;
  flex-wrap: wrap;
  justify-content: center;
  font-size: 1.2rem;
}

.expected-text {
  font-size: 0.95rem;
  color: #4b5563;
}

.stop-row {
  margin-top: 0.2rem;
}

.score-label {
  font-size: 0.78rem;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: #9ca3af;
}

.score {
  font-size: 3rem;
  font-weight: 700;
  line-height: 1;
  color: #6d5bd0;
}

.score-total {
  font-size: 1.4rem;
  color: #b8bcc4;
}

.score-note {
  font-size: 0.85rem;
  color: #6b7280;
  text-align: center;
}

.review {
  width: 100%;
}

.review h2 {
  font-size: 0.78rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: #9ca3af;
  margin-bottom: 0.5rem;
  text-align: center;
}

.missed {
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  max-height: 16rem;
  overflow-y: auto;
}

.missed li {
  display: grid;
  grid-template-columns: 5.5rem 1fr auto;
  gap: 0.5rem;
  align-items: baseline;
  font-size: 0.85rem;
  padding: 0.4rem 0.6rem;
  background: white;
  border: 1px solid #f0eefa;
  border-radius: 8px;
}

.m-track {
  font-size: 0.62rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: #b8bcc4;
}

.m-prompt {
  color: #4b5563;
  overflow-wrap: anywhere;
}

.m-answer {
  font-weight: 600;
  overflow-wrap: anywhere;
  text-align: right;
}

.keys-hint {
  font-size: 0.72rem;
  color: #b8bcc4;
  text-align: center;
}

.actions {
  display: flex;
  gap: 0.6rem;
  flex-wrap: wrap;
  justify-content: center;
}

@media (max-width: 480px) {
  .missed li {
    grid-template-columns: 1fr;
    gap: 0.15rem;
  }
}

.primary {
  font: inherit;
  font-size: 0.9rem;
  font-weight: 600;
  padding: 0.55rem 1.1rem;
  border: none;
  border-radius: 8px;
  background: #6d5bd0;
  color: white;
  cursor: pointer;
}

.primary:disabled {
  background: #d8d5ea;
  cursor: default;
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

.secondary:hover:not(:disabled) {
  border-color: #6d5bd0;
  color: #6d5bd0;
}

.secondary:disabled {
  opacity: 0.5;
  cursor: default;
}

.ghost {
  font: inherit;
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
</style>
