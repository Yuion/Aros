<!--
  One long text to translate.

  The reading and the translation are both here and both hidden to begin with. A text with its
  pinyin printed under it is a pronunciation exercise, and a text with the English beside it is
  not a translation task at all — so each is a button, pressed when you have done what you can.
-->
<template>
  <article class="text-card">
    <header class="head">
      <div>
        <h3>{{ text.title || 'Untitled text' }}</h3>
        <p class="meta">
          {{ text.characters }} characters
          <template v-if="text.wordsUsed?.length"> · {{ text.wordsUsed.length }} of your words</template>
          <template v-if="date"> · {{ date }}</template>
        </p>
      </div>

      <div class="actions">
        <button class="ghost" @click="showPinyin = !showPinyin">
          {{ showPinyin ? 'Hide reading' : 'Reading' }}
        </button>
        <button class="ghost" @click="showEnglish = !showEnglish">
          {{ showEnglish ? 'Hide translation' : 'Translation' }}
        </button>
        <button v-if="text.hasAudio" class="ghost" @click="play">▶ Listen</button>
        <button v-else class="ghost" :disabled="speaking" @click="$emit('speak', text)">
          {{ speaking ? 'Speaking…' : 'Add audio' }}
        </button>
        <button v-if="deletable" class="ghost danger" @click="$emit('delete', text)">Delete</button>
      </div>
    </header>

    <p class="chinese" lang="zh">{{ text.chinese }}</p>

    <p v-if="showPinyin" class="pinyin">{{ text.pinyin }}</p>

    <p v-if="text.notes" class="notes">{{ text.notes }}</p>

    <!-- The answer key. Nothing opens it but a press. -->
    <div v-if="showEnglish" class="english">
      <h4>The tutor's translation</h4>
      <p>{{ text.english }}</p>
    </div>

    <details v-if="text.wordsUsed?.length || text.grammarUsed?.length" class="coverage">
      <summary>What it covers</summary>
      <p v-if="text.wordsUsed?.length" class="covered">
        <span v-for="w in text.wordsUsed" :key="w" lang="zh" class="chip">{{ w }}</span>
      </p>
      <ul v-if="text.grammarUsed?.length" class="patterns">
        <li v-for="g in text.grammarUsed" :key="g">{{ g }}</li>
      </ul>
    </details>

    <audio v-if="text.hasAudio" ref="player" :src="text.audioUrl" preload="none" controls class="player" />
  </article>
</template>

<script setup>
import { computed, ref } from 'vue'

const props = defineProps({
  text: { type: Object, required: true },
  speaking: Boolean,
  deletable: Boolean,
})

defineEmits(['speak', 'delete'])

const showPinyin = ref(false)
const showEnglish = ref(false)
const player = ref(null)

const date = computed(() =>
  props.text.createdAt ? new Date(props.text.createdAt).toLocaleDateString() : '',
)

function play() {
  player.value?.play()
}
</script>

<style scoped>
.text-card {
  background: white;
  border: 1px solid #e5e7eb;
  border-radius: 10px;
  padding: 1rem 1.1rem;
}

.head {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 0.8rem;
  flex-wrap: wrap;
}

h3 {
  font-size: 0.95rem;
  font-weight: 700;
}

h4 {
  font-size: 0.75rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: #898781;
  margin-bottom: 0.25rem;
}

.meta {
  font-size: 0.72rem;
  color: #9ca3af;
  margin-top: 0.15rem;
}

.actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.3rem;
}

.ghost {
  font: inherit;
  font-size: 0.75rem;
  padding: 0.3rem 0.6rem;
  background: white;
  border: 1px solid #e5e7eb;
  border-radius: 7px;
  color: #4b5563;
  cursor: pointer;
}

.ghost:hover:not(:disabled) {
  border-color: #6d5bd0;
  color: #6d5bd0;
}

.ghost.danger:hover {
  border-color: #dc2626;
  color: #b91c1c;
}

.ghost:disabled {
  opacity: 0.55;
  cursor: default;
}

.chinese {
  margin-top: 0.8rem;
  font-size: 1.25rem;
  line-height: 2;
  white-space: pre-wrap;
  color: #1a1a1a;
}

.pinyin {
  margin-top: 0.5rem;
  font-size: 0.85rem;
  line-height: 1.8;
  color: #6b7280;
  white-space: pre-wrap;
}

.notes {
  margin-top: 0.7rem;
  padding: 0.5rem 0.7rem;
  background: #fffbeb;
  border: 1px solid #fde68a;
  border-radius: 7px;
  font-size: 0.8rem;
  color: #78350f;
}

.english {
  margin-top: 0.8rem;
  padding: 0.6rem 0.8rem;
  background: #f9fafb;
  border-radius: 7px;
  font-size: 0.85rem;
  line-height: 1.6;
  white-space: pre-wrap;
}

.coverage {
  margin-top: 0.7rem;
  font-size: 0.78rem;
  color: #6b7280;
}

.coverage summary {
  cursor: pointer;
}

.covered {
  display: flex;
  flex-wrap: wrap;
  gap: 0.25rem;
  margin-top: 0.4rem;
}

.chip {
  padding: 0.1rem 0.35rem;
  border: 1px solid #e5e7eb;
  border-radius: 5px;
  font-size: 0.9rem;
}

.patterns {
  list-style: disc;
  margin: 0.4rem 0 0 1rem;
}

.player {
  width: 100%;
  margin-top: 0.7rem;
  height: 32px;
}
</style>
