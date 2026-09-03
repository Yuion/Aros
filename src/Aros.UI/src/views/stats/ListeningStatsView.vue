<template>
  <div class="area">
    <p v-if="error" class="error">{{ error }}</p>
    <p v-else-if="loading" class="placeholder">Loading…</p>

    <template v-else-if="data">
      <section class="tiles">
        <StatTile label="Accuracy" :value="percent(data.totals.accuracy)"
                  :note="`${data.totals.correct} of ${data.totals.answers} answers`" />
        <StatTile label="Mastered" :value="data.totals.mastered"
                  :note="`retired · ${data.totals.resting} resting`" />
        <StatTile label="Practiced" :value="data.totals.practiced" :of="data.totals.librarySize"
                  :note="`${data.totals.neverPracticed} never heard`" />
        <StatTile label="Last played" :value="lastPlayed" small
                  :note="`${data.totals.answers} answers all time`" />
      </section>

      <p v-if="!data.totals.answers" class="placeholder">
        No rounds played yet.
        <RouterLink to="/chinese-listening">Play one →</RouterLink>
      </p>

      <template v-else>
        <section class="card">
          <h2>Accuracy by day</h2>
          <p class="card-note">
            <template v-if="data.daily.length">
              {{ data.daily.length }} {{ data.daily.length === 1 ? 'day' : 'days' }} recorded, up to
              the last {{ data.trendDays }}.
            </template>
            <template v-else>
              Per-answer history has only just started — earlier rounds are counted in the totals
              above but carry no date.
            </template>
          </p>
          <AccuracyChart :points="data.daily" />
        </section>

        <section class="card">
          <h2>Needs work</h2>
          <p class="card-note">
            Sentences you've missed, weakest first — these are weighted to come up more often.
          </p>
          <p v-if="!data.needsWork.length" class="placeholder small">
            Nothing missed yet. {{ data.totals.answers }} for {{ data.totals.answers }}.
          </p>
          <RankedBars v-else :rows="needsWorkRows" />
        </section>

        <section class="card">
          <h2>Mastery</h2>
          <p class="card-note">
            Sentences by how many times in a row you've got them right. Five in a row sends a
            sentence to rest for a week, then two weeks, then four; the next correct answer
            masters it and it leaves the pool.
          </p>
          <RankedBars :rows="masteryRows" scale-to-max />
        </section>

        <section v-if="data.untouched.length" class="card">
          <h2>Never heard <span class="count">{{ data.totals.neverPracticed }}</span></h2>
          <ul class="chips">
            <li v-for="s in data.untouched" :key="s" lang="zh">{{ s }}</li>
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
import StatTile from '@/components/stats/StatTile.vue'
import AccuracyChart from '@/components/stats/AccuracyChart.vue'
import RankedBars from '@/components/stats/RankedBars.vue'

// Ordinal blue ramp, validated against the white card surface
const ORDINAL = ['#bfd7f5', '#9dc3ef', '#7aade9', '#5598e7', '#2a78d6', '#1f66b8', '#16457c']

const data = ref(null)
const loading = ref(true)
const error = ref('')

const needsWorkRows = computed(() =>
  (data.value?.needsWork ?? []).map((row) => ({
    key: row.sentence,
    label: row.sentence,
    lang: 'zh',
    ratio: row.accuracy,
    value: percent(row.accuracy),
    detail: `${row.correct}✓ ${row.wrong}✗`,
  })),
)

const masteryRows = computed(() =>
  (data.value?.mastery ?? []).map((step, i) => ({
    key: step.label,
    label: /^\d+$/.test(step.label) ? `${step.label} in a row` : step.label,
    ratio: step.count,
    value: step.count,
    color: ORDINAL[i],
  })),
)

const lastPlayed = computed(() => {
  const at = data.value?.totals?.lastPlayed
  if (!at) return '—'

  const days = Math.floor((Date.now() - new Date(at)) / 86400000)
  if (days === 0) return 'Today'
  if (days === 1) return 'Yesterday'
  return `${days}d ago`
})

function percent(value) {
  return value == null ? '—' : `${Math.round(value * 100)}%`
}

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
@import '@/components/stats/area.css';
</style>
