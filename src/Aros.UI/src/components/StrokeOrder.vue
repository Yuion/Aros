<!--
  How a word is written, one character at a time.

  Two things at once, because they answer different questions. The grid on the right is the
  one printed in every textbook — stroke 1, strokes 1 and 2, and so on, the newest stroke dark
  against the ones already down — and it is what you read while your hand is busy. The box on
  the left writes the character properly when you click it, which is the part a picture cannot
  do: the direction each stroke travels.

  The data comes from the API, which holds the whole dataset locally. Nothing here calls out.
-->
<template>
  <div class="strokes">
    <p v-if="loading" class="note">Looking up the strokes…</p>
    <p v-else-if="error" class="note fail">{{ error }}</p>

    <template v-else>
      <div v-for="(entry, index) in entries" :key="index" class="char">
        <template v-if="entry.data">
          <button class="writer" :title="`Write ${entry.character}`" @click="animate(index)">
            <span :ref="(el) => { if (el) targets[index] = el }" class="canvas" />
            <span class="replay">✍ write it</span>
          </button>

          <ol class="steps">
            <li v-for="n in entry.data.strokes.length" :key="n">
              <svg viewBox="0 0 1024 1024" aria-hidden="true">
                <g class="guide">
                  <line x1="512" y1="0" x2="512" y2="1024" />
                  <line x1="0" y1="512" x2="1024" y2="512" />
                </g>
                <!-- The dataset draws with y pointing up, as fonts do -->
                <g transform="scale(1, -1) translate(0, -900)">
                  <path
                    v-for="(d, stroke) in entry.data.strokes.slice(0, n)"
                    :key="stroke"
                    :d="d"
                    :class="stroke === n - 1 ? 'now' : 'laid'"
                  />
                </g>
              </svg>
            </li>
          </ol>
        </template>

        <p v-else class="note">
          No stroke order recorded for <span lang="zh">{{ entry.character }}</span>.
        </p>
      </div>
    </template>
  </div>
</template>

<script setup>
import { nextTick, onMounted, ref, shallowRef } from 'vue'
import HanziWriter from 'hanzi-writer'
import { api } from '@/services/api'

const props = defineProps({ word: { type: String, required: true } })

// Plain objects on purpose: the library reads these paths on every animation frame, and a
// deep reactive proxy would charge it for the privilege
const entries = shallowRef([])
const loading = ref(true)
const error = ref('')

// A plain ref inside v-for collects an array rather than an element, so each box claims its
// own slot by index — the same reason the readings editor takes a function ref
const targets = ref([])
const writers = []

onMounted(load)

async function load() {
  try {
    entries.value = await api.get(`/strokes?word=${encodeURIComponent(props.word)}`)
  } catch (e) {
    error.value = e.message
    return
  } finally {
    loading.value = false
  }

  await nextTick()
  entries.value.forEach(mount)
}

function mount(entry, index) {
  const target = targets.value[index]
  if (!entry.data || !target) return

  writers[index] = HanziWriter.create(target, entry.character, {
    width: 104,
    height: 104,
    padding: 4,
    strokeColor: '#1f2937',
    outlineColor: '#e5e7eb',
    showOutline: true,
    strokeAnimationSpeed: 0.9,
    delayBetweenStrokes: 180,
    // The character is already here — the data was fetched for the grid, and letting the
    // library fetch it again would be a call to a CDN this machine cannot reach
    charDataLoader: () => entry.data,
  })
}

function animate(index) {
  writers[index]?.animateCharacter()
}
</script>

<style scoped>
.strokes {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.char {
  display: flex;
  align-items: flex-start;
  gap: 0.9rem;
}

.writer {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.3rem;
  padding: 0.4rem;
  border: 1px solid #e5e7eb;
  border-radius: 10px;
  background: white;
  cursor: pointer;
  flex-shrink: 0;
}

.writer:hover {
  border-color: #0066cc;
}

.canvas {
  display: block;
  width: 104px;
  height: 104px;
}

.replay {
  font-size: 0.68rem;
  color: #6b7280;
}

.steps {
  display: flex;
  flex-wrap: wrap;
  gap: 0.35rem;
  margin: 0;
  padding: 0;
  list-style: none;
}

.steps li {
  width: 52px;
  height: 52px;
  border: 1px solid #eef0f3;
  border-radius: 6px;
  background: white;
}

.steps svg {
  width: 100%;
  height: 100%;
}

.guide line {
  stroke: #e8e9ec;
  stroke-width: 12;
  stroke-dasharray: 40 30;
}

/* What is already written, and what this step adds */
.laid {
  fill: #d1d5db;
}

/* Outlined as well as filled: the last stroke of a character is often a thin one, and
   thin and red reads as grey */
.now {
  fill: #b91c1c;
  stroke: #b91c1c;
  stroke-width: 8;
}

.note {
  margin: 0;
  font-size: 0.75rem;
  color: #9ca3af;
}

.fail {
  color: #b91c1c;
}
</style>
