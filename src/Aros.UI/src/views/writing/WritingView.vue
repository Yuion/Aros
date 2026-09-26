<!--
  Writing characters by hand, graded stroke by stroke.

  Two things shape this page. The grading is the same comparison the stroke-order box already
  does — distance, start and end, direction, shape and length against the median — run against
  what you drew instead of against the dataset, so nothing new is fetched and nothing leaves
  the machine.

  The second is the tablet. It is opaque and maps absolutely, so the writing square is a fixed
  size and never moves: the mapping you set once in the driver stays true. Writing happens at
  the size your fingers move, not the size of the screen — see the setup note below the box.

  Nothing here touches VocabProgress. Not the schedule, not the daily session, not a trainer.
-->
<template>
  <div class="page">
    <h1>Writing</h1>
    <p class="lead">
      Characters by hand, graded on order, direction and shape. Kept entirely apart from the
      trainers — a bad afternoon here never changes when a word is next asked.
    </p>

    <p v-if="error" class="error">{{ error }}</p>

    <div class="modes">
      <button :class="{ picked: mode === 'copying' }" @click="setMode('copying')">
        Copying
        <small>the stroke order stays on screen</small>
      </button>
      <button :class="{ picked: mode === 'memory' }" @click="setMode('memory')">
        From memory
        <small>an empty box and the meaning</small>
      </button>
    </div>

    <template v-if="word">
      <section class="bench">
        <!-- ------------------------------------------------------------ the square -->
        <div class="writing">
          <p class="prompt">
            <span v-if="mode === 'copying'" class="target" lang="zh">{{ current }}</span>
            <span v-else class="hidden-target">{{ word.english }}</span>
          </p>

          <div ref="box" class="box" :class="{ done: finished }">
            <div ref="target" class="target-node" />
          </div>

          <p class="progress">
            Character {{ index + 1 }} of {{ characters.length }}
            <template v-if="word.characters.length > 1"> · <span lang="zh">{{ word.characters }}</span></template>
            <template v-if="strokesLeft !== null"> · {{ strokesLeft }} strokes left</template>
          </p>

          <p class="tally" :class="{ bad: mistakes > 0 }">
            <template v-if="mistakes === 0">no mistakes yet</template>
            <template v-else>{{ mistakes }} {{ mistakes === 1 ? 'mistake' : 'mistakes' }}</template>
            <template v-if="backwards"> · one stroke went backwards</template>
          </p>

          <div class="actions">
            <button class="secondary" @click="restart">Start this one again</button>
            <button class="secondary" @click="reveal">Show me</button>
            <button class="primary" @click="pickAnother">Another word</button>
          </div>
        </div>

        <!-- ------------------------------------------------------- how it is written -->
        <div v-if="mode === 'copying'" class="help">
          <h2>How it is written</h2>
          <StrokeOrder :key="current" :word="current" />
        </div>
        <div v-else class="help quiet">
          <h2>From memory</h2>
          <p>
            {{ word.english }}<br />
            <span class="pinyin">{{ word.pinyin }}</span>
          </p>
          <p class="hint">
            Nothing is shown until you finish or press <em>Show me</em>. Pressing it records the
            attempt as it stands — which is the honest thing to do with a character you could not
            recall.
          </p>
        </div>
      </section>

      <!-- ----------------------------------------------------------------- the record -->
      <section class="card">
        <h2>This word</h2>
        <dl class="record">
          <div>
            <dt>Copying</dt>
            <dd>{{ told(word.copying) }}</dd>
          </div>
          <div>
            <dt>From memory</dt>
            <dd>{{ told(word.memory) }}</dd>
          </div>
        </dl>
      </section>
    </template>

    <!-- ------------------------------------------------------------------ the word list -->
    <section class="card">
      <h2>Pick a word</h2>
      <input v-model="search" class="search" placeholder="Find a word, reading or meaning" />

      <ul class="words">
        <li v-for="candidate in shown" :key="candidate.id">
          <button class="word" :class="{ picked: word?.id === candidate.id }" @click="choose(candidate)">
            <span class="chars" lang="zh">{{ candidate.characters }}</span>
            <span class="pinyin">{{ candidate.pinyin }}</span>
            <span class="english">{{ candidate.english }}</span>
            <span class="count">{{ shortRecord(candidate) }}</span>
          </button>
        </li>
      </ul>
      <p v-if="!shown.length" class="hint">Nothing matches.</p>
    </section>

    <!-- --------------------------------------------------------------- the tablet note -->
    <section class="card">
      <h2>Setting up the tablet</h2>
      <p class="hint">
        The HS610 has no screen and maps its whole surface to the whole monitor, so out of the box
        one character costs an arm's sweep. In the driver, set the <strong>pen working area</strong>
        to a small square — 30 to 40&nbsp;mm is about the size you would write on paper — and the
        <strong>screen area</strong> to the square below. Then your fingers move a character's
        worth and the pen lands where you expect.
      </p>
      <p class="hint">
        The writing square never moves or changes size, so this is a one-time setting. It is
        currently at:
      </p>
      <pre class="rect">{{ rectangle }}</pre>
      <p class="hint">
        Measured in screen pixels from the top left of the display. Keep the browser window
        maximised and the zoom at 100%, or the numbers drift.
      </p>
    </section>
  </div>
