<template>
  <div class="area">
    <p v-if="error" class="error">{{ error }}</p>
    <p v-else-if="loading" class="placeholder">Loading…</p>

    <template v-else-if="data">
      <section class="tiles">
        <StatTile label="Lessons" :value="data.totals.lessons"
                  :note="span" />
        <StatTile label="Vocabulary" :value="data.totals.vocabulary"
                  :note="`${data.totals.unattributed} added outside a lesson`" />
        <StatTile label="Grammar" :value="data.totals.grammar"
                  :note="`${data.totals.rules} pronunciation rules`" />
        <StatTile label="Exercises" :value="data.totals.exercises" small
                  :note="`${data.totals.exercisesAnswered} answered`" />
      </section>

      <!-- A lesson under way has not been written up, and only a write-up records anything -->
      <section v-if="data.inProgress" class="card running">
        <h2>Lesson in progress <span class="count">not yet recorded</span></h2>
        <p class="card-note">
          {{ data.inProgress.lessonId }} · {{ data.inProgress.phase.toLowerCase() }}
          <template v-if="data.inProgress.minutesRequested">
            · {{ data.inProgress.minutesElapsed }} of {{ data.inProgress.minutesRequested }} minutes
          </template>
          · {{ data.inProgress.exercisesSent }} exercises set
        </p>
        <p class="card-note">
          Nothing here reaches the chronicle until you press End lesson and save the write-up.
        </p>
        <ul v-if="data.inProgress.newVocabulary.length" class="chips">
          <li v-for="c in data.inProgress.newVocabulary" :key="c" lang="zh">{{ c }}</li>
        </ul>
      </section>

      <p v-if="!data.lessons.length" class="placeholder">
        No lessons recorded yet.
        <RouterLink to="/tutor">Start one →</RouterLink>
      </p>

      <!-- Newest first: after a lesson, the last one is the one you want -->
      <ol v-else class="chronicle">
        <li v-for="lesson in data.lessons" :key="lesson.number" class="entry">
          <header class="entry-head">
            <span class="lesson-no">{{ lesson.number }}</span>
            <div>
              <p class="entry-date">
                {{ lesson.date }}
                <span v-if="lesson.durationMinutes" class="entry-length">· {{ lesson.durationMinutes }} min</span>
              </p>
              <p v-if="lesson.summary" class="entry-summary">{{ lesson.summary }}</p>
            </div>
          </header>

          <div v-if="lesson.vocabulary.length" class="block">
            <h3>New vocabulary</h3>
            <ul class="words">
              <li v-for="w in lesson.vocabulary" :key="w.characters" :class="{ gone: !w.known }">
                <span lang="zh" class="w-chars">{{ w.characters }}</span>
                <span v-if="w.pinyin" class="w-pinyin">{{ w.pinyin }}</span>
                <span v-if="w.english" class="w-english">{{ w.english }}</span>
                <span v-if="!w.known" class="w-note">no longer in the pool</span>
              </li>
            </ul>
          </div>

          <div v-if="lesson.grammar.length || lesson.grammarMentioned.length" class="block">
            <h3>New grammar</h3>
            <ul class="points">
              <li v-for="g in lesson.grammar" :key="g.title">
                <strong>{{ g.title }}</strong>
                <span class="status" :class="g.status.toLowerCase()">{{ g.status.toLowerCase() }}</span>
                <p v-if="g.summary" class="point-note">{{ g.summary }}</p>
              </li>
              <li v-for="m in lesson.grammarMentioned" :key="m.name" class="mentioned">
                {{ m.name }}
                <span class="w-note">
                  {{ m.recordedIn ? `recorded under lesson ${m.recordedIn}` : 'no entry of its own' }}
                </span>
              </li>
            </ul>
          </div>

          <div v-if="lesson.rules.length" class="block">
            <h3>Pronunciation</h3>
            <ul class="points">
              <li v-for="r in lesson.rules" :key="r.title">
                <strong>{{ r.title }}</strong>
                <p v-if="r.summary" class="point-note">{{ r.summary }}</p>
              </li>
            </ul>
          </div>

          <div v-if="lesson.reinforced.length" class="block">
            <h3>Reinforced</h3>
            <ul class="chips">
              <li v-for="r in lesson.reinforced" :key="r" :lang="hasHan(r) ? 'zh' : undefined">{{ r }}</li>
            </ul>
          </div>

          <div v-if="lesson.exercises.length" class="block">
            <h3>Exercises <span class="count">{{ lesson.exercises.length }}</span></h3>
            <ul class="chips">
              <li v-for="e in lesson.exercises" :key="e.key" :class="{ unanswered: !e.answered }">
                {{ e.key }} · {{ e.type || 'exercise' }} · {{ e.items }} items
              </li>
            </ul>
          </div>

          <p v-if="lesson.mistakes" class="block mistakes">{{ lesson.mistakes }}</p>

          <p v-if="lesson.nextRecommendedTopic" class="block next">
            Next suggested: {{ lesson.nextRecommendedTopic }}
          </p>
        </li>
      </ol>

      <section v-if="data.weakPoints.length" class="card">
        <h2>Weak points</h2>
        <p class="card-note">
          What the tutor noticed and no trainer can measure — a tone missed aloud, a pattern
          misused in writing. Resolved ones are kept, so a weakness that returns reads as a
          relapse rather than a new problem.
        </p>
        <ul class="points">
          <li v-for="w in data.weakPoints" :key="w.target + w.type" :class="{ done: w.resolved }">
            <strong :lang="hasHan(w.target) ? 'zh' : undefined">{{ w.target }}</strong>
            <span class="status" :class="w.resolved ? 'learned' : 'shaky'">
              {{ w.resolved ? 'resolved' : `severity ${w.severity}` }}
            </span>
            <p class="point-note">{{ w.type }} · first seen {{ day(w.firstSeen) }}</p>
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

