import { ref } from 'vue'
import { registerSW } from 'virtual:pwa-register'

/**
 * Notices when a deploy has landed, and offers the reload rather than taking it.
 *
 * Every commit deploys, and the page is open for hours at a time — so the browser can be running
 * yesterday's bundle against today's API. That is not theoretical: a payload gained a field, the
 * cached page rendered it as raw JSON, and the exercise it was showing became unreadable. The
 * service worker was already set to update itself, but nothing ever asked it to look.
 *
 * It asks rather than reloads because a reload in the middle of a round throws the round away.
 * The banner waits; a session does not have to.
 */
export const updateReady = ref(false)

/** How often to ask whether a new build has been deployed. */
const CHECK_EVERY = 2 * 60 * 1000

let apply = null

export function watchForUpdates() {
  apply = registerSW({
    immediate: true,

    onNeedRefresh() {
      updateReady.value = true
    },

    onRegisteredSW(_url, registration) {
      if (!registration) return

      setInterval(() => {
        // Nothing to update from while offline, and the check would only log a failure
        if (navigator.onLine !== false) registration.update()
      }, CHECK_EVERY)
    },
  })
}

/** Takes the update: the worker activates and the page comes back on the new build. */
export function reloadForUpdate() {
  updateReady.value = false
  apply?.(true)
}
