<template>
  <div class="area">
    <p v-if="error" class="error">{{ error }}</p>
    <p v-else-if="loading" class="placeholder">Loading…</p>

    <template v-else-if="data">
      <section class="tiles">
        <StatTile label="Accuracy" :value="percent(data.totals.accuracy)"
                  :note="`${data.totals.correct} of ${data.totals.answers} answers`" />
        <StatTile label="Mastered" :value="data.totals.mastered" note="word + direction, 3+ in a row" />
        <StatTile label="Practiced" :value="data.totals.practiced" :of="data.totals.wordsTotal"
                  :note="`${data.totals.neverPracticed} untouched`" />
        <StatTile label="Needs review" :value="data.totals.needsReview" small
                  note="held out of tests" />
      </section>

      <p v-if="!data.totals.answers" class="placeholder">
        No rounds yet.
        <RouterLink to="/vocab">Start one →</RouterLink>
      </p>

      <template v-else>
        <!-- The reason progress is tracked per direction at all -->
        <section class="card">
          <h2>By direction</h2>
          <p class="card-note">
            Recognising a word and producing it are different skills. A gap between the top and
            bottom rows here is the useful signal — untested directions are left blank rather than
            shown as zero.
          </p>
          <RankedBars :rows="directionRows" />
        </section>

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
            Word and direction together, weakest first — a word can be solid one way and weak the other.
          </p>
          <p v-if="!data.needsWork.length" class="placeholder small">
            Nothing missed yet. {{ data.totals.answers }} for {{ data.totals.answers }}.
          </p>
          <RankedBars v-else :rows="needsWorkRows" />
        </section>

        <section v-if="data.untouched.length" class="card">
          <h2>Untouched <span class="count">{{ data.totals.neverPracticed }}</span></h2>
          <p class="card-note">Ready to test, but never drawn yet.</p>
          <ul class="chips">
            <li v-for="w in data.untouched" :key="w" lang="zh">{{ w }}</li>
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

const DIRECTION_LABELS = {
  CharactersToPinyin: '汉字 → pinyin',
  CharactersToEnglish: '汉字 → English',
  PinyinToEnglish: 'pinyin → English',
  EnglishToPinyin: 'English → pinyin',
  PinyinToCharacters: 'pinyin → 汉字',
  EnglishToCharacters: 'English → 汉字',
}

const data = ref(null)
const loading = ref(true)
const error = ref('')

// Worst first, and directions never tested drop to the bottom rather than reading as 0%
const directionRows = computed(() =>
  [...(data.value?.byDirection ?? [])]
    .sort((a, b) => (a.accuracy ?? 2) - (b.accuracy ?? 2))
    .map((row) => ({
      key: row.direction,
      label: DIRECTION_LABELS[row.direction] ?? row.direction,
      ratio: row.accuracy ?? 0,
      value: row.accuracy == null ? '—' : percent(row.accuracy),
      detail: row.answers ? `${row.correct}/${row.answers}` : 'untested',
    })),
)

const needsWorkRows = computed(() =>
  (data.value?.needsWork ?? []).map((row, i) => ({
    key: `${row.characters}-${row.direction}-${i}`,
    label: row.characters,
    sublabel: DIRECTION_LABELS[row.direction] ?? row.direction,
    lang: 'zh',
    ratio: row.accuracy,
    value: percent(row.accuracy),
    detail: `${row.correct}✓ ${row.wrong}✗`,
  })),
)

function percent(value) {
  return value == null ? '—' : `${Math.round(value * 100)}%`
}

onMounted(async () => {
  try {
    data.value = await api.get('/stats/vocab')
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
