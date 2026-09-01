<template>
  <div class="stats">
    <header>
      <h1>Listening Stats</h1>
      <p class="subtitle">How your Chinese listening practice is going.</p>
    </header>

    <p v-if="error" class="error">{{ error }}</p>
    <p v-else-if="loading" class="placeholder">Loading…</p>

    <template v-else-if="data">
      <!-- Headline numbers -->
      <section class="tiles">
        <div class="tile">
          <span class="tile-label">Accuracy</span>
          <span class="tile-value">{{ percent(data.totals.accuracy) }}</span>
          <span class="tile-note">{{ data.totals.correct }} of {{ data.totals.answers }} answers</span>
        </div>
        <div class="tile">
          <span class="tile-label">Mastered</span>
          <span class="tile-value">{{ data.totals.mastered }}</span>
          <span class="tile-note">3+ correct in a row</span>
        </div>
        <div class="tile">
          <span class="tile-label">Practiced</span>
          <span class="tile-value">{{ data.totals.practiced }}<span class="tile-of">/{{ data.totals.librarySize }}</span></span>
          <span class="tile-note">{{ data.totals.neverPracticed }} never heard</span>
        </div>
        <div class="tile">
          <span class="tile-label">Last played</span>
          <span class="tile-value small">{{ lastPlayed }}</span>
          <span class="tile-note">{{ data.totals.answers }} answers all time</span>
        </div>
      </section>

      <p v-if="!data.totals.answers" class="placeholder">
        No rounds played yet.
        <RouterLink to="/chinese-listening">Play one →</RouterLink>
      </p>

      <template v-else>
        <!-- Accuracy over time -->
        <section class="card">
          <h2>Accuracy by day</h2>
          <p class="card-note">
            <template v-if="trend.length">
              {{ trend.length }} {{ trend.length === 1 ? 'day' : 'days' }} recorded, up to the last
              {{ data.trendDays }}.
            </template>
            <template v-else>
              Per-answer history has only just started — earlier rounds are counted in the totals
              above but carry no date, so this fills in from your next round.
            </template>
          </p>

          <p v-if="trend.length === 0" class="placeholder small">Nothing recorded yet.</p>

          <figure v-else class="chart" @mouseleave="hover = null">
            <svg :viewBox="`0 0 ${W} ${H}`" role="img" aria-label="Accuracy by day" @mousemove="onMove">
              <!-- gridlines + y labels -->
              <g>
                <template v-for="t in [0, 25, 50, 75, 100]" :key="t">
                  <line :x1="M.l" :x2="W - M.r" :y1="y(t / 100)" :y2="y(t / 100)" class="grid" />
                  <text :x="M.l - 7" :y="y(t / 100) + 3.5" class="tick" text-anchor="end">{{ t }}%</text>
                </template>
              </g>

              <!-- x labels: first and last day in range -->
              <text :x="M.l" :y="H - 5" class="tick" text-anchor="start">{{ shortDate(rangeStart) }}</text>
              <text :x="W - M.r" :y="H - 5" class="tick" text-anchor="end">{{ shortDate(rangeEnd) }}</text>

              <polyline v-if="trend.length > 1" :points="linePoints" class="series-line" />

              <circle
                v-for="p in trend"
                :key="p.date"
                :cx="x(p.date)"
                :cy="y(p.accuracy)"
                :r="hover?.date === p.date ? 5.5 : 4"
                class="series-dot"
              />

              <line
                v-if="hover"
                :x1="x(hover.date)"
                :x2="x(hover.date)"
                :y1="M.t"
                :y2="H - M.b"
                class="crosshair"
              />
            </svg>

            <figcaption v-if="hover" class="tooltip">
              <strong>{{ shortDate(hover.date) }}</strong>
              {{ percent(hover.accuracy) }} · {{ hover.correct }}/{{ hover.answers }} correct
            </figcaption>
            <figcaption v-else class="hint">Hover a point for that day's detail.</figcaption>
          </figure>
        </section>

        <!-- Study list -->
        <section class="card">
          <h2>Needs work</h2>
          <p class="card-note">
            Sentences you've missed, weakest first — these are weighted to come up more often.
          </p>

          <p v-if="!data.needsWork.length" class="placeholder small">
            Nothing missed yet. {{ data.totals.answers }} for {{ data.totals.answers }}.
          </p>

          <ul v-else class="bars">
            <li v-for="row in data.needsWork" :key="row.sentence" class="bar-row">
              <span class="bar-label" lang="zh">{{ row.sentence }}</span>
              <span class="bar-track">
                <span class="bar-fill" :style="{ width: `${Math.max(row.accuracy * 100, 2)}%` }" />
              </span>
              <span class="bar-value">{{ percent(row.accuracy) }}</span>
              <span class="bar-detail">{{ row.correct }}✓ {{ row.wrong }}✗</span>
            </li>
          </ul>
        </section>

        <!-- Mastery distribution -->
        <section class="card">
          <h2>Mastery</h2>
          <p class="card-note">Sentences by how many times in a row you've got them right.</p>

          <ul class="bars ordinal">
            <li v-for="(step, i) in data.mastery" :key="step.label" class="bar-row">
              <span class="bar-label streak">{{ step.label }} in a row</span>
              <span class="bar-track">
                <span
                  class="bar-fill"
                  :style="{ width: `${masteryWidth(step.count)}%`, background: ordinal[i] }"
                />
              </span>
              <span class="bar-value">{{ step.count }}</span>
            </li>
          </ul>
        </section>

        <!-- Untouched -->
        <section v-if="data.untouched.length" class="card">
          <h2>Never heard <span class="count">{{ data.totals.neverPracticed }}</span></h2>
          <p class="card-note">In your library but never played in a round.</p>
          <ul class="untouched">
            <li v-for="sentence in data.untouched" :key="sentence" lang="zh">{{ sentence }}</li>
          </ul>
        </section>
      </template>
    </template>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { api } from '@/services/api'