</template>

<script setup>
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import HanziWriter from 'hanzi-writer'
import { api } from '@/services/api'
import StrokeOrder from '@/components/StrokeOrder.vue'

/** Fixed, so the driver mapping set once stays true. Matches .box in the stylesheet. */
const SIDE = 420

const words = ref([])
const word = ref(null)
const mode = ref('memory')
const search = ref('')
const error = ref('')

const index = ref(0)
const mistakes = ref(0)
const backwards = ref(false)
const strokesLeft = ref(null)
const strokeCount = ref(0)
const finished = ref(false)

const box = ref(null)
const target = ref(null)
const rectangle = ref('—')

let writer = null
let startedAt = 0

const characters = computed(() => [...(word.value?.characters ?? '')])
const current = computed(() => characters.value[index.value] ?? '')

const shown = computed(() => {
  const needle = search.value.trim().toLowerCase()
  const list = needle
    ? words.value.filter(w =>
        w.characters.includes(needle)
        || w.pinyin.toLowerCase().includes(needle)
        || w.english.toLowerCase().includes(needle))
    : words.value
  return list.slice(0, 60)
})

onMounted(async () => {
  try {
    words.value = await api.get('/writing/words')
  } catch (e) {
    error.value = e.message
    return
  }
  pickAnother()
  measure()
  window.addEventListener('resize', measure)
})

/** Where the square sits on the monitor, for the driver's screen-area boxes. */
function measure() {
  if (!box.value) return

  const rect = box.value.getBoundingClientRect()
  const scale = window.devicePixelRatio || 1
  const left = Math.round((window.screenX + rect.left) * scale)
  const top = Math.round((window.screenY + (window.outerHeight - window.innerHeight) + rect.top) * scale)
  const side = Math.round(rect.width * scale)

  rectangle.value = `left ${left}   top ${top}   width ${side}   height ${side}`
}

function told(record) {
  if (!record?.attempts) return 'never'
  return `${record.clean} clean of ${record.attempts} · ${record.mistakes} mistakes in total`
}

function shortRecord(candidate) {
  const total = candidate.copying.attempts + candidate.memory.attempts
  if (!total) return ''
  return `${candidate.copying.clean + candidate.memory.clean}/${total}`
}

function setMode(next) {
  if (mode.value === next) return
  mode.value = next
  start()
}

function choose(candidate) {
  word.value = candidate
  index.value = 0
  start()
}

/** Any word, favouring the ones written least — practice should not pile onto the same three. */
function pickAnother() {
  if (!words.value.length) return

  const fewest = Math.min(...words.value.map(w => w.copying.attempts + w.memory.attempts))
  const pool = words.value.filter(w => w.copying.attempts + w.memory.attempts <= fewest + 1)
  const next = pool[Math.floor(Math.random() * pool.length)] ?? words.value[0]

  choose(next)
}

function restart() {
  start()
}

/** Gives up on this character and records it as it stands, mistakes and all. */
function reveal() {
  if (!writer || finished.value) return
  writer.showCharacter()
  finish()
}

watch(current, start)

