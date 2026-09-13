/**
 * Searching, filtering and ordering for the two libraries — the sentence list and the word list.
 * They hold different things but answer the same questions: where is that one, what is still being
 * asked, and what have I finished with. One set of rules keeps them honest about the words they
 * use, since "mastered" must not mean two things across two pages.
 *
 * Items arrive with the state the API worked out: ready, resting, mastered, retired, unavailable.
 */

export const FILTERS = [
  // The default. What the trainer will still ask you, which is what you are usually looking at
  { value: 'rotation', label: 'In rotation' },
  { value: 'ready', label: 'Ready now' },
  { value: 'resting', label: 'Resting' },
  { value: 'needsWork', label: 'Needs work' },
  { value: 'done', label: 'Finished with' },
  { value: 'all', label: 'Everything' },
]

/** The gap filter differs per list: a word can be missing audio, a sentence a reading. */
export const GAP_FILTERS = {
  vocab: { value: 'gaps', label: 'No audio yet' },
  clips: { value: 'gaps', label: 'Missing readings' },
}

export const SORTS = [
  { value: 'added', label: 'Added', labels: ['oldest first', 'newest first'] },
  { value: 'alphabetical', label: 'Pinyin A–Z', labels: ['A to Z', 'Z to A'] },
  { value: 'practice', label: 'Times practised', labels: ['least practised', 'most practised'] },
]

function done(item) {
  return item.state === 'mastered' || item.state === 'retired'
}

export function matches(item, filter) {
  switch (filter) {
    case 'all':
      return true
    case 'rotation':
      return !done(item)
    case 'ready':
      return item.state === 'ready'
    case 'resting':
      return item.state === 'resting'
    case 'done':
      return done(item)
    // Missed at least once and still being asked: finishing something does not make it a worry
    case 'needsWork':
      return item.wrong > 0 && !done(item)
    case 'gaps':
      return item.hasAudio === false || item.pinyin === '' || item.english === ''
    default:
      return true
  }
}

/**
 * Chinese has no useful alphabetical order of its own, so "A–Z" sorts on the pinyin, and an entry
 * without one sorts on its characters rather than collecting at one end.
 */
const COMPARE = {
  added: (a, b) => new Date(a.createdAt) - new Date(b.createdAt),
  alphabetical: (a, b) => reading(a).localeCompare(reading(b)),
  practice: (a, b) => a.correct + a.wrong - (b.correct + b.wrong),
}

function reading(item) {
  return item.pinyin || item.sentence || item.characters || ''
}

export function arrange(items, { search = '', filter = 'rotation', sort = 'added', descending = true } = {}) {
  const needle = search.trim().toLowerCase()

  const kept = items.filter(
    (item) =>
      matches(item, filter) &&
      (!needle ||
        [item.sentence, item.characters, item.pinyin, item.english].some((field) =>
          (field ?? '').toLowerCase().includes(needle)
        ))
  )

  kept.sort(COMPARE[sort] ?? COMPARE.added)
  if (descending) kept.reverse()

  return kept
}

export function directionLabel(sort, descending) {
  const found = SORTS.find((s) => s.value === sort) ?? SORTS[0]
  return found.labels[descending ? 1 : 0]
}