// Sequential blue, validated against the white card surface (ordinal ramp, all checks pass)
const ordinal = ['#86b6ef', '#5598e7', '#2a78d6', '#1c5cab']

const W = 640
const H = 210
const M = { t: 10, r: 10, b: 24, l: 40 }

const data = ref(null)
const loading = ref(true)
const error = ref('')
const hover = ref(null)

const trend = computed(() => data.value?.daily ?? [])

// Span the days actually recorded rather than a fixed window — with history only a week old,
// a 30-day axis would squeeze every point into the last quarter of the plot.
const rangeEnd = computed(() => todayIso())
const rangeStart = computed(() => trend.value[0]?.date ?? todayIso())

const spanDays = computed(() => {
  const start = new Date(`${rangeStart.value}T00:00:00`)
  const end = new Date(`${rangeEnd.value}T00:00:00`)
  return Math.max(Math.round((end - start) / 86400000), 0)
})

const linePoints = computed(() =>
  trend.value.map((p) => `${x(p.date)},${y(p.accuracy)}`).join(' '),
)

function iso(d) {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

function todayIso() {
  return iso(new Date())
}

function dayIndex(dateStr) {
  const start = new Date(`${rangeStart.value}T00:00:00`)
  const d = new Date(`${dateStr}T00:00:00`)
  return Math.round((d - start) / 86400000)
}

function x(dateStr) {
  const plot = W - M.l - M.r
  return M.l + (spanDays.value === 0 ? plot / 2 : (dayIndex(dateStr) / spanDays.value) * plot)
}

function y(accuracy) {
  return M.t + (1 - accuracy) * (H - M.t - M.b)
}

function onMove(event) {
  if (!trend.value.length) return

  const svg = event.currentTarget
  const rect = svg.getBoundingClientRect()
  const cursor = ((event.clientX - rect.left) / rect.width) * W

  let nearest = trend.value[0]
  let best = Infinity

  for (const point of trend.value) {
    const distance = Math.abs(x(point.date) - cursor)
    if (distance < best) {
      best = distance
      nearest = point
    }
  }

  hover.value = nearest
}

function masteryWidth(count) {
  const max = Math.max(...(data.value?.mastery ?? []).map((m) => m.count), 1)
  return count === 0 ? 0 : Math.max((count / max) * 100, 2)
}

function percent(value) {
  return value == null ? '—' : `${Math.round(value * 100)}%`
}

function shortDate(dateStr) {
  if (!dateStr) return ''
  return new Date(`${dateStr}T00:00:00`).toLocaleDateString(undefined, {
    month: 'short',
    day: 'numeric',
  })
}

const lastPlayed = computed(() => {
  const at = data.value?.totals?.lastPlayed
  if (!at) return '—'

  const days = Math.floor((Date.now() - new Date(at)) / 86400000)
  if (days === 0) return 'Today'
  if (days === 1) return 'Yesterday'
  return `${days}d ago`
})

onMounted(async () => {
  try {
    data.value = await api.get('/stats/listening')
  } catch (e) {
    error.value = e.message
  } finally {
    loading.value = false
  }
})
</script>

<style scoped>
.stats {
  max-width: 760px;
  margin: 0 auto;
  display: flex;
  flex-direction: column;
  gap: 1.25rem;

  --series: #2a78d6;
  --grid: #e1e0d9;
  --axis: #c3c2b7;
  --muted: #898781;
  --ink-2: #52514e;
}

h1 {
  font-size: 1.5rem;
  font-weight: 700;
}

.subtitle {
  color: #6b7280;
  font-size: 0.85rem;
  margin-top: 0.25rem;
}

.error {
  padding: 0.7rem 0.9rem;
  background: #fef2f2;
  border: 1px solid #fecaca;
  border-radius: 8px;
  color: #b91c1c;
  font-size: 0.85rem;
}

.placeholder {
  color: var(--muted);
  font-size: 0.88rem;
  padding: 1.25rem 0;
}

.placeholder.small {
  padding: 0.5rem 0 0.25rem;
  font-size: 0.82rem;
}

.placeholder a {
  color: #6d5bd0;
  margin-left: 0.3rem;
}

/* Stat tiles */
/* Fixed counts, not auto-fit — auto-fit stranded the 4th tile alone on its own row */
.tiles {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 0.75rem;
}

@media (max-width: 640px) {
  .tiles {
    grid-template-columns: repeat(2, 1fr);
  }
}

.tile {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  padding: 0.9rem 1rem;
  background: white;
  border: 1px solid #e5e7eb;
  border-radius: 10px;
}

.tile-label {
  font-size: 0.7rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--muted);
}