async function start() {
  mistakes.value = 0
  backwards.value = false
  finished.value = false
  strokesLeft.value = null
  startedAt = Date.now()

  await nextTick()
  if (!target.value || !current.value) return

  target.value.innerHTML = ''

  writer = HanziWriter.create(target.value, current.value, {
    width: SIDE,
    height: SIDE,
    padding: 12,
    showCharacter: false,
    // Copying still hides the outline in the square: the stroke order is beside you to read,
    // and an outline under the pen turns writing into tracing
    showOutline: false,
    strokeColor: '#1f2937',
    drawingColor: '#b91c1c',
    drawingWidth: 26,
    charDataLoader: loadCharacter,
  })

  writer.quiz({
    // A shown stroke would answer the question being asked, so misses are counted, never helped
    showHintAfterMisses: false,
    markStrokeCorrectAfterMisses: false,
    acceptBackwardsStrokes: false,
    leniency: 1,
    onMistake: (stroke) => {
      mistakes.value = stroke.totalMistakes
      if (stroke.isBackwards) backwards.value = true
      strokesLeft.value = stroke.strokesRemaining
    },
    onCorrectStroke: (stroke) => {
      strokesLeft.value = stroke.strokesRemaining
    },
    onComplete: finish,
  })
}

/** The API holds the whole dataset; the library's own loader would reach for a CDN. */
async function loadCharacter(character, onComplete, onError) {
  try {
    const entries = await api.get(`/strokes?word=${encodeURIComponent(character)}`)
    const data = entries[0]?.data
    if (!data) throw new Error(`No stroke data for ${character}.`)
    strokeCount.value = data.strokes.length
    onComplete(data)
  } catch (e) {
    error.value = e.message
    onError(e)
  }
}

async function finish() {
  if (finished.value) return
  finished.value = true

  const written = current.value
  const attempt = {
    wordId: word.value.id,
    character: written,
    mode: mode.value,
    mistakes: mistakes.value,
    backwards: backwards.value,
    strokes: strokeCount.value,
    durationMs: Date.now() - startedAt,
  }

  try {
    await api.post('/writing/attempts', attempt)
    words.value = await api.get('/writing/words')
    word.value = words.value.find(w => w.id === attempt.wordId) ?? word.value
  } catch (e) {
    error.value = e.message
  }

  // A pause to see the finished character before the next one replaces it
  setTimeout(() => {
    if (index.value < characters.value.length - 1) index.value += 1
    else pickAnother()
  }, 1200)
}
</script>

<style scoped>
.page {
  max-width: 980px;
  padding: 1.5rem;
}

h1 {
  margin: 0 0 0.35rem;
  font-size: 1.6rem;
}

.lead {
  margin: 0 0 1.25rem;
  color: #6b7280;
  font-size: 0.88rem;
  line-height: 1.55;
}

.modes {
  display: flex;
  gap: 0.6rem;
  margin-bottom: 1.25rem;
}

.modes button {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  align-items: flex-start;
  font: inherit;
  font-size: 0.85rem;
  padding: 0.55rem 1rem;
  border: 1px solid #e5e7eb;
  border-radius: 10px;
  background: white;
  cursor: pointer;
}

.modes button small {
  font-size: 0.7rem;
  color: #9ca3af;
}

.modes button.picked {
  border-color: #6d5bd0;
  background: #f5f3ff;
  color: #4c3fa8;
}

.bench {
  display: flex;
  gap: 1.25rem;
  align-items: flex-start;
  margin-bottom: 1.1rem;
  flex-wrap: wrap;
}

.writing {
  background: white;
  border: 1px solid #f0efec;
  border-radius: 10px;
  padding: 1rem 1.1rem 1.1rem;
}

.prompt {
  margin: 0 0 0.6rem;
  min-height: 2.2rem;
}

.target {
  font-size: 1.9rem;
}

.hidden-target {
  font-size: 1rem;
  color: #4b5563;
}

/* Never moves, never resizes: the tablet is mapped to this rectangle */
.box {
  position: relative;
  width: 420px;
  height: 420px;
  border: 1px solid #e5e7eb;
  border-radius: 10px;
  background: white;
  touch-action: none;
}

