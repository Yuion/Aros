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

    <!-- -------------------------------------------------------------------- the score -->
    <section v-else-if="finished" class="card">
      <h2>{{ score.right }} of {{ score.total }}</h2>
      <p class="hint">
        {{ score.right === score.total
          ? 'Every one.'
          : 'The ones you missed come round more often from now on.' }}
      </p>
      <button class="primary" @click="start">Again</button>
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
        <button class="primary" @click="start">Start</button>
        <button v-if="standing.withAudio < standing.sounds" class="secondary" :disabled="building" @click="build">
          {{ building ? 'Speaking…' : `Speak the missing ${standing.sounds - standing.withAudio}` }}
        </button>
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

const question = computed(() => (finished.value ? null : round.value[index.value] ?? null))

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

async function start() {
  error.value = ''
  verdict.value = null
  finished.value = false
  index.value = 0
  score.value = { right: 0, total: 0 }

  try {
    const result = await api.post('/tones/round')
    round.value = result.questions
    await play()
  } catch (e) {
    error.value = e.message
  }
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
  askedAt = 0

  if (index.value + 1 < round.value.length) {
    index.value += 1
    await play()
    return
  }

  finished.value = true
  await load()
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
