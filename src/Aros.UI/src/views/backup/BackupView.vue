<!--
  Where the data goes when this machine stops existing.

  The page does everything except put a snapshot back. That one is a command line job and says
  so below, for a reason worth stating rather than hiding: a restore stops the API and drops
  the database, and a Windows service takes its children down with it, so a restore started
  from here would kill itself halfway through.
-->
<template>
  <div class="page">
    <h1>Backup</h1>
    <p class="lead">
      GitHub rebuilds the program. It does not rebuild what it learned — the database, the audio
      that cost a synthesis a clip, or the keys. Those live here, and nowhere else, unless
      something copies them off.
    </p>

    <p v-if="error" class="error">{{ error }}</p>

    <template v-if="loaded">
      <!-- ------------------------------------------------------------ where it stands -->
      <section class="card">
        <header class="card-head">
          <h2>Where it stands</h2>
          <span class="state" :class="standing.tone">{{ standing.label }}</span>
        </header>

        <dl class="facts">
          <div>
            <dt>Last snapshot</dt>
            <dd>
              <template v-if="latest">{{ when(latest.takenAt) }}</template>
              <template v-else>never</template>
            </dd>
          </div>
          <div>
            <dt>Kept</dt>
            <dd>{{ status.snapshots.length }} snapshot{{ status.snapshots.length === 1 ? '' : 's' }}</dd>
          </div>
          <div>
            <dt>Audio here</dt>
            <dd>{{ status.local.clips }} clips · {{ size(status.local.mediaBytes) }}</dd>
          </div>
        </dl>

        <div class="actions">
          <button class="primary" :disabled="working" @click="run('run', 'Backing up')">
            {{ busyWith === 'run' ? 'Backing up…' : 'Back up now' }}
          </button>
          <button class="secondary" :disabled="working" @click="run('verify', 'Verifying')">
            {{ busyWith === 'verify' ? 'Verifying…' : 'Verify the stored data' }}
          </button>
        </div>

        <p class="aside">
          A nightly task takes one at 03:00. Verifying downloads a tenth of the stored packs and
          checks them against their hashes — a backup nobody has read back is a guess.
        </p>

        <pre v-if="output.length" class="output" :class="{ failed: !outputOk }">{{ output.join('\n') }}</pre>
      </section>

      <!-- ------------------------------------------------------------------ snapshots -->
      <section class="card">
        <h2>Snapshots</h2>

        <p v-if="!status.snapshots.length" class="aside">
          Nothing stored yet. Set the credentials below, then take one.
        </p>

        <ul v-else class="snapshots">
          <li v-for="snapshot in status.snapshots" :key="snapshot.id">
            <span class="id">{{ snapshot.id }}</span>
            <span class="taken">{{ when(snapshot.takenAt) }}</span>
            <span class="bytes">{{ size(snapshot.bytes) }}</span>
            <button class="ghost" :disabled="working" @click="fetch(snapshot)">
              {{ busyWith === snapshot.id ? 'Fetching…' : 'Fetch to look at' }}
            </button>
          </li>
        </ul>

        <p class="aside">
          Fetching unpacks a snapshot to <code>C:\Aros\restore</code> and changes nothing here.
          To actually put one back — which drops the database and replaces the audio and the
          settings — run it from a terminal, where it outlives the API it is replacing:
        </p>
        <pre class="command">.\Scripts\restore.ps1 -Snapshot {{ named }} -Apply -Force</pre>
      </section>

      <!-- ---------------------------------------------------------------- credentials -->
      <section class="card">
        <h2>Credentials</h2>
        <p class="aside">
          Stored in <code>C:\Aros\backup.env.ps1</code>, outside the repository and outside the
          deploy folder, so neither a commit nor a redeploy can carry them anywhere.
        </p>

        <form class="form" @submit.prevent="save">
          <label>
            <span>Repository</span>
            <input v-model="form.repository" placeholder="s3:s3.eu-central-003.backblazeb2.com/bucket/aros" />
            <small>Backend, endpoint, bucket, folder. No https:// in front.</small>
          </label>

          <label>
            <span>Key ID</span>
            <input v-model="form.keyId" placeholder="003bc5a2ed36c54…" />
            <small>The first half of the Backblaze application key.</small>
          </label>

          <label>
            <span>Application key</span>
            <input v-model="form.applicationKey" type="password" :placeholder="kept(settings.applicationKeySet)" />
            <small>Backblaze shows it once. Leave blank to keep the stored one.</small>
          </label>

          <label>
            <span>Passphrase</span>
            <input v-model="form.passphrase" type="password" :placeholder="kept(settings.passphraseSet)" />
            <small>
              What encrypts every snapshot, before anything is uploaded. The key above can be
              reissued from Backblaze; this cannot be reissued by anyone. Leave blank to keep it.
            </small>
          </label>

          <div class="actions">
            <button class="primary" type="submit" :disabled="working || !form.repository">
              {{ saving ? 'Saving…' : 'Save' }}
            </button>
            <span v-if="saved" class="saved">Saved.</span>
          </div>
        </form>

        <p class="warn">
          Neither secret is ever sent back to this page, which is why the boxes look empty even
          when both are set. Keep the passphrase somewhere that is not this computer — a backup
          nobody can decrypt fails in exactly the situation it exists for.
        </p>
      </section>
    </template>

    <p v-else-if="!error" class="aside">Loading…</p>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { api } from '@/services/api'

