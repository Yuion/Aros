<template>
  <div class="grammar">
    <header>
      <h1>Grammar Trainer</h1>
      <p class="subtitle">
        The patterns your lessons taught, drilled with the sentences those lessons used. Answer by
        building the Chinese from tiles — word order is the whole point.
      </p>
    </header>

    <p v-if="error" class="error">{{ error }}</p>
    <p v-else-if="loading" class="placeholder">Loading…</p>

    <template v-else>
      <section class="card start">
        <div class="standing">
          <span class="stat"><strong>{{ standing.ready }}</strong> ready</span>
          <span class="stat"><strong>{{ standing.resting }}</strong> resting</span>
          <span class="stat"><strong>{{ standing.mastered }}</strong> mastered</span>
        </div>

        <p v-if="standing.restingOut" class="note resting">
          Every pattern is resting — the next is due {{ standing.nextDue }}.
        </p>

        <div class="round-modes">
          <button
            v-for="option in ROUND_MODES"
            :key="option.id"
            class="round-mode"
            :class="{ active: sweep === option.sweep }"
            @click="sweep = option.sweep"
          >
            {{ option.label }}
          </button>
        </div>

        <button class="primary" :disabled="!standing.ready" @click="start">
          Start{{ standing.ready ? ` — ${roundLength}` : '' }}
        </button>
      </section>

      <section class="card">
        <h2>
          Patterns <span class="count">{{ withDrills.length }}</span>
          <button class="link-btn" :disabled="rebuilding" @click="rebuild">
            {{ rebuilding ? 'Rebuilding…' : 'Rebuild from lessons' }}
          </button>
        </h2>
        <p class="card-note">
          Drills come from the exercises your lessons set and the examples recorded with each
          pattern. Rebuild after a lesson to pick up what it added; nothing already held is touched.
        </p>
        <p v-if="report" class="report">
          {{ report.added }} added, {{ report.skipped }} already held —
          {{ report.fromDrills }} written for a pattern, {{ report.fromExamples }} from examples,
          {{ report.fromExercises }} from exercises.
        </p>

        <ul class="points">
          <li v-for="point in points" :key="point.id" :class="point.state">
            <div class="p-head">
              <span class="p-title">{{ point.title }}</span>
              <span class="p-state" :class="point.state">{{ stateLabel(point) }}</span>
            </div>
            <p v-if="point.summary" class="p-summary">{{ point.summary }}</p>
            <p class="p-meta">
              <template v-if="point.introducedInLesson">lesson {{ point.introducedInLesson }} · </template>
              {{ point.drills }} {{ point.drills === 1 ? 'drill' : 'drills' }}
              <template v-if="point.correct || point.wrong">
                · {{ point.correct }}✓ / {{ point.wrong }}✗
              </template>
            </p>
          </li>
        </ul>
      </section>
    </template>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { api } from '@/services/api'

const ROUND_MODES = [
  { id: 'sweep', label: 'Everything', sweep: true },
  { id: 'sample', label: 'Short round', sweep: false },
]

const router = useRouter()
const points = ref([])
const standing = ref({ ready: 0, resting: 0, mastered: 0, perSession: 0 })

// Even "everything" stops at the session budget; what is left over keeps its place in the queue
const roundLength = computed(() => {
  const open = standing.value.ready
  const cap = standing.value.perSession || open
  const asked = Math.min(sweep.value ? open : 10, cap)

  return asked < open ? `${asked} of ${open} patterns` : `${asked} patterns`
})
const sweep = ref(true)
const loading = ref(true)
const rebuilding = ref(false)
const report = ref(null)
const error = ref('')

const withDrills = computed(() => points.value.filter((p) => p.drills > 0))

function stateLabel(point) {
  if (point.state === 'unavailable') return 'no drills yet'
  if (point.state === 'mastered') return 'mastered'
  if (point.state === 'resting') return 'resting'

  return point.streak > 0 ? `ready · ${point.streak} in a row` : 'ready'
}

async function load() {
  try {
    const [list, available] = await Promise.all([
      api.get('/grammar/points'),
      api.get('/grammar/availability'),
    ])
    points.value = list
    standing.value = available
  } catch (e) {
    error.value = e.message
  } finally {
    loading.value = false
  }
}

