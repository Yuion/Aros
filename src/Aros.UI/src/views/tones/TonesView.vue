<!--
  Telling the four tones apart by ear, with the word taken out of the way.

  The listening trainer asks for the pinyin of a sentence, which tests the tones along with
  everything else — and a sentence you know hands you its own tones. Here a single syllable is
  played and the only question is which of the four it was, so there is nothing to lean on.

  The character is hidden until after the answer. Knowing what 菜 is would settle the question
  before the sound had finished.
-->
<template>
  <div class="page">
    <h1>Tones</h1>
    <p class="lead">
      One syllable, four possible tones. No word, no sentence, no context — just the sound.
      Nothing here changes when a word is next asked.
    </p>

    <p v-if="error" class="error">{{ error }}</p>

    <!-- ------------------------------------------------------------------- the round -->
    <section v-if="question" class="card bench">
      <p class="count">{{ index + 1 }} of {{ round.length }}</p>

      <button class="play" :disabled="playing" @click="play">
        {{ playing ? '♪' : '▶' }}
      </button>
      <p class="hint">Press to hear it again.</p>

      <!-- The syllable without its tone. It settles nothing about which of the four was said,
           and it separates "I cannot place that sound at all" from "I cannot hear the tone". -->
      <p class="hint-row">
        <button v-if="!hint && !verdict" class="ghost" @click="reveal">Hint: which syllable?</button>
        <span v-else-if="hint" class="hinted">{{ hint }} — but which tone?</span>
      </p>

      <div class="tones">
        <button
          v-for="tone in 4"
          :key="tone"
          class="tone"
          :class="verdict ? toneClass(tone) : ''"
          :disabled="!!verdict"
          @click="answer(tone)"
        >
          <span class="mark">{{ MARKS[tone - 1] }}</span>
          <span class="number">{{ tone }}</span>
        </button>
      </div>

      <div v-if="verdict" class="verdict" :class="verdict.correct ? 'right' : 'wrong'">
        <p class="said">
          <span lang="zh">{{ verdict.character }}</span>
          <span class="pin">{{ verdict.pinyin }}</span>
        </p>
        <p>
          {{ verdict.correct ? 'Right.' : `That was tone ${verdict.tone}.` }}
        </p>
        <button class="primary" @click="next">
          {{ index + 1 < round.length ? 'Next' : 'Finish' }}
        </button>
      </div>
    </section>


    <!-- --------------------------------------------------------------- sorting a set -->
    <!-- The same four sounds together: nothing has to be recognised cold, they only have to be
         told apart, which is a different skill from naming one tone out of the blue. -->
    <section v-else-if="set" class="card bench">
      <p class="count">Set {{ setIndex + 1 }} of {{ sets.length }}</p>
      <p class="lead-in">One syllable, all four tones. Give each one its number.</p>

      <ul class="cards">
        <li v-for="(card, i) in set" :key="card.token" class="sound">
          <button class="play small" @click="playCard(i)">{{ playingCard === i ? '♪' : '▶' }}</button>

          <div class="tones">
            <button
              v-for="tone in 4"
              :key="tone"
              class="tone small"
              :class="setClass(i, tone)"
              :disabled="!!setVerdicts"
              @click="assign(i, tone)"
            >
              <span class="mark">{{ MARKS[tone - 1] }}</span>
              <span class="number">{{ tone }}</span>
            </button>
          </div>

          <span v-if="setVerdicts" class="said small" lang="zh">
            {{ setVerdicts[i].character }} <span class="pin">{{ setVerdicts[i].pinyin }}</span>
          </span>
        </li>
      </ul>

      <p v-if="!setVerdicts" class="hint">
        Each number once. {{ assigned.filter(Boolean).length }} of 4 placed.
      </p>

      <button
        v-if="!setVerdicts"
        class="primary"
        :disabled="!allPlaced || checking"
        @click="checkSet"
      >
        {{ checking ? 'Checking…' : 'Check' }}
      </button>

      <template v-else>
        <p class="verdict" :class="setRight === 4 ? 'right' : 'wrong'">
          {{ setRight }} of 4 right.
        </p>
        <button class="primary" @click="nextSet">
          {{ setIndex + 1 < sets.length ? 'Next set' : 'Finish' }}
        </button>
      </template>
    </section>

    <!-- -------------------------------------------------------------------- the score -->
    <section v-else-if="finished" class="card">
      <h2>{{ score.right }} of {{ score.total }}</h2>
      <p class="hint">
        {{ score.right === score.total
          ? 'Every one.'
          : 'The ones you missed come round more often from now on.' }}
      </p>
      <button class="primary" @click="again">Again</button>
    </section>

    <!-- --------------------------------------------------------------------- the start -->
    <section v-else-if="standing" class="card">
      <template v-if="standing.withAudio === 0">
        <h2>The sound bank is empty</h2>
        <p class="hint">
          {{ standing.sounds }} sounds — {{ standing.sounds / 4 }} syllables in all four tones,
          each spoken once and kept for good. Building it costs one synthesis per sound and
          never costs again.
        </p>
        <button class="primary" :disabled="building" @click="build">
          {{ building ? 'Speaking…' : `Build the bank (${standing.sounds} sounds)` }}
        </button>
      </template>

      <template v-else>
        <h2>{{ standing.withAudio }} sounds ready</h2>
        <p class="hint">
          {{ standing.sounds / 4 }} syllables, each in all four tones.
          <template v-if="standing.answered">
            {{ standing.answered }} answered so far, {{ percent(standing.accuracy) }} right.
          </template>
        </p>
        <!-- Two different tests, so two buttons rather than a setting: naming a tone cold, and
             telling four apart when all four are in front of you -->
        <button class="primary" @click="start()">One at a time</button>
        <button class="primary" @click="startSets()">Sort the four</button>
        <button v-if="standing.withAudio < standing.sounds" class="secondary" :disabled="building" @click="build">
          {{ building ? 'Speaking…' : `Speak the missing ${standing.sounds - standing.withAudio}` }}
        </button>
        <p class="hint">
          <strong>One at a time</strong> plays a single syllable and asks which tone it was.
          <strong>Sort the four</strong> plays one syllable in all four, shuffled, and asks you
          to hand out the numbers.
        </p>
      </template>

      <!-- What gets mistaken for what is the whole point of keeping the answers -->
      <div v-if="standing.confusions?.length" class="confusions">
        <h3>What you mix up</h3>
        <ul>
          <li v-for="c in standing.confusions" :key="`${c.heard}-${c.said}`">
            heard <strong>tone {{ c.heard }}</strong>, said tone {{ c.said }}
            <span class="times">× {{ c.times }}</span>
          </li>
        </ul>
      </div>
    </section>

    <p v-else class="hint">Loading…</p>

    <audio ref="player" />
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import { api } from '@/services/api'

