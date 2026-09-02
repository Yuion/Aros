<template>
  <p v-if="!points.length" class="placeholder">Nothing recorded yet.</p>

  <figure v-else class="chart" @mouseleave="hover = null">
    <svg :viewBox="`0 0 ${W} ${H}`" role="img" aria-label="Accuracy by day" @mousemove="onMove">
      <template v-for="t in [0, 25, 50, 75, 100]" :key="t">
        <line :x1="M.l" :x2="W - M.r" :y1="y(t / 100)" :y2="y(t / 100)" class="grid" />
        <text :x="M.l - 7" :y="y(t / 100) + 3.5" class="tick" text-anchor="end">{{ t }}%</text>
      </template>

      <text :x="M.l" :y="H - 5" class="tick" text-anchor="start">{{ shortDate(rangeStart) }}</text>
      <text :x="W - M.r" :y="H - 5" class="tick" text-anchor="end">{{ shortDate(rangeEnd) }}</text>

      <polyline v-if="points.length > 1" :points="linePoints" class="series-line" />

      <circle
        v-for="p in points"
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
      {{ Math.round(hover.accuracy * 100) }}% · {{ hover.correct }}/{{ hover.answers }} correct
    </figcaption>
    <figcaption v-else class="hint">Hover a point for that day's detail.</figcaption>
  </figure>
</template>

<script setup>
import { computed, ref } from 'vue'

const props = defineProps({
  points: { type: Array, required: true },   // [{ date, answers, correct, accuracy }]
})

// Sequential blue, validated against the white card surface
const W = 640
const H = 210
const M = { t: 10, r: 10, b: 24, l: 40 }

const hover = ref(null)

const rangeEnd = computed(() => iso(new Date()))
const rangeStart = computed(() => props.points[0]?.date ?? rangeEnd.value)

// Span the days actually recorded — a fixed window would squeeze a short history
// into a corner of the plot.
const spanDays = computed(() => {
  const start = new Date(`${rangeStart.value}T00:00:00`)
  const end = new Date(`${rangeEnd.value}T00:00:00`)
  return Math.max(Math.round((end - start) / 86400000), 0)
})

const linePoints = computed(() => props.points.map((p) => `${x(p.date)},${y(p.accuracy)}`).join(' '))

function iso(d) {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

function x(dateStr) {
  const plot = W - M.l - M.r
  if (spanDays.value === 0) return M.l + plot / 2

  const start = new Date(`${rangeStart.value}T00:00:00`)
  const day = Math.round((new Date(`${dateStr}T00:00:00`) - start) / 86400000)
  return M.l + (day / spanDays.value) * plot
}

function y(accuracy) {
  return M.t + (1 - accuracy) * (H - M.t - M.b)
}

function onMove(event) {
  if (!props.points.length) return

  const rect = event.currentTarget.getBoundingClientRect()
  const cursor = ((event.clientX - rect.left) / rect.width) * W

  let nearest = props.points[0]
  let best = Infinity

  for (const point of props.points) {
    const distance = Math.abs(x(point.date) - cursor)
    if (distance < best) {
      best = distance
      nearest = point
    }
  }

  hover.value = nearest
}

function shortDate(dateStr) {
  if (!dateStr) return ''
  return new Date(`${dateStr}T00:00:00`).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
}
</script>

<style scoped>
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
  stroke: #e1e0d9;
  stroke-width: 1;
}

.tick {
  fill: #898781;
  font-size: 10px;
  font-variant-numeric: tabular-nums;
}

.series-line {
  fill: none;
  stroke: #2a78d6;
  stroke-width: 2;
  stroke-linejoin: round;
  stroke-linecap: round;
}

.series-dot {
  fill: #2a78d6;
  stroke: white;
  stroke-width: 2;
}

.crosshair {
  stroke: #c3c2b7;
  stroke-width: 1;
  stroke-dasharray: 3 3;
}

.tooltip,
.hint {
  font-size: 0.76rem;
  color: #52514e;
  padding-top: 0.5rem;
}

.hint {
  color: #898781;
}

.placeholder {
  color: #898781;
  font-size: 0.82rem;
  padding: 0.5rem 0 0.25rem;
}
</style>