const status = ref(null)
const loaded = computed(() => status.value !== null)
const settings = computed(() => status.value?.settings ?? {})
const latest = computed(() => status.value?.snapshots?.[0] ?? null)

const error = ref('')
const busyWith = ref('')
const saving = ref(false)
const saved = ref(false)
const working = computed(() => busyWith.value !== '' || saving.value)

const output = ref([])
const outputOk = ref(true)

/** The command below names whichever snapshot you last looked at, not always the newest. */
const fetched = ref('')
const named = computed(() => fetched.value || latest.value?.id || 'latest')

const form = reactive({ repository: '', keyId: '', passphrase: '', applicationKey: '' })

onMounted(load)

async function load() {
  try {
    status.value = await api.get('/backup/status')
    form.repository = settings.value.repository ?? ''
    form.keyId = settings.value.keyId ?? ''
  } catch (e) {
    error.value = e.message
  }
}

/** Set, but not shown: the box says so rather than pretending to be empty. */
function kept(isSet) {
  return isSet ? 'stored — leave blank to keep it' : ''
}

const standing = computed(() => {
  if (!settings.value.configured) return { label: 'not set up', tone: 'bad' }
  if (!latest.value) return { label: 'nothing stored yet', tone: 'bad' }

  const age = (Date.now() - new Date(latest.value.takenAt)) / 86400000
  if (age > 3) return { label: `${Math.floor(age)} days behind`, tone: 'bad' }
  if (age > 1.5) return { label: 'a day or two behind', tone: 'warn' }
  return { label: 'up to date', tone: 'good' }
})

async function run(what, label) {
  busyWith.value = what
  output.value = [`${label}…`]
  error.value = ''

  try {
    const result = await api.post(`/backup/${what}`)
    outputOk.value = result.ok
    output.value = result.output.length ? result.output : [result.ok ? 'Done.' : 'Failed.']
    status.value = await api.get('/backup/status')
  } catch (e) {
    error.value = e.message
    output.value = []
  } finally {
    busyWith.value = ''
  }
}

async function fetch(snapshot) {
  busyWith.value = snapshot.id
  output.value = [`Fetching ${snapshot.id}…`]
  error.value = ''

  try {
    const result = await api.post(`/backup/fetch/${snapshot.id}`)
    outputOk.value = result.ok
    output.value = result.output
    if (result.ok) fetched.value = snapshot.id
  } catch (e) {
    error.value = e.message
    output.value = []
  } finally {
    busyWith.value = ''
  }
}

async function save() {
  saving.value = true
  saved.value = false
  error.value = ''

  try {
    await api.put('/backup/credentials', {
      repository: form.repository,
      keyId: form.keyId,
      passphrase: form.passphrase,
      applicationKey: form.applicationKey,
    })

    // Not kept a moment longer than the request needs them
    form.passphrase = ''
    form.applicationKey = ''

    saved.value = true
    status.value = await api.get('/backup/status')
  } catch (e) {
    error.value = e.message
  } finally {
    saving.value = false
  }
}

function when(value) {
  const date = new Date(value)
  return date.toLocaleString('de-DE', {
    day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit',
  })
}

function size(bytes) {
  if (!bytes) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB']
  let value = bytes
  let unit = 0
  while (value >= 1024 && unit < units.length - 1) { value /= 1024; unit += 1 }
  return `${value.toFixed(unit === 0 ? 0 : 1)} ${units[unit]}`
}
</script>

<style scoped>
.page {
  max-width: 820px;
  padding: 1.5rem;
}

h1 {
  margin: 0 0 0.35rem;
  font-size: 1.6rem;
}

.lead {
  margin: 0 0 1.5rem;
  color: #6b7280;
  font-size: 0.88rem;
  line-height: 1.55;
}

