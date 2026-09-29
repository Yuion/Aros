<!--
  The ear, on its own.

  There is no mastery here and no "ready now", because the tone trainer has no ladder: a sound is
  never finished with. What this tab can answer is the only question the trainer was built to ask
  — which of the four you cannot hear, and what you hear instead.
-->
<template>
  <div class="area">
    <p v-if="error" class="error">{{ error }}</p>
    <p v-else-if="loading" class="placeholder">Loading…</p>

    <template v-else-if="data">
      <section class="tiles">
        <StatTile label="Sounds ready" :value="data.totals.withAudio" :of="data.totals.sounds"
                  :note="`${data.totals.syllables} syllables, four tones each`" />
        <StatTile :label="`Accuracy · ${data.totals.recentDays} days`"
                  :value="percent(data.totals.recentAccuracy)" :note="recentNote" />
        <StatTile label="Answer time" :value="seconds(data.totals.medianMs)"
                  note="median — heard, not worked out" />
        <StatTile label="Going wrong" :value="data.totals.trouble" small
                  :note="`sounds missed in ${data.totals.recentDays} days`" />
      </section>

      <p v-if="!data.totals.answers" class="placeholder">
        No rounds yet.
        <RouterLink to="/tones">Start one →</RouterLink>
      </p>

      <template v-else>
        <!-- The headline result: one bar per tone as it was spoken -->
        <section class="card">
          <h2>By tone</h2>
          <p class="card-note">
            How often you name each tone correctly when it is the one played. A low bar is a tone
            your ear does not catch; the row below says what you hear instead.
          </p>
          <RankedBars :rows="toneRows" />
        </section>

        <section v-if="data.confusions.length" class="card">
          <h2>What you mix up</h2>
          <p class="card-note">
            Every wrong answer, grouped. Second for third is the usual pair — both rise, and the
            dip at the start of a third tone is easy to miss at speed.
          </p>
          <ul class="pairs">
            <li v-for="c in data.confusions" :key="`${c.heard}-${c.said}`">
              <span class="pair">
                <span class="mark">{{ MARKS[c.heard - 1] }}</span>
                heard tone {{ c.heard }}
                <span class="arrow">→</span>
                <span class="mark said">{{ MARKS[c.said - 1] }}</span>
                said tone {{ c.said }}
              </span>
              <span class="times">{{ c.times }}×</span>
            </li>
          </ul>
        </section>

        <section class="card">
          <h2>Accuracy by day</h2>
          <p class="card-note">
            <template v-if="data.daily.length">
              {{ data.daily.length }} {{ data.daily.length === 1 ? 'day' : 'days' }} recorded, up to
              the last {{ data.trendDays }}.
            </template>
            <template v-else>Nothing recorded in the last {{ data.trendDays }} days.</template>
          </p>
          <AccuracyChart :points="data.daily" />
        </section>

        <section v-if="data.hardest.length" class="card">
          <h2>Hardest sounds</h2>
          <p class="card-note">
            One syllable in one tone, weakest first. A syllable that keeps appearing here whatever
            the tone is a syllable, not a tone, problem.
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

/** The diacritic each tone wears, the way the trainer's buttons show them. */
const MARKS = ['ˉ', 'ˊ', 'ˇ', 'ˋ']

const data = ref(null)
const loading = ref(true)
const error = ref('')

const recentNote = computed(() => {
  const t = data.value?.totals
  if (!t) return ''
  if (!t.recentAnswered) return 'nothing answered lately'

  return `${t.recentAnswered} answers · ${percent(t.accuracy)} all time`
})

const toneRows = computed(() =>
  (data.value?.byTone ?? []).map((row) => ({
    key: `tone-${row.tone}`,
    label: `${MARKS[row.tone - 1]}  tone ${row.tone}`,
    sublabel: row.asked ? `${row.asked} asked` : 'not yet asked',
    ratio: row.accuracy ?? 0,
    value: percent(row.accuracy),
    detail: `${row.correct}✓ ${row.asked - row.correct}✗`,
  })),
)

const hardestRows = computed(() =>
  (data.value?.hardest ?? []).map((row) => ({
    key: `${row.syllable}-${row.tone}`,
    label: `${row.syllable}${row.tone}`,
    sublabel: `${MARKS[row.tone - 1]} tone ${row.tone}`,
    ratio: row.accuracy,
    value: percent(row.accuracy),
    detail: `${row.asked - row.wrong}✓ ${row.wrong}✗`,
  })),
)

function percent(value) {
  return value == null ? '—' : `${Math.round(value * 100)}%`
}

function seconds(ms) {
  return ms == null ? '—' : `${(ms / 1000).toFixed(1)}s`
}

onMounted(async () => {
  try {
    data.value = await api.get('/stats/tones')
  } catch (e) {
    error.value = e.message
  } finally {
    loading.value = false
  }
})
</script>

<style scoped>
@import '@/components/stats/area.css';

.pairs {
  list-style: none;
  display: flex;
  flex-direction: column;
  font-size: 0.85rem;
}

.pairs li {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.6rem;
  padding: 0.4rem 0;
  border-bottom: 1px solid #f3f4f6;
}

.pairs li:last-child {
  border-bottom: none;
}

.pair {
  display: flex;
  align-items: center;
  gap: 0.35rem;
}

.mark {
  font-size: 1.1rem;
  color: #15803d;
}

.mark.said {
  color: #b91c1c;
}

.arrow {
  color: #9ca3af;
  margin: 0 0.2rem;
}

.times {
  color: #6b7280;
  font-variant-numeric: tabular-nums;
}
</style>