const data = ref(null)
const loading = ref(true)
const error = ref('')

const span = computed(() => {
  const t = data.value?.totals
  if (!t?.lessons) return 'none yet'
  if (t.minutes) return `${t.first} to ${t.last} · ${t.minutes} min recorded`
  return `${t.first} to ${t.last}`
})

function hasHan(text) {
  return /[一-鿿]/.test(text ?? '')
}

function day(value) {
  return value ? String(value).slice(0, 10) : ''
}

onMounted(async () => {
  try {
    data.value = await api.get('/stats/tutor')
  } catch (e) {
    error.value = e.message
  } finally {
    loading.value = false
  }
})
</script>

<style scoped>
@import '@/components/stats/area.css';

.running {
  border-color: #bfdbfe;
  background: #eff6ff;
}

.chronicle {
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 0.9rem;
}

.entry {
  padding: 1rem 1.1rem;
  background: white;
  border: 1px solid #eceaf5;
  border-radius: 10px;
}

.entry-head {
  display: flex;
  gap: 0.8rem;
  align-items: flex-start;
}

/* The number carries the order, so it reads as a chronicle rather than a list of cards */
.lesson-no {
  flex-shrink: 0;
  width: 2rem;
  height: 2rem;
  display: grid;
  place-items: center;
  font-size: 0.85rem;
  font-weight: 700;
  color: white;
  background: #6d5bd0;
  border-radius: 50%;
}

.entry-date {
  font-size: 0.78rem;
  color: #6b7280;
}

.entry-length {
  color: #9ca3af;
}

.entry-summary {
  margin-top: 0.2rem;
  font-size: 0.9rem;
  line-height: 1.55;
}

.block {
  margin-top: 0.8rem;
}

.block h3 {
  font-size: 0.72rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: #9ca3af;
  margin-bottom: 0.35rem;
}

.words {
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
}

.words li {
  display: grid;
  grid-template-columns: auto 6rem 1fr;
  gap: 0.6rem;
  align-items: baseline;
  font-size: 0.85rem;
}

.w-chars {
  font-size: 1.15rem;
  font-weight: 600;
}

.w-pinyin {
  color: #6d5bd0;
}

.w-english {
  color: #4b5563;
}

.words li.gone {
  opacity: 0.55;
}

.w-note {
  font-size: 0.72rem;
  color: #9ca3af;
}

.points {
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
  font-size: 0.85rem;
}

.points .done {
  opacity: 0.55;
}

.status {
  margin-left: 0.4rem;
  font-size: 0.68rem;
  font-weight: 600;
  border-radius: 999px;
  padding: 0.05rem 0.45rem;
}

.status.learned {
  color: #15803d;
  background: #f0fdf4;
}

.status.introduced {
  color: #1e40af;
  background: #eff6ff;
}

.status.shaky {
  color: #92400e;
  background: #fffbeb;
}

.point-note {
  font-size: 0.78rem;
  color: #6b7280;
  line-height: 1.5;
}

.mentioned {
  color: #6b7280;
}

.chips li.unanswered {
  opacity: 0.6;
  border-style: dashed;
}

.mistakes {
  font-size: 0.82rem;
  color: #92400e;
  background: #fffbeb;
  border-radius: 7px;
  padding: 0.4rem 0.6rem;
}

.next {
  font-size: 0.8rem;
  color: #6b7280;
  border-top: 1px solid #f3f4f6;
  padding-top: 0.6rem;
}

@media (max-width: 560px) {
  .words li {
    grid-template-columns: auto 1fr;
  }

  .w-english {
    display: none;
  }
}
</style>