/* The centre cross of a 田字格, for judging where a stroke starts and how far it reaches.
   Underneath the ink and deaf to the pen, so they guide without getting in the way. */
.box::before,
.box::after {
  content: '';
  position: absolute;
  pointer-events: none;
  z-index: 0;
}

.box::before {
  top: 8px;
  bottom: 8px;
  left: 50%;
  border-left: 1px dashed #d3d8e0;
}

.box::after {
  left: 8px;
  right: 8px;
  top: 50%;
  border-top: 1px dashed #d3d8e0;
}

.box.done {
  border-color: #86efac;
}

.target-node {
  position: relative;
  z-index: 1;
  width: 420px;
  height: 420px;
}

.progress,
.tally {
  margin: 0.55rem 0 0;
  font-size: 0.76rem;
  color: #9ca3af;
}

.tally.bad {
  color: #b91c1c;
}

.actions {
  display: flex;
  gap: 0.5rem;
  margin-top: 0.85rem;
  flex-wrap: wrap;
}

.primary,
.secondary {
  font: inherit;
  font-size: 0.8rem;
  padding: 0.45rem 0.9rem;
  border-radius: 8px;
  cursor: pointer;
  border: 1px solid transparent;
}

.primary {
  background: #6d5bd0;
  border-color: #6d5bd0;
  color: white;
}

.primary:hover { background: #5c4bbd; }

.secondary {
  background: white;
  border-color: #e5e7eb;
  color: #4b5563;
}

.secondary:hover { border-color: #6b7280; }

.help {
  flex: 1;
  min-width: 260px;
  background: white;
  border: 1px solid #f0efec;
  border-radius: 10px;
  padding: 1rem 1.1rem;
}

.help h2,
.card h2 {
  margin: 0 0 0.75rem;
  font-size: 0.95rem;
}

.help.quiet p {
  margin: 0 0 0.6rem;
  font-size: 0.95rem;
}

.help .pinyin {
  color: #6b7280;
  font-size: 0.85rem;
}

.card {
  background: white;
  border: 1px solid #f0efec;
  border-radius: 10px;
  padding: 1.1rem 1.2rem;
  margin-bottom: 1.1rem;
}

.record {
  display: flex;
  gap: 2rem;
  margin: 0;
}

.record dt {
  font-size: 0.7rem;
  color: #9ca3af;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.record dd {
  margin: 0.15rem 0 0;
  font-size: 0.85rem;
}

.search {
  font: inherit;
  font-size: 0.85rem;
  width: 100%;
  padding: 0.45rem 0.6rem;
  border: 1px solid #e5e7eb;
  border-radius: 8px;
  margin-bottom: 0.75rem;
}

.search:focus {
  outline: none;
  border-color: #6d5bd0;
}

.words {
  list-style: none;
  margin: 0;
  padding: 0;
  max-height: 20rem;
  overflow-y: auto;
}

.word {
  display: grid;
  grid-template-columns: 5rem 7rem 1fr auto;
  gap: 0.75rem;
  align-items: center;
  width: 100%;
  text-align: left;
  font: inherit;
  font-size: 0.82rem;
  padding: 0.45rem 0.5rem;
  border: none;
  border-bottom: 1px solid #f6f5f3;
  background: none;
  cursor: pointer;
}

.word:hover { background: #fafafa; }

.word.picked {
  background: #f5f3ff;
  color: #4c3fa8;
}

.word .chars {
  font-size: 1rem;
}

.word .pinyin,
.word .count {
  color: #9ca3af;
}

.hint {
  margin: 0 0 0.6rem;
  font-size: 0.76rem;
  color: #9ca3af;
  line-height: 1.6;
}

.rect {
  margin: 0 0 0.6rem;
  padding: 0.6rem 0.75rem;
  background: #1e1e2e;
  color: #cdd6f4;
  border-radius: 8px;
  font-family: ui-monospace, Consolas, monospace;
  font-size: 0.76rem;
}

.error {
  margin: 0 0 1rem;
  padding: 0.6rem 0.8rem;
  background: #fef2f2;
  border: 1px solid #fecaca;
  border-radius: 8px;
  font-size: 0.8rem;
  color: #b91c1c;
}
</style>
