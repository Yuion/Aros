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
      <div class="actions">
        <button class="primary" @click="load">Again</button>
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

      <!-- Multiple choice -->
      <ul v-else class="options">
        <li v-for="option in current.options" :key="option.wordId">
          <button
            class="option"
            :class="optionClass(option)"
            :disabled="!!answer"
            lang="zh"
            @click="submitChoice(option)"
          >
            {{ option.characters }}
          </button>
        </li>
      </ul>

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
const loading = ref(true)
const error = ref('')
const field = ref(null)
const nextButton = ref(null)
let advance = null

const current = computed(() => questions.value[index.value])

// A right answer to a typed question moves on by itself; everything else waits for Next
const autoAdvancing = computed(() => !!answer.value?.correct && !!current.value?.typed)

async function load() {
  clearTimeout(advance)
  advance = null
  loading.value = true
  error.value = ''
  finished.value = false
  answer.value = null
  retry.value = ''
  text.value = ''
  index.value = 0
  correctCount.value = 0

  // Length is decided server-side: everything not resting, or a sample of it
  const params = new URLSearchParams()
  if (route.query.direction) params.set('direction', route.query.direction)
  if (route.query.tag) params.set('tag', route.query.tag)
  if (route.query.sweep === 'false') params.set('sweep', 'false')

  try {
    const session = await api.post(`/vocab/session?${params}`)
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

async function submitChoice(option) {
  if (answer.value) return
  await send({ selectedWordId: option.wordId })
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
    answer.value = { ...result, selectedWordId: payload.selectedWordId }

    if (result.correct) {
      correctCount.value++

      // Right answers carry nothing to read, so hold the ✓ briefly and move on. A miss
      // waits: the expected answer is the whole point of showing it.
      if (current.value.typed) advance = setTimeout(next, CORRECT_PAUSE)
    }

    // Enter now works the Next button, so a whole round needs no mouse
    if (!advance) await nextTick(() => nextButton.value?.focus())
  } catch (e) {
    error.value = e.message
  }
}

function optionClass(option) {
  if (!answer.value) return ''
  if (answer.value.characters === option.characters) return 'right'
  if (answer.value.selectedWordId === option.wordId) return 'wrong'
  return 'dimmed'
}

async function next() {
  clearTimeout(advance)
  advance = null

  answer.value = null
  retry.value = ''
  text.value = ''

  if (index.value + 1 >= questions.value.length) {
    finished.value = true
    return
  }

  index.value++
  await focusField()
}

onMounted(load)
onUnmounted(() => clearTimeout(advance))
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

.options {
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 0.6rem;
  width: 100%;
  margin-top: 0.5rem;
}

.option {
  width: 100%;
  padding: 0.9rem;
  font-family: inherit;
  font-size: 1.6rem;
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
