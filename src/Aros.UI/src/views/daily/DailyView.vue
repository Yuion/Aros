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
  max-width: 44rem;
}

h1 {
  font-size: 1.4rem;
  margin: 0 0 1rem;
}

h2 {
  font-size: 0.95rem;
  margin: 0 0 0.3rem;
}

.status {
  color: #6b7280;
}

.status.error {
  color: #b91c1c;
}

.headline {
  display: flex;
  align-items: center;
  gap: 1rem;
  padding: 1rem 1.1rem;
  border-radius: 12px;
  background: #eef2ff;
  margin-bottom: 0.9rem;
}

.headline.clear {
  background: #ecfdf5;
}

.count {
  display: flex;
  flex-direction: column;
  align-items: center;
  min-width: 5rem;
}

.count strong {
  font-size: 2.1rem;
  line-height: 1;
  color: #3730a3;
}

.headline.clear .count strong {
  color: #047857;
}

.count span {
  font-size: 0.7rem;
  color: #6b7280;
  text-align: center;
}

.sub {
  margin: 0;
  font-size: 0.85rem;
  color: #374151;
}

.actions {
  display: flex;
  gap: 0.6rem;
  flex-wrap: wrap;
  margin-bottom: 0.4rem;
}

.big {
  padding: 0.7rem 1.1rem;
  font-size: 0.95rem;
}

.locked-note {
  margin: 0 0 1rem;
  font-size: 0.75rem;
  color: #9ca3af;
}

.card {
  background: #fff;
  border-radius: 12px;
  padding: 0.9rem 1rem;
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.06);
}

.hint {
  margin: 0 0 0.8rem;
  font-size: 0.78rem;
  color: #6b7280;
}

.tracks {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.tracks li.idle {
  opacity: 0.45;
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
  color: #3730a3;
}

.bar {
  height: 7px;
  border-radius: 4px;
  background: #f1f3f6;
  overflow: hidden;
  margin: 0.25rem 0;
}

.fill {
  height: 100%;
  border-radius: 4px;
  background: #6366f1;
}

.fill.good {
  background: #10b981;
}

.fill.fair {
  background: #f59e0b;
}

.fill.poor {
  background: #ef4444;
}

.fill.unknown {
  background: #94a3b8;
}

.t-foot {
  display: flex;
  gap: 0.8rem;
  font-size: 0.72rem;
  color: #6b7280;
  flex-wrap: wrap;
}
</style>
