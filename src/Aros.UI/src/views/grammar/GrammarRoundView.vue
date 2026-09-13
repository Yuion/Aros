<template>
  <div class="session">
    <p v-if="loading" class="status">Building your round…</p>

    <p v-else-if="error" class="status error">
      {{ error }}
      <RouterLink to="/grammar">Back</RouterLink>
    </p>

    <!-- Score -->
    <section v-else-if="finished" class="scorecard">
      <p class="score-label">Round complete</p>
      <p class="score">{{ correctCount }}<span class="score-total">/{{ questions.length }}</span></p>

      <div v-if="missed.length" class="review">
        <h2>Worth another look</h2>
        <ul class="missed">
          <li v-for="(miss, i) in missed" :key="i">
            <span class="m-pattern">{{ miss.pattern }}</span>
            <span class="m-prompt">{{ miss.prompt }}</span>
            <span class="m-answer" lang="zh">{{ miss.expected }}</span>
          </li>
        </ul>
      </div>

      <div class="actions">
        <button class="primary" @click="load">Again</button>
        <RouterLink to="/grammar" class="secondary">Done</RouterLink>
      </div>
    </section>

    <!-- A question -->
    <section v-else class="round">
      <header class="progress">
        <span>{{ index + 1 }} / {{ questions.length }}</span>
        <span class="tally">{{ correctCount }} correct</span>
      </header>

      <p class="pattern">{{ current.pattern }}</p>
      <p class="prompt">{{ current.prompt }}</p>

      <div class="tiles">
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
          <span v-if="!built.length" class="built-hint">Build the sentence</span>
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
          <button v-if="!answer" class="primary" :disabled="!built.length" @click="submit">Check</button>
        </div>
      </div>

      <div v-if="answer" class="feedback">
        <p :class="answer.correct ? 'right' : 'wrong'">
          {{ answer.correct ? '✓ Correct' : '✗ Not quite' }}
        </p>
        <p v-if="answer.note" class="note">{{ answer.note }}</p>
        <p v-if="!answer.correct" class="expected" lang="zh">{{ answer.expected }}</p>
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

// Long enough to register the ✓, short enough that a round does not stall on it
const CORRECT_PAUSE = 1000

const route = useRoute()

const questions = ref([])
const index = ref(0)
const answer = ref(null)
const correctCount = ref(0)
const missed = ref([])
const finished = ref(false)
const loading = ref(true)
const error = ref('')
const nextButton = ref(null)
let advance = null

// Indexes into current.tiles, in the order they were tapped
const used = ref([])

const current = computed(() => questions.value[index.value])
const built = computed(() => used.value.map((i) => current.value?.tiles?.[i] ?? ''))
const autoAdvancing = computed(() => !!answer.value?.correct)

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

async function submit() {
  if (answer.value || !built.value.length) return

  try {
    const result = await api.post('/grammar/answer', {
      token: current.value.token,
      text: built.value.join(''),
    })

    answer.value = result

    if (result.correct) correctCount.value++
    else
      missed.value = [
        ...missed.value,
        { pattern: current.value.pattern, prompt: current.value.prompt, expected: result.expected },
      ]

    if (result.correct) advance = setTimeout(next, CORRECT_PAUSE)
    else await nextTick(() => nextButton.value?.focus())
  } catch (e) {
    error.value = e.message
  }
}

async function next() {
  clearTimeout(advance)
  advance = null

  answer.value = null
  used.value = []

  if (index.value + 1 >= questions.value.length) {
    finished.value = true
    return
  }

  index.value++
}

/** A round with no mouse: only the first nine tiles carry a digit. */
function onKey(event) {
  if (loading.value || finished.value || answer.value) return
  if (event.ctrlKey || event.altKey || event.metaKey) return

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
    submit()
    event.preventDefault()
  }
}

async function load() {
  clearTimeout(advance)
  advance = null
  loading.value = true
  error.value = ''
  finished.value = false
  answer.value = null
  used.value = []
  index.value = 0
  correctCount.value = 0
  missed.value = []

  const params = new URLSearchParams()
  if (route.query.sweep === 'false') params.set('sweep', 'false')

  try {
    const round = await api.post(`/grammar/round?${params}`)
    questions.value = round.questions
  } catch (e) {
    error.value = e.message
  } finally {
    loading.value = false
  }
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

/* The pattern is named: this is a grammar test, not a vocabulary one, and knowing which rule is
   being asked about is part of the question rather than a giveaway */
.pattern {
  font-size: 0.72rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: #9ca3af;
  text-align: center;
}

.prompt {
  font-size: 1.25rem;
  font-weight: 600;
  text-align: center;
  line-height: 1.4;
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
  font-family: inherit;
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
  font-family: inherit;
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
  font-size: 0.85rem;
  color: #6b7280;
  text-decoration: none;
  padding: 0.55rem 1.1rem;
}

.feedback {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.4rem;
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
  font-size: 1.2rem;
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
  color: #6d5bd0;
}

.score-total {
  font-size: 1.5rem;
  color: #9ca3af;
}

.review {
  width: 100%;
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
}

.missed li {
  display: flex;
  flex-direction: column;
  gap: 0.1rem;
  padding: 0.45rem 0.6rem;
  background: white;
  border: 1px solid #f0efec;
  border-radius: 7px;
}

.m-pattern {
  font-size: 0.66rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: #b8bcc4;
}

.m-prompt {
  font-size: 0.82rem;
  color: #4b5563;
}

.m-answer {
  font-size: 1.05rem;
}

.actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}
</style>
