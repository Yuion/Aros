<template>
  <div>
    <ul class="legend">
      <li v-for="band in BANDS" :key="band.key">
        <span class="swatch" :style="{ background: band.color }" />{{ band.label }}
      </li>
    </ul>

    <ul class="bars">
      <li v-for="row in rows" :key="row.key" class="bar-row">
        <span class="bar-label" :lang="row.lang">{{ row.label }}</span>

        <span class="bar-track" :class="{ empty: !total(row) }">
          <span
            v-for="band in BANDS"
            :key="band.key"
            class="segment"
            :style="{ width: `${share(row, band.key)}%`, background: band.color }"
            :title="`${row[band.key]} ${band.label}`"
          />
        </span>

        <span class="bar-value">
          <span class="open">{{ row.open }}</span>
          <span class="slash">/</span>
          <span class="resting">{{ row.resting }}</span>
          <span class="slash">/</span>
          <span class="mastered">{{ row.mastered }}</span>
        </span>
      </li>
    </ul>
  </div>
</template>

<script setup>
// rows: [{ key, label, lang?, open, resting, mastered }]
// Three states of one pool, so the bar is stacked rather than three bars: what the
// segments show is how the whole splits, and the widths have to add up for that to read.
const BANDS = [
  { key: 'open', label: 'open', color: '#2a78d6' },
  { key: 'resting', label: 'resting', color: '#9dc3ef' },
  { key: 'mastered', label: 'mastered', color: '#16457c' },
]

defineProps({
  rows: { type: Array, required: true },
})

function total(row) {
  return row.open + row.resting + row.mastered
}

function share(row, key) {
  const sum = total(row)
  return sum === 0 ? 0 : (row[key] / sum) * 100
}
</script>

<style scoped>
.legend {
  list-style: none;
  display: flex;
  gap: 0.9rem;
  margin-bottom: 0.6rem;
  font-size: 0.72rem;
  color: #6b7280;
}

.legend li {
  display: flex;
  align-items: center;
  gap: 0.3rem;
}

.swatch {
  width: 0.65rem;
  height: 0.65rem;
  border-radius: 2px;
}

.bars {
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 0.45rem;
}

.bar-row {
  display: grid;
  grid-template-columns: minmax(0, 10rem) 1fr auto;
  align-items: center;
  gap: 0.6rem;
}

.bar-label {
  font-size: 0.82rem;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.bar-track {
  display: flex;
  height: 0.6rem;
  border-radius: 999px;
  overflow: hidden;
  background: #f3f4f6;
}

.bar-track.empty {
  background: repeating-linear-gradient(
    45deg,
    #f3f4f6,
    #f3f4f6 4px,
    #e5e7eb 4px,
    #e5e7eb 8px
  );
}

.segment {
  height: 100%;
}

.bar-value {
  font-size: 0.78rem;
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}

.open {
  color: #2a78d6;
  font-weight: 600;
}

.resting {
  color: #6b7280;
}

.mastered {
  color: #16457c;
  font-weight: 600;
}

.slash {
  color: #d1d5db;
  margin: 0 0.15rem;
}
</style>