/** The diacritic each tone wears, which is how they are taught and how they are read. */
const MARKS = ['ˉ', 'ˊ', 'ˇ', 'ˋ']

const standing = ref(null)
const round = ref([])
const index = ref(0)
const verdict = ref(null)
const picked = ref(0)
const finished = ref(false)
const building = ref(false)
const playing = ref(false)
const error = ref('')
const player = ref(null)
const score = ref({ right: 0, total: 0 })

let askedAt = 0

/** The syllable, once asked for. Never the tone — that is the question. */
const hint = ref('')

// Sorting mode: sets of four, the numbers handed out between them
const sets = ref([])
const setIndex = ref(0)
const assigned = ref([0, 0, 0, 0])
const setVerdicts = ref(null)
const playingCard = ref(-1)
const checking = ref(false)
const wasSets = ref(false)

const question = computed(() => (finished.value ? null : round.value[index.value] ?? null))

const set = computed(() => (finished.value ? null : sets.value[setIndex.value]?.questions ?? null))

const allPlaced = computed(() => assigned.value.every((tone) => tone > 0))

const setRight = computed(() =>
  (setVerdicts.value ?? []).filter((v) => v.correct).length,
)

onMounted(load)

async function load() {
  try {
    standing.value = await api.get('/tones/standing')
  } catch (e) {
    error.value = e.message
  }
}

async function build() {
  building.value = true
  error.value = ''

  try {
    await api.post('/tones/bank')
    await load()
  } catch (e) {
    error.value = e.message
  } finally {
    building.value = false
  }
}

/** Everything a fresh round of either kind needs cleared. */
function reset() {
  error.value = ''
  verdict.value = null
  hint.value = ''
  finished.value = false
  index.value = 0
  round.value = []
  sets.value = []
  setIndex.value = 0
  setVerdicts.value = null
  assigned.value = [0, 0, 0, 0]
  score.value = { right: 0, total: 0 }
}

