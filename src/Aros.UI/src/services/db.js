import { openDB } from 'idb'

const DB_NAME = 'aros'
const DB_VERSION = 1

let _db = null

async function getDb() {
  if (_db) return _db
  _db = await openDB(DB_NAME, DB_VERSION, {
    upgrade(db) {
      // Queue of actions to sync to the server
      if (!db.objectStoreNames.contains('sync_queue')) {
        const queue = db.createObjectStore('sync_queue', { keyPath: 'id', autoIncrement: true })
        queue.createIndex('synced', 'synced')
      }

      // Local cache of server data (keyed by store name + id)
      if (!db.objectStoreNames.contains('cache')) {
        db.createObjectStore('cache', { keyPath: 'key' })
      }
    },
  })
  return _db
}

// Sync queue — store an action to be sent to the API later
export async function enqueue(type, payload) {
  const db = await getDb()
  await db.add('sync_queue', {
    type,
    payload,
    timestamp: new Date().toISOString(),
    synced: false,
  })
}

export async function getPendingActions() {
  const db = await getDb()
  return db.getAllFromIndex('sync_queue', 'synced', IDBKeyRange.only(false))
}

export async function markSynced(ids) {
  const db = await getDb()
  const tx = db.transaction('sync_queue', 'readwrite')
  await Promise.all(ids.map(id => tx.store.delete(id)))
  await tx.done
}

// Cache — store/retrieve data locally
export async function cacheSet(key, value) {
  const db = await getDb()
  await db.put('cache', { key, value, updatedAt: new Date().toISOString() })
}

export async function cacheGet(key) {
  const db = await getDb()
  const entry = await db.get('cache', key)
  return entry?.value ?? null
}