async function rebuild() {
  rebuilding.value = true
  error.value = ''

  try {
    report.value = await api.post('/grammar/rebuild')
    await load()
  } catch (e) {
    error.value = e.message
  } finally {
    rebuilding.value = false
  }
}

function start() {
  router.push({ path: '/grammar/round', query: sweep.value ? {} : { sweep: 'false' } })
}

onMounted(load)
</script>

<style scoped>
.grammar {
  max-width: 680px;
  margin: 0 auto;
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

h1 {
  font-size: 1.5rem;
  font-weight: 700;
}

.subtitle {
  color: #6b7280;
  font-size: 0.85rem;
  margin-top: 0.25rem;
  line-height: 1.5;
}

.card {
  padding: 1.1rem 1.2rem;
  background: white;
  border: 1px solid #e5e7eb;
  border-radius: 10px;
}

.card h2 {
  font-size: 0.95rem;
  font-weight: 600;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.4rem;
}

.count {
  font-size: 0.7rem;
  font-weight: 500;
  color: #4b5563;
  background: #f0efec;
  border-radius: 999px;
  padding: 0.1rem 0.5rem;
}

.card-note {
  font-size: 0.76rem;
  color: #4b5563;
  line-height: 1.5;
  margin-bottom: 0.6rem;
}

.link-btn {
  margin-left: auto;
  background: none;
  border: none;
  font: inherit;
  font-size: 0.76rem;
  color: #6d5bd0;
  cursor: pointer;
  text-decoration: underline;
}

.link-btn:disabled {
  color: #9ca3af;
  cursor: default;
}

.report {
  font-size: 0.76rem;
  color: #15803d;
  background: #f0fdf4;
  border-radius: 7px;
  padding: 0.4rem 0.6rem;
  margin-bottom: 0.6rem;
}

.start {
  display: flex;
  flex-direction: column;
  gap: 0.8rem;
  align-items: flex-start;
}

.standing {
  display: flex;
  gap: 1rem;
  font-size: 0.8rem;
  color: #6b7280;
}

.stat strong {
  font-size: 1.1rem;
  color: #1a1a1a;
  font-weight: 700;
  margin-right: 0.2rem;
}

.note.resting {
  font-size: 0.8rem;
  color: #92400e;
  background: #fffbeb;
  border-radius: 7px;
  padding: 0.4rem 0.6rem;
}

.round-modes {
  display: flex;
  gap: 0.4rem;
}

.round-mode {
  font: inherit;
  font-size: 0.8rem;
  padding: 0.35rem 0.7rem;
  border: 1px solid #e5e7eb;
  border-radius: 999px;
  background: white;
  color: #4b5563;
  cursor: pointer;
}

.round-mode.active {
  border-color: #6d5bd0;
  color: #6d5bd0;
}

.primary {
  font: inherit;
  font-size: 0.9rem;
  font-weight: 600;
  padding: 0.6rem 1.2rem;
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

.points {
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}

.points li {
  padding: 0.5rem 0.65rem;
  border: 1px solid #f0efec;
  border-radius: 7px;
}

.points li.unavailable {
  opacity: 0.55;
}

.p-head {
  display: flex;
  align-items: baseline;
  gap: 0.5rem;
}

.p-title {
  font-size: 0.9rem;
  font-weight: 600;
  flex: 1;
}

.p-state {
  font-size: 0.68rem;
  font-weight: 600;
  white-space: nowrap;
}

.p-state.ready {
  color: #6d5bd0;
}

.p-state.resting {
  color: #92400e;
}

.p-state.mastered {
  color: #15803d;
}

.p-state.unavailable {
  color: #b8bcc4;
}

.p-summary {
  font-size: 0.78rem;
  color: #4b5563;
  line-height: 1.45;
  margin-top: 0.15rem;
}

.p-meta {
  font-size: 0.7rem;
  color: #9ca3af;
  margin-top: 0.2rem;
}

.error {
  color: #b91c1c;
  font-size: 0.85rem;
}

.placeholder {
  color: #9ca3af;
  font-size: 0.85rem;
}
</style>