async function start() {
  reset()
  wasSets.value = false

  try {
    const result = await api.post('/tones/round')
    round.value = result.questions
    await play()
  } catch (e) {
    error.value = e.message
  }
}

async function startSets() {
  reset()
  wasSets.value = true

  try {
    const result = await api.post('/tones/sets?count=5')
    sets.value = result.sets
    await playCard(0)
  } catch (e) {
    error.value = e.message
  }
}

/** "Again" means the kind you were just doing, not whichever came first. */
function again() {
  return wasSets.value ? startSets() : start()
}

/** The syllable, asked for by token so the page never holds the answer. */
async function reveal() {
  try {
    const result = await api.get(`/tones/hint/${question.value.token}`)
    hint.value = result.syllable
  } catch (e) {
    error.value = e.message
  }
}

async function playCard(i) {
  const card = set.value?.[i]
  if (!card || !player.value) return

  playingCard.value = i

  try {
    player.value.src = card.audioUrl
    await player.value.play()
  } catch {
    // Same as the single round: a browser that waits for a gesture gets one from the button
  } finally {
    playingCard.value = -1
  }
}

/**
 * Hands a number to one sound. Each number belongs to exactly one of the four, so giving it to a
 * second takes it off the first rather than leaving two sounds claiming the same tone.
 */
function assign(card, tone) {
  if (setVerdicts.value) return

  assigned.value = assigned.value.map((held, i) =>
    i === card ? tone : held === tone ? 0 : held,
  )
}

async function checkSet() {
  if (!allPlaced.value || checking.value) return

  checking.value = true
  error.value = ''

  try {
    const verdicts = []

    for (const [i, card] of set.value.entries()) {
      verdicts.push(
        await api.post('/tones/answer', {
          token: card.token,
          tone: assigned.value[i],
          durationMs: 0,
        }),
      )
    }

    setVerdicts.value = verdicts
    score.value.total += 4
    score.value.right += verdicts.filter((v) => v.correct).length
  } catch (e) {
    error.value = e.message
  } finally {
    checking.value = false
  }
}

async function nextSet() {
  setVerdicts.value = null
  assigned.value = [0, 0, 0, 0]

  if (setIndex.value + 1 < sets.value.length) {
    setIndex.value += 1
    await playCard(0)
    return
  }

  finished.value = true
  await load()
}

async function play() {
  if (!question.value || !player.value) return

  playing.value = true
  askedAt = askedAt || Date.now()

  try {
    player.value.src = question.value.audioUrl
    await player.value.play()
  } catch {
    // A browser that will not play until the page is touched — the button is the touch
  } finally {
    playing.value = false
  }
}

async function answer(tone) {
  if (verdict.value) return
  picked.value = tone

  try {
    verdict.value = await api.post('/tones/answer', {
      token: question.value.token,
      tone,
      durationMs: askedAt ? Date.now() - askedAt : 0,
    })

    score.value.total += 1
    if (verdict.value.correct) score.value.right += 1
  } catch (e) {
    error.value = e.message
  }
}

async function next() {
  verdict.value = null
  picked.value = 0
  hint.value = ''
  askedAt = 0

  if (index.value + 1 < round.value.length) {
    index.value += 1
    await play()
    return
  }

  finished.value = true
  await load()
}

/** In a set: the number you gave this sound, and after checking whether it was its own. */
function setClass(card, tone) {
  const chosen = assigned.value[card] === tone

  if (!setVerdicts.value) return chosen ? 'picked' : ''

  const answer = setVerdicts.value[card]
  if (tone === answer.tone) return 'was-right'
  if (chosen) return 'was-chosen'

  return 'was-wrong'
}

/** Green on what it was, red on what you said when those differ, faded on the rest. */
function toneClass(tone) {
  if (!verdict.value) return ''
  if (tone === verdict.value.tone) return 'was-right'
  if (tone === picked.value) return 'was-chosen'
  return 'was-wrong'
}

function percent(value) {
  return value == null ? '—' : `${Math.round(value * 100)}%`
}
</script>

<style scoped>
.page {
  max-width: 640px;
  padding: 1.5rem;
}

h1 {
  margin: 0 0 0.35rem;
  font-size: 1.6rem;
}

h2 {
  margin: 0 0 0.5rem;
  font-size: 1.05rem;
}

h3 {
  margin: 1.2rem 0 0.5rem;
  font-size: 0.85rem;
}

