<template>
  <div class="area">
    <p v-if="error" class="error">{{ error }}</p>
    <p v-else-if="loading" class="placeholder">Loading…</p>

    <template v-else-if="data">
      <section class="tiles">
        <StatTile label="Accuracy" :value="percent(data.totals.accuracy)"
                  :note="`${data.totals.correct} of ${data.totals.answers} answers`" />
        <StatTile label="Mastered" :value="data.totals.mastered"
                  :note="`patterns · ${data.totals.resting} resting`" />
        <StatTile label="Practised" :value="data.totals.practised" :of="data.totals.withDrills"
                  :note="`${data.totals.neverPractised} never produced cold`" />
        <StatTile label="Drills" :value="data.totals.drills" small
                  :note="`from lessons · ${data.totals.noDrills} patterns have none`" />
      </section>

      <p v-if="!data.totals.answers" class="placeholder">
        No rounds yet.
        <RouterLink to="/grammar">Start one →</RouterLink>
      </p>

      <template v-else>
        <section class="card">
          <h2>Accuracy by day</h2>
          <p class="card-note">
            <template v-if="data.daily.length">
              {{ data.daily.length }} {{ data.daily.length === 1 ? 'day' : 'days' }} recorded, up to
              the last {{ data.trendDays }}.
            </template>
            <template v-else>Nothing recorded yet.</template>
          </p>
          <AccuracyChart :points="data.daily" />
        </section>

        <section class="card">
          <h2>Needs work</h2>
          <p class="card-note">
            Patterns you have missed and not yet mastered, weakest first. A pattern is asked with a
            different sentence each time, so a low score here is about the rule, not about one
            sentence you happen to find awkward.
          </p>
          <p v-if="!data.needsWork.length" class="placeholder small">
            Nothing missed yet. {{ data.totals.answers }} for {{ data.totals.answers }}.
          </p>
          <RankedBars v-else :rows="needsWorkRows" />
        </section>

        <section class="card">
          <h2>Mastery</h2>
          <p class="card-note">
            Counted per pattern. Six correct answers to six different sentences master one; a
            pattern you have missed climbs the longer ladder and needs eight.
          </p>
          <RankedBars :rows="masteryRows" scale-to-max />
        </section>
      </template>

      <!-- The tail: taught once, never produced since. The reason this trainer exists. -->
      <section v-if="data.untouched.length" class="card">
        <h2>Taught but never drilled <span class="count">{{ data.untouched.length }}</span></h2>
        <p class="card-note">
          These have drills waiting and have not been asked once. The oldest are the ones most
          likely to have quietly gone.
        </p>
        <ul class="points">
          <li v-for="row in data.untouched" :key="row.title">
            <strong>{{ row.title }}</strong>
            <p class="point-note">
              <template v-if="row.introducedInLesson">lesson {{ row.introducedInLesson }}</template>
              <template v-if="row.date"> · {{ row.date }}</template>
              · {{ row.drills }} {{ row.drills === 1 ? 'drill' : 'drills' }} ready
            </p>
          </li>
        </ul>
      </section>

      <!-- A gap in the library rather than in the learning -->
      <section v-if="data.missingDrills.length" class="card">
        <h2>No drills yet <span class="count">{{ data.missingDrills.length }}</span></h2>
        <p class="card-note">
          Nothing in the lessons exercises these patterns in a sentence, so the trainer cannot ask
          about them. The next lesson that uses one will supply it — press
          <RouterLink to="/grammar">Rebuild from lessons</RouterLink> afterwards.
        </p>
        <ul class="points">
          <li v-for="row in data.missingDrills" :key="row.title">
            <strong>{{ row.title }}</strong>
            <p v-if="row.introducedInLesson" class="point-note">lesson {{ row.introducedInLesson }}</p>
          </li>
        </ul>
      </section>
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

// The same ramp the other tabs use: the bands are one ordered measure, so lightness carries
// the order rather than a set of unrelated hues.
const RAMP_FROM = [191, 215, 245]
const RAMP_TO = [22, 69, 124]

function shade(index, count) {
  const t = count < 2 ? 1 : index / (count - 1)
  const channel = (i) => Math.round(RAMP_FROM[i] + (RAMP_TO[i] - RAMP_FROM[i]) * t)

  return `rgb(${channel(0)}, ${channel(1)}, ${channel(2)})`
}

const data = ref(null)
const loading = ref(true)
const error = ref('')

const needsWorkRows = computed(() =>
  (data.value?.needsWork ?? []).map((row, i) => ({
    key: `${row.title}-${i}`,
    label: row.title,
    sublabel: row.state,
    ratio: row.accuracy,
    value: percent(row.accuracy),
    detail: `${row.correct}✓ ${row.wrong}✗`,
  })),
)

const masteryRows = computed(() =>
  (data.value?.mastery ?? []).map((step, i, all) => ({
    key: step.label,
    label: /^\d+$/.test(step.label) ? `${step.label} in a row` : step.label,
    ratio: step.count,
    value: step.count,
    color: shade(i, all.length),
  })),
)

function percent(value) {
  return value == null ? '—' : `${Math.round(value * 100)}%`
}

onMounted(async () => {
  try {
    data.value = await api.get('/stats/grammar')
  } catch (e) {
    error.value = e.message
  } finally {
    loading.value = false
  }
})
</script>

<style scoped>
@import '@/components/stats/area.css';

.points {
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
  font-size: 0.85rem;
}

.point-note {
  font-size: 0.72rem;
  color: #9ca3af;
  margin-top: 0.1rem;
}
</style>
