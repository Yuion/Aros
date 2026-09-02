<template>
  <ul class="bars">
    <li v-for="(row, i) in rows" :key="row.key ?? i" class="bar-row">
      <span class="bar-label" :lang="row.lang">
        {{ row.label }}
        <span v-if="row.sublabel" class="sublabel">{{ row.sublabel }}</span>
      </span>

      <span class="bar-track">
        <span
          class="bar-fill"
          :style="{ width: `${width(row)}%`, background: row.color ?? '#2a78d6' }"
        />
      </span>

      <span class="bar-value">{{ row.value }}</span>
      <span v-if="row.detail" class="bar-detail">{{ row.detail }}</span>
    </li>
  </ul>
</template>

<script setup>
// rows: [{ key?, label, sublabel?, lang?, ratio (0..1), value, detail?, color? }]
// A single hue by default: these charts show one measure, and magnitude takes a
// sequential encoding rather than status colours.
const props = defineProps({
  rows: { type: Array, required: true },
  scaleToMax: Boolean,      // for counts; leave off when ratio is already 0..1
})

function width(row) {
  if (row.ratio == null) return 0

  const max = props.scaleToMax
    ? Math.max(...props.rows.map((r) => r.ratio ?? 0), Number.EPSILON)
    : 1

  const share = (row.ratio / max) * 100
  return row.ratio === 0 ? 0 : Math.max(share, 2)
}
</script>

<style scoped>
.bars {
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 0.45rem;
}

.bar-row {
  display: grid;
  grid-template-columns: minmax(0, 10rem) 1fr auto auto;
  align-items: center;
  gap: 0.6rem;
}

.bar-label {
  font-size: 0.82rem;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.bar-label:lang(zh) {
  font-size: 0.95rem;
}

.sublabel {
  color: #898781;
  font-size: 0.72rem;
  margin-left: 0.3rem;
}

.bar-track {
  height: 9px;
  background: #f0efec;
  border-radius: 4px;
  overflow: hidden;
}

.bar-fill {
  display: block;
  height: 100%;
  border-radius: 4px;
}

.bar-value {
  font-size: 0.78rem;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  min-width: 2.4rem;
  text-align: right;
}

.bar-detail {
  font-size: 0.72rem;
  color: #898781;
  font-variant-numeric: tabular-nums;
  min-width: 3.2rem;
  text-align: right;
}

@media (max-width: 560px) {
  .bar-row {
    grid-template-columns: minmax(0, 7rem) 1fr auto;
  }

  .bar-detail {
    display: none;
  }
}
</style>
