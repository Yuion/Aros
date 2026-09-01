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

      <ul class="options">
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

      <div v-if="answer" class="feedback">
        <p :class="answer.correct ? 'right' : 'wrong'">
          {{ answer.correct ? '✓ Correct' : '✗ Not quite' }}
        </p>
        <button class="primary" @click="next">
          {{ index + 1 === questions.length ? 'See score' : 'Next' }}
        </button>
      </div>
    </section>

    <audio ref="player" />
  </div>
</template>

<script setup>
import { computed, nextTick, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { api } from '@/services/api'

const questions = ref([])
const index = ref(0)
const answer = ref(null)
const correctCount = ref(0)
const finished = ref(false)
const loading = ref(true)
const error = ref('')
const player = ref(null)

const current = computed(() => questions.value[index.value])

const verdict = computed(() => {
  const ratio = correctCount.value / questions.value.length
  if (ratio === 1) return '完美 — perfect round.'
  if (ratio >= 0.7) return 'Solid. The ones you missed will come back sooner.'
  return 'Rough round — those sentences are now weighted to reappear.'
})

async function loadQuiz() {
  loading.value = true
  error.value = ''
  finished.value = false
  answer.value = null
  index.value = 0
  correctCount.value = 0

  try {
    const quiz = await api.post('/listening/quiz?questions=10')
    questions.value = quiz.questions
    await nextTick()
    replay()
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

  try {
    const result = await api.post('/listening/answer', {
      token: current.value.token,
      selectedClipId: option.clipId,
    })
    answer.value = { ...result, selectedClipId: option.clipId }
    if (result.correct) correctCount.value++
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
  answer.value = null

  if (index.value + 1 >= questions.value.length) {
    finished.value = true
    return
  }

  index.value++
  await nextTick()
  replay()
}

onMounted(loadQuiz)
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