.card {
  background: white;
  border: 1px solid #f0efec;
  border-radius: 10px;
  padding: 1.15rem 1.25rem;
  margin-bottom: 1.1rem;
}

.card h2 {
  margin: 0 0 0.9rem;
  font-size: 1rem;
}

.card-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 0.9rem;
}

.card-head h2 {
  margin: 0;
}

.state {
  font-size: 0.72rem;
  padding: 0.2rem 0.55rem;
  border-radius: 999px;
}

.state.good { color: #15803d; background: #f0fdf4; }
.state.warn { color: #b45309; background: #fffbeb; }
.state.bad  { color: #b91c1c; background: #fef2f2; }

.facts {
  display: flex;
  flex-wrap: wrap;
  gap: 1.75rem;
  margin: 0 0 1rem;
}

.facts dt {
  font-size: 0.7rem;
  color: #9ca3af;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.facts dd {
  margin: 0.15rem 0 0;
  font-size: 0.9rem;
}

.actions {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  flex-wrap: wrap;
}

/* The house kit is declared per view, so it is declared here too */
.primary,
.secondary,
.ghost {
  font: inherit;
  font-size: 0.82rem;
  padding: 0.45rem 0.9rem;
  border-radius: 8px;
  cursor: pointer;
  border: 1px solid transparent;
}

.primary {
  background: #6d5bd0;
  border-color: #6d5bd0;
  color: white;
}

.primary:hover:not(:disabled) { background: #5c4bbd; }

.secondary {
  background: white;
  border-color: #ddd6fe;
  color: #6d5bd0;
}

.secondary:hover:not(:disabled) { border-color: #6d5bd0; }

.ghost {
  background: white;
  border-color: #e5e7eb;
  color: #4b5563;
}

.ghost:hover:not(:disabled) { border-color: #6b7280; }

.primary:disabled,
.secondary:disabled,
.ghost:disabled {
  opacity: 0.55;
  cursor: default;
}

.aside {
  margin: 0.9rem 0 0;
  font-size: 0.76rem;
  color: #9ca3af;
  line-height: 1.55;
}

.aside code,
.command {
  font-family: ui-monospace, Consolas, monospace;
}

.command {
  margin: 0.5rem 0 0;
  padding: 0.6rem 0.75rem;
  background: #1e1e2e;
  color: #cdd6f4;
  border-radius: 8px;
  font-size: 0.76rem;
  overflow-x: auto;
}

.output {
  margin: 0.9rem 0 0;
  padding: 0.7rem 0.8rem;
  max-height: 16rem;
  overflow: auto;
  background: #fafafa;
  border: 1px solid #f0efec;
  border-radius: 8px;
  font-family: ui-monospace, Consolas, monospace;
  font-size: 0.72rem;
  line-height: 1.5;
  white-space: pre-wrap;
}

.output.failed {
  background: #fef2f2;
  border-color: #fecaca;
}

.snapshots {
  list-style: none;
  margin: 0;
  padding: 0;
}

.snapshots li {
  display: grid;
  grid-template-columns: 5.5rem 1fr auto auto;
  align-items: center;
  gap: 0.75rem;
  padding: 0.5rem 0;
  border-bottom: 1px solid #f6f5f3;
  font-size: 0.82rem;
}

.snapshots li:last-child { border-bottom: none; }

.id {
  font-family: ui-monospace, Consolas, monospace;
  color: #6b7280;
}

.bytes { color: #9ca3af; }

.form {
  display: flex;
  flex-direction: column;
  gap: 0.9rem;
}

.form label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.form label > span {
  font-size: 0.78rem;
  font-weight: 600;
}

.form input {
  font: inherit;
  font-size: 0.85rem;
  padding: 0.45rem 0.6rem;
  border: 1px solid #e5e7eb;
  border-radius: 8px;
}

.form input:focus {
  outline: none;
  border-color: #6d5bd0;
}

.form small {
  font-size: 0.72rem;
  color: #9ca3af;
  line-height: 1.5;
}

.saved {
  font-size: 0.76rem;
  color: #15803d;
}

.warn {
  margin: 1rem 0 0;
  padding: 0.65rem 0.8rem;
  background: #fffbeb;
  border: 1px solid #fde68a;
  border-radius: 8px;
  font-size: 0.76rem;
  color: #92400e;
  line-height: 1.55;
}

.error {
  margin: 0 0 1rem;
  padding: 0.6rem 0.8rem;
  background: #fef2f2;
  border: 1px solid #fecaca;
  border-radius: 8px;
  font-size: 0.8rem;
  color: #b91c1c;
}
</style>
