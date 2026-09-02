<template>
  <div class="tile">
    <span class="tile-label">{{ label }}</span>
    <span class="tile-value" :class="{ small }">
      {{ value }}<span v-if="of" class="tile-of">/{{ of }}</span>
    </span>
    <span class="tile-note">{{ note }}</span>
  </div>
</template>

<script setup>
defineProps({
  label: { type: String, required: true },
  value: { type: [String, Number], required: true },
  of: { type: [String, Number], default: null },
  note: { type: String, default: '' },
  small: Boolean,
})
</script>

<style scoped>
.tile {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  padding: 0.9rem 1rem;
  background: white;
  border: 1px solid #e5e7eb;
  border-radius: 10px;
}

.tile-label {
  font-size: 0.7rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: #898781;
}

.tile-value {
  font-size: 1.9rem;
  font-weight: 700;
  line-height: 1.15;
  color: #0b0b0b;
}

.tile-value.small {
  font-size: 1.25rem;
  padding-top: 0.35rem;
}

.tile-of {
  font-size: 1rem;
  font-weight: 600;
  color: #898781;
}

.tile-note {
  font-size: 0.72rem;
  color: #52514e;
}
</style>
