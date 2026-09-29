<!--
  Handwriting.

  No mastery, no rests, no "ready now" — writing is deliberately joined to nothing, so a character
  written badly never puts a word back in rotation for a reason no trainer tested. What is left is
  worth having on its own: how clean the hand is, and whether it stays clean with the stroke order
  taken away.
-->
<template>
  <div class="area">
    <p v-if="error" class="error">{{ error }}</p>
    <p v-else-if="loading" class="placeholder">Loading…</p>

    <template v-else-if="data">
      <section class="tiles">
        <StatTile label="Characters written" :value="data.totals.characters"
                  :note="`${data.totals.attempts} attempts`" />
        <StatTile :label="`Clean · ${data.totals.recentDays} days`"
                  :value="percent(data.totals.recentCleanShare)" :note="recentNote" />
        <StatTile label="From memory" :value="percent(memory?.cleanShare)"
                  :note="memory?.attempts ? `${memory.attempts} without the stroke order` : 'not tried yet'" />
        <StatTile label="Backwards strokes" :value="data.totals.backwards" small
                  note="right shape, wrong direction" />
      </section>

      <p v-if="!data.totals.attempts" class="placeholder">
        Nothing written yet.
        <RouterLink to="/writing">Open the writing trainer →</RouterLink>
      </p>

      <template v-else>
        <!-- The comparison this page exists for: copying tests the hand, memory tests whether
             you have the character at all, and averaging the two would hide both -->
        <section class="card">
          <h2>Copying against memory</h2>
          <p class="card-note">
            The share of characters written with no rejected stroke. Copying has the stroke order
            on screen, so the gap between the two is what you actually carry in your head.
          </p>
          <RankedBars :rows="modeRows" />
          <ul class="mode-notes">
            <li v-for="m in data.modes" :key="m.mode">
              <strong>{{ label(m.mode) }}</strong>
              <template v-if="m.attempts">
                — {{ m.attempts }} attempts over {{ m.characters }}
                {{ m.characters === 1 ? 'character' : 'characters' }},
                {{ perStroke(m.perStroke) }} mistakes per stroke,
                {{ m.backwards }} backwards.
              </template>
              <template v-else> — not tried yet.</template>
            </li>
          </ul>
        </section>

        <section class="card">
          <h2>Clean characters by day</h2>
          <p class="card-note">
            <template v-if="data.daily.length">
              {{ data.daily.length }} {{ data.daily.length === 1 ? 'day' : 'days' }} recorded, up to
              the last {{ data.trendDays }}. The line is the share written with no rejected stroke.
            </template>
            <template v-else>Nothing written in the last {{ data.trendDays }} days.</template>
          </p>
          <AccuracyChart :points="data.daily" />
        </section>

        <section v-if="data.hardest.length" class="card">
          <h2>Hardest to write</h2>
          <p class="card-note">
            Ranked by mistakes per stroke, not per character: 街 has more chances to go wrong than
            人, and counting whole characters would just list the complicated ones.
          </p>
          <RankedBars :rows="hardestRows" />
        </section>
      </template>
    </template>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { api } from '@/services/api'
import StatTile from '@/components/stats/StatTile.vue'
import AccuracyChart from '@/components/stats/AccuracyChart.vue'
import RankedBars from '@/components/stats/RankedBars.vue'

const data = ref(null)
const loading = ref(true)
const error = ref('')

const memory = computed(() => data.value?.modes.find((m) => m.mode === 'memory'))

const recentNote = computed(() => {
  const t = data.value?.totals
  if (!t) return ''
  if (!t.recentAttempts) return 'nothing written lately'

  return `${t.recentAttempts} attempts · ${percent(t.cleanShare)} all time`
})

const modeRows = computed(() =>
  (data.value?.modes ?? []).map((m) => ({
    key: m.mode,
    label: label(m.mode),
    sublabel: m.attempts ? `${m.attempts} attempts` : 'not tried yet',
    ratio: m.cleanShare ?? 0,
    value: percent(m.cleanShare),
    detail: `${m.clean}✓ ${m.attempts - m.clean}✗`,
  })),
)

// Worst first, and the bar is the mistake rate itself — long is bad here, unlike every other
// chart on these tabs, so the label says what it measures
const hardestRows = computed(() => {
  const rows = data.value?.hardest ?? []
  const worst = Math.max(...rows.map((r) => r.perStroke), Number.EPSILON)

  return rows.map((row) => ({
    key: row.character,
    label: row.character,
    lang: 'zh',
    sublabel: `${row.attempts} attempts · ${row.clean} clean`,
    ratio: row.perStroke / worst,
    value: perStroke(row.perStroke),
    detail: row.backwards ? `${row.backwards} backwards` : '',
    color: '#c2410c',
  }))
})

function label(mode) {
  return mode === 'memory' ? 'From memory' : 'Copying'
}

function percent(value) {
  return value == null ? '—' : `${Math.round(value * 100)}%`
}

function perStroke(value) {
  return value == null ? '—' : value.toFixed(2)
}

onMounted(async () => {
  try {
    data.value = await api.get('/stats/writing')
  } catch (e) {
    error.value = e.message
  } finally {
    loading.value = false
  }
})
</script>

<style scoped>
@import '@/components/stats/area.css';

.mode-notes {
  list-style: none;
  margin-top: 0.7rem;
  font-size: 0.78rem;
  color: #6b7280;
  line-height: 1.6;
}
</style>
