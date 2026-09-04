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
      <div class="actions">
        <button class="primary" @click="loadQuiz">Play again</button>
        <RouterLink to="/chinese-listening" class="secondary">Done</RouterLink>
      </div>
    </section>

    <!-- A question -->
    <section v-else class="round">
      <header class="progress">
        <span>Question {{ index + 1 }} / {{ questions.length }}</span>
        <span class="tally">{{ correctCount }} correct</span>
      </header>

      <button class="listen" title="Play the clip" @click="replay">🔊</button>

      <p class="mode-label">{{ MODE_LABELS[mode] }}</p>

      <!-- Pick the sentence -->
      <ul v-if="!typed" class="options">
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

// Long enough to register the ✓, short enough that typing does not stall on it
const CORRECT_PAUSE = 1000

const MODE_LABELS = {
  Characters: 'Pick what you heard',
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
const loading = ref(true)
const error = ref('')
const player = ref(null)
const field = ref(null)
const nextButton = ref(null)
let advance = null

const current = computed(() => questions.value[index.value])

// A right answer in a writing mode moves on by itself; everything else waits for Next
const autoAdvancing = computed(() => !!answer.value?.correct && typed.value)

// Only a translation can be overruled: pinyin is marked exactly on purpose, and picking the
// sentence has one right answer with nothing to argue about.
const canOverride = computed(() => mode.value === 'English' && answer.value && !answer.value.correct)

const verdict = computed(() => {
  const ratio = correctCount.value / questions.value.length
  if (ratio === 1) return '完美 — perfect round.'
  if (ratio >= 0.7) return 'Solid. The ones you missed will come back sooner.'
  return 'Rough round — those sentences are now weighted to reappear.'
})

async function loadQuiz() {
  clearTimeout(advance)
  advance = null
  loading.value = true
  error.value = ''
  finished.value = false
  answer.value = null
  text.value = ''
  index.value = 0
  correctCount.value = 0

  try {
    // Length is decided server-side: every clip not resting, or ten of them
    const params = new URLSearchParams({ questions: '10', mode: route.query.mode ?? 'Characters' })
    if (route.query.sweep === 'false') params.set('sweep', 'false')

    const quiz = await api.post(`/listening/quiz?${params}`)
    mode.value = quiz.mode
    typed.value = quiz.typed
    questions.value = quiz.questions
    await nextTick()
    replay()
    field.value?.focus()
  } catch (e) {
    error.value = e.message
  } finally {
    loading.value = false
  }
}

function replay() {
  if (!player.value || !current.value) return
  player.value.src = current.value.audioUrl
  // Autoplay can be refused before the page has seen a gesture — the 🔊 button is the fallback
  player.value.play().catch(() => {})
}

async function choose(option) {
  if (answer.value) return
  await send({ selectedClipId: option.clipId })
}

async function submitTyped() {
  if (answer.value || !text.value.trim()) return
  await send({ text: text.value })
}

async function send(payload) {
  try {
    const result = await api.post('/listening/answer', { token: current.value.token, ...payload })
    answer.value = { ...result, selectedClipId: payload.selectedClipId }

    if (result.correct) {
      correctCount.value++

      // Right answers carry nothing to read, so hold the ✓ briefly and move on. A miss
      // waits: the sentence and its expected answer are the whole point of showing it.
      if (typed.value) advance = setTimeout(next, CORRECT_PAUSE)
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

  if (index.value + 1 >= questions.value.length) {
    finished.value = true
    return
  }

  index.value++
  await nextTick()
  replay()
  field.value?.focus()
}

onMounted(loadQuiz)
onUnmounted(() => clearTimeout(advance))
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
}

.secondary {
  font-size: 0.9rem;
  color: #6b7280;
  text-decoration: none;
}
</style>