.lead {
  margin: 0 0 1.25rem;
  color: #6b7280;
  font-size: 0.88rem;
  line-height: 1.55;
}

.card {
  background: white;
  border: 1px solid #f0efec;
  border-radius: 10px;
  padding: 1.25rem 1.35rem;
}

.bench {
  text-align: center;
}

.count {
  margin: 0 0 1rem;
  font-size: 0.72rem;
  color: #9ca3af;
}

.play {
  width: 84px;
  height: 84px;
  border-radius: 50%;
  border: 1px solid #ddd6fe;
  background: #f5f3ff;
  color: #6d5bd0;
  font-size: 1.7rem;
  cursor: pointer;
}

.play:hover { border-color: #6d5bd0; }

.tones {
  display: flex;
  justify-content: center;
  gap: 0.6rem;
  margin-top: 1.2rem;
}

.tone {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.15rem;
  width: 70px;
  padding: 0.7rem 0;
  border: 1px solid #e5e7eb;
  border-radius: 10px;
  background: white;
  cursor: pointer;
}

.tone:hover:not(:disabled) { border-color: #6d5bd0; }

.tone .mark {
  font-size: 1.5rem;
  line-height: 1;
  color: #1f2937;
}

.tone .number {
  font-size: 0.72rem;
  color: #9ca3af;
}

.tone.was-right {
  border-color: #86efac;
  background: #f0fdf4;
}

.tone.was-wrong {
  opacity: 0.35;
}

.tone.was-chosen {
  border-color: #fecaca;
  background: #fef2f2;
}

.tone.was-chosen .mark { color: #b91c1c; }

.tone.picked {
  border-color: #6d5bd0;
  background: #f5f3ff;
}

/* ---------------------------------------------------------------- sorting a set */

.cards {
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 0.6rem;
  margin: 1rem 0 0.4rem;
}

.sound {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.6rem;
  flex-wrap: wrap;
}

.play.small {
  width: 44px;
  height: 44px;
  font-size: 1rem;
}

.tone.small {
  width: 52px;
  padding: 0.4rem 0;
}

.tone.small .mark { font-size: 1.1rem; }

.said.small {
  font-size: 1.1rem;
  min-width: 90px;
  text-align: left;
}

.lead-in {
  margin: 0 0 0.3rem;
  font-size: 0.82rem;
  color: #6b7280;
}

.hint-row {
  margin-top: 0.6rem;
  min-height: 1.6rem;
}

.ghost {
  font: inherit;
  font-size: 0.75rem;
  padding: 0.25rem 0.6rem;
  background: white;
  border: 1px solid #e5e7eb;
  border-radius: 7px;
  color: #6b7280;
  cursor: pointer;
}

.ghost:hover { border-color: #6d5bd0; color: #6d5bd0; }

.hinted {
  font-size: 0.95rem;
  color: #4b5563;
}

.tone:disabled { cursor: default; }

.verdict {
  margin-top: 1.1rem;
  font-size: 0.85rem;
}

.verdict.right { color: #15803d; }
.verdict.wrong { color: #b91c1c; }

.said {
  margin: 0 0 0.3rem;
  font-size: 1.6rem;
  color: #1f2937;
}

.said .pin {
  margin-left: 0.5rem;
  font-size: 0.95rem;
  color: #6b7280;
}

.primary,
.secondary {
  font: inherit;
  font-size: 0.82rem;
  padding: 0.45rem 0.95rem;
  margin: 0.8rem 0.3rem 0;
  border-radius: 8px;
  cursor: pointer;
  border: 1px solid transparent;
}

.primary {
  background: #6d5bd0;
  border-color: #6d5bd0;
  color: white;
}

.primary:hover:not(:disabled) { background: #5c4bbd; }

.secondary {
  background: white;
  border-color: #e5e7eb;
  color: #4b5563;
}

.primary:disabled,
.secondary:disabled { opacity: 0.55; cursor: default; }

.hint {
  margin: 0.5rem 0 0;
  font-size: 0.76rem;
  color: #9ca3af;
  line-height: 1.55;
}

.confusions ul {
  list-style: none;
  margin: 0;
  padding: 0;
  font-size: 0.8rem;
}

.confusions li {
  padding: 0.3rem 0;
  border-bottom: 1px solid #f6f5f3;
}

.confusions li:last-child { border-bottom: none; }

.times { color: #9ca3af; }

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
