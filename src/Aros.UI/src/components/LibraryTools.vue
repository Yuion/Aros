<template>
  <div class="tools">
    <input v-model="search" class="search" type="search" :placeholder="placeholder" />

    <select v-model="filter" class="pick" aria-label="Show">
      <option v-for="option in filters" :key="option.value" :value="option.value">
        {{ option.label }}
      </option>
    </select>

    <div class="sort">
      <select v-model="sort" class="pick" aria-label="Sort by">
        <option v-for="option in SORTS" :key="option.value" :value="option.value">
          {{ option.label }}
        </option>
      </select>
      <!-- The arrow alone never says what "up" means for a date, so the words come with it -->
      <button class="sort-dir" :title="label" @click="descending = !descending">
        {{ descending ? '↓' : '↑' }} <span class="dir-label">{{ label }}</span>
      </button>
    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue'
import { SORTS, directionLabel } from '@/services/library'

defineProps({
  filters: { type: Array, required: true },
  placeholder: { type: String, default: 'Find one' },
})

const search = defineModel('search', { type: String, default: '' })
const filter = defineModel('filter', { type: String, default: 'rotation' })
const sort = defineModel('sort', { type: String, default: 'added' })
const descending = defineModel('descending', { type: Boolean, default: true })

const label = computed(() => directionLabel(sort.value, descending.value))
</script>

<style scoped>
.tools {
  display: flex;
  gap: 0.5rem;
  margin-bottom: 0.6rem;
  flex-wrap: wrap;
}

.search {
  flex: 1;
  min-width: 11rem;
  font: inherit;
  font-size: 0.85rem;
  padding: 0.4rem 0.6rem;
  border: 1px solid #e5e7eb;
  border-radius: 7px;
  background: white;
}

.pick {
  font: inherit;
  font-size: 0.8rem;
  padding: 0.4rem 0.5rem;
  border: 1px solid #e5e7eb;
  border-radius: 7px;
  background: white;
  cursor: pointer;
}

.sort {
  display: flex;
  gap: 0.35rem;
}

.sort-dir {
  font: inherit;
  font-size: 0.8rem;
  padding: 0.4rem 0.6rem;
  border: 1px solid #e5e7eb;
  border-radius: 7px;
  background: white;
  color: #4b5563;
  cursor: pointer;
  white-space: nowrap;
}

.sort-dir:hover {
  border-color: #6d5bd0;
  color: #6d5bd0;
}

@media (max-width: 560px) {
  .dir-label {
    display: none;
  }
}
</style>