.tile-value {
  font-size: 1.9rem;
  font-weight: 700;
  line-height: 1.15;
  color: #0b0b0b;
}

.tile-value.small {
  font-size: 1.25rem;
  padding-top: 0.35rem;
}

.tile-of {
  font-size: 1rem;
  font-weight: 600;
  color: var(--muted);
}

.tile-note {
  font-size: 0.72rem;
  color: var(--ink-2);
}

/* Cards */
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
}

.count {
  font-size: 0.7rem;
  font-weight: 500;
  color: var(--ink-2);
  background: #f0efec;
  border-radius: 999px;
  padding: 0.1rem 0.5rem;
}

.card-note {
  font-size: 0.76rem;
  color: var(--ink-2);
  line-height: 1.5;
  margin: 0.3rem 0 0.9rem;
}

/* Line chart */
.chart {
  margin: 0;
}

.chart svg {
  width: 100%;
  height: auto;
  display: block;
  overflow: visible;
}

.grid {
  stroke: var(--grid);
  stroke-width: 1;
}

.tick {
  fill: var(--muted);
  font-size: 10px;
  font-variant-numeric: tabular-nums;
}

.series-line {
  fill: none;
  stroke: var(--series);
  stroke-width: 2;
  stroke-linejoin: round;
  stroke-linecap: round;
}

.series-dot {
  fill: var(--series);
  stroke: white;
  stroke-width: 2;
}

.crosshair {
  stroke: var(--axis);
  stroke-width: 1;
  stroke-dasharray: 3 3;
}

.tooltip,
.hint {
  font-size: 0.76rem;
  color: var(--ink-2);
  padding-top: 0.5rem;
}

.hint {
  color: var(--muted);
}

/* Bars */
.bars {
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 0.45rem;
}

.bar-row {
  display: grid;
  grid-template-columns: minmax(0, 9rem) 1fr auto auto;
  align-items: center;
  gap: 0.6rem;
}

.bar-label {
  font-size: 0.95rem;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.bar-label.streak {
  font-size: 0.78rem;
  color: var(--ink-2);
}

.bar-track {
  height: 9px;
  background: #f0efec;
  border-radius: 4px;
  overflow: hidden;
}

.bar-fill {
  display: block;
  height: 100%;
  background: var(--series);
  border-radius: 4px;
}

.bar-value {
  font-size: 0.78rem;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  min-width: 2.4rem;
  text-align: right;
}

.bar-detail {
  font-size: 0.72rem;
  color: var(--muted);
  font-variant-numeric: tabular-nums;
  min-width: 3.2rem;
  text-align: right;
}

/* Chips, so a sentence never wraps across lines mid-phrase */
.untouched {
  list-style: none;
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
}

.untouched li {
  font-size: 0.95rem;
  color: var(--ink-2);
  background: #f0efec;
  border-radius: 6px;
  padding: 0.25rem 0.55rem;
  white-space: nowrap;
}

@media (max-width: 520px) {
  .bar-row {
    grid-template-columns: minmax(0, 6.5rem) 1fr auto;
  }

  .bar-detail {
    display: none;
  }
}
</style>
