import { enqueue, getPendingActions, markSynced } from './db'
import { api } from './api'

/**
 * Queue an action for sync.
 * If online, attempt immediate sync. If offline, store locally.
 *
 * Usage:
 *   await syncAction('test_result', { vocabId: 1, correct: true })
 */
export async function syncAction(type, payload) {
  await enqueue(type, payload)

  if (navigator.onLine) {
    await flushQueue()
  }
}

/**
 * Push all pending actions to the API.
 * Called automatically when coming back online.
 */
export async function flushQueue() {
  const pending = await getPendingActions()
  if (pending.length === 0) return

  try {
    const result = await api.post('/sync', { actions: pending })
    await markSynced(result.syncedIds)
    console.log(`[sync] Synced ${result.syncedIds.length} action(s)`)
  } catch (err) {
    console.warn('[sync] Flush failed, will retry when online:', err.message)
  }
}

/**
 * Register the online event listener — call once at app startup.
 */
export function registerSyncOnReconnect() {
  window.addEventListener('online', () => {
    console.log('[sync] Back online — flushing queue...')
    flushQueue()
  })
}
