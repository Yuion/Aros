<template>
  <section class="page">
    <h1>Today</h1>

    <p v-if="loading" class="status">Working out what is due…</p>
    <p v-else-if="error" class="status error">{{ error }}</p>

    <template v-else>
      <!-- What the day costs, before it is started -->
      <div class="headline" :class="{ clear: status.done }">
        <div class="count">
          <strong>{{ status.done ? '✓' : status.planned }}</strong>
          <span>{{ status.done ? 'done for today' : 'questions' }}</span>
        </div>
        <p class="sub">
          <template v-if="status.done">
            Everything due has been answered. Endless practice is open for as long as you want it.
          </template>
          <template v-else>
            {{ status.due }} due in all — today's session takes the {{ status.planned }} that matter
            most, weighted towards what you are weakest at. The rest keep their place.
          </template>
        </p>
      </div>

      <div class="actions">
        <button v-if="!status.done" class="primary big" @click="start">Start today's session</button>
        <button class="secondary big" :disabled="!status.done" @click="endless">
          Endless practice
        </button>
      </div>

      <p v-if="!status.done" class="locked-note">
        Endless practice opens once today's work is done.
      </p>

      <!-- Where the questions are going, and why -->
      <section class="card">
        <h2>The mix</h2>
        <p class="hint">
          Share of the session by weakness: a kind you answer right nine times in ten earns fewer
          questions than one you get right six times in ten. A kind with nothing due is left out.
        </p>

        <ul class="tracks">
          <li v-for="track in sorted" :key="track.key" :class="{ idle: !track.planned }">
            <div class="t-head">
              <span class="t-label">{{ track.label }}</span>
              <span class="t-count">{{ track.planned || '—' }}</span>
            </div>
            <div class="bar">
              <div class="fill" :style="{ width: width(track) }" :class="grade(track)"></div>
            </div>
            <div class="t-foot">
              <span>{{ track.ready }} due</span>
              <span v-if="track.accuracy !== null">{{ Math.round(track.accuracy * 100) }}% right lately</span>
              <span v-else>not asked yet</span>
              <span>{{ track.unmastered }} unmastered</span>
            </div>
          </li>
        </ul>
      </section>
    </template>
  </section>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { api } from '@/services/api'

const router = useRouter()
const status = ref({ planned: 0, due: 0, unmastered: 0, done: false, tracks: [] })
const loading = ref(true)
const error = ref('')

// Biggest share first: the list should read as the plan, not as an alphabet
const sorted = computed(() =>
  [...status.value.tracks].sort((a, b) => b.planned - a.planned || b.ready - a.ready),
)

const widest = computed(() => Math.max(1, ...status.value.tracks.map((t) => t.planned)))

function width(track) {
  return `${Math.round((track.planned / widest.value) * 100)}%`
}

/** Green where it is solid, amber where it slips, red where it is going badly. */
function grade(track) {
  if (track.accuracy === null) return 'unknown'
  if (track.accuracy >= 0.9) return 'good'
  if (track.accuracy >= 0.75) return 'fair'
  return 'poor'
}

function start() {
  router.push('/daily/session')
}

function endless() {
  router.push({ path: '/daily/session', query: { mode: 'endless' } })
}

onMounted(async () => {
  try {
    status.value = await api.get('/daily/status')
  } catch (e) {
    error.value = e.message
  } finally {
    loading.value = false
  }
})
</script>

<style scoped>
.page {
  max-width: 560px;
  margin: 0 auto;
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

h1 {
  font-size: 1.15rem;
  font-weight: 700;
}

h2 {
  font-size: 0.78rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: #9ca3af;
}

.status {
  color: #6b7280;
  font-size: 0.9rem;
}

.status.error {
  color: #b91c1c;
}

/* What the day costs, before it is started */
.headline {
  display: flex;
  align-items: center;
  gap: 1.1rem;
  padding: 1.1rem 1.2rem;
  border-radius: 14px;
  background: white;
  border: 1px solid #ece9f8;
  box-shadow: 0 1px 2px rgba(26, 26, 26, 0.04);
}

.count {
  display: flex;
  flex-direction: column;
  align-items: center;
  min-width: 4.5rem;
  gap: 0.1rem;
}

.count strong {
  font-size: 2.4rem;
  font-weight: 700;
  line-height: 1;
  color: #6d5bd0;
}

.headline.clear .count strong {
  color: #15803d;
}

.count span {
  font-size: 0.68rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: #9ca3af;
  text-align: center;
}

.sub {
  font-size: 0.85rem;
  line-height: 1.5;
  color: #4b5563;
}

.actions {
  display: flex;
  gap: 0.6rem;
  flex-wrap: wrap;
}

.big {
  padding: 0.7rem 1.3rem;
  font-size: 0.95rem;
}

.locked-note {
  font-size: 0.75rem;
  color: #9ca3af;
  margin-top: -0.6rem;
}

.card {
  background: white;
  border-radius: 14px;
  border: 1px solid #ece9f8;
  padding: 1rem 1.1rem 1.1rem;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.hint {
  font-size: 0.78rem;
  line-height: 1.5;
  color: #6b7280;
}

.tracks {
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 0.8rem;
  margin-top: 0.2rem;
}

.tracks li.idle {
  opacity: 0.4;
}

.t-head {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 0.5rem;
}

.t-label {
  font-size: 0.85rem;
  font-weight: 600;
}

.t-count {
  font-variant-numeric: tabular-nums;
  font-weight: 700;
  color: #6d5bd0;
}

.bar {
  height: 6px;
  border-radius: 999px;
  background: #f3f2fa;
  overflow: hidden;
  margin: 0.3rem 0 0.25rem;
}

.fill {
  height: 100%;
  border-radius: 999px;
  background: #6d5bd0;
}

/* Green where it is solid, amber where it slips, red where it is going badly */
.fill.good {
  background: #22c55e;
}

.fill.fair {
  background: #f59e0b;
}

.fill.poor {
  background: #ef4444;
}

.fill.unknown {
  background: #b8bcc4;
}

.t-foot {
  display: flex;
  gap: 0.75rem;
  flex-wrap: wrap;
  font-size: 0.72rem;
  color: #9ca3af;
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
