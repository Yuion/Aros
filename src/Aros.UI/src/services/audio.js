/**
 * Clips, fetched whole before anything plays them.
 *
 * Setting `src` and calling `play()` in the same breath starts playback on whatever frames have
 * arrived, so a slow first response is heard as a clipped syllable — and swapping `src` while an
 * earlier `play()` is still pending aborts that load mid-word, which sounds like the same bug. A
 * blob is complete before playback begins, so neither can happen; the next clip is fetched while
 * the current one is still on screen, which is where the wait belongs.
 */
const cache = new Map()

/** The blob URL for a clip, fetching it once and reusing it afterwards. */
export function clip(url) {
  if (!url) return Promise.resolve(null)
  if (!cache.has(url)) cache.set(url, load(url))

  return cache.get(url)
}

async function load(url) {
  try {
    const res = await fetch(url)
    if (!res.ok) throw new Error(`${res.status} ${res.statusText}`)

    return URL.createObjectURL(await res.blob())
  } catch (e) {
    // A failed fetch must not be remembered as a failure: the next play should try again
    cache.delete(url)
    throw e
  }
}

/** Warms a clip that will be wanted shortly. Failure here is silent — playing it will report it. */
export function prefetch(url) {
  clip(url).catch(() => {})
}

/** Drops every blob this page made. Called when the view goes away, or they leak until reload. */
export function release() {
  for (const pending of cache.values()) pending.then(URL.revokeObjectURL).catch(() => {})
  cache.clear()
}
