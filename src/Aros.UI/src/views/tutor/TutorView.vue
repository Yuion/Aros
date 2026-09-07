<template>
  <div class="tutor">
    <header class="head">
      <div>
        <h1>Tutor</h1>
        <p class="subtitle">
          <template v-if="state">
            {{ state.model || 'no model configured' }}
            <span v-if="state.budget" class="budget" :class="{ tight: budgetTight }">
              · {{ state.budget.used.toLocaleString() }} / {{ state.budget.limit.toLocaleString() }} tokens today
            </span>
          </template>
        </p>
      </div>

      <div class="head-actions">
        <button class="ghost" title="Show what the tutor is told about you" @click="showContext">Context</button>
        <button class="ghost" :disabled="busy" @click="courseFile?.click()">Import course file</button>
        <input ref="courseFile" type="file" accept=".json,application/json" hidden @change="importCourse" />
        <button class="ghost" :disabled="busy || !messages.length" @click="endLesson">
          {{ ending ? 'Writing it up…' : 'End lesson' }}
        </button>
        <button class="ghost" :disabled="busy" @click="newConversation">New thread</button>
      </div>
    </header>

    <p v-if="state && !state.configured" class="notice">{{ state.problem }}</p>
    <p v-if="error" class="notice error">{{ error }}</p>

    <section v-if="context" class="card context">
      <header class="card-head">
        <h2>What the tutor is told</h2>
        <button class="ghost" @click="context = null">Close</button>
      </header>
      <p class="card-note">
        {{ context.characters.toLocaleString() }} characters, roughly
        {{ context.roughTokens.toLocaleString() }} tokens, sent with every message.
      </p>

      <!-- Written by hand, and the only half worth editing -->
      <h3 class="part-head">
        Standing instructions
        <span v-if="!context.isDefault" class="edited">edited</span>
      </h3>
      <p class="card-note">
        How the tutor should teach. Yours to change; saved to the database, so it takes effect on
        the next message with no restart.
      </p>
      <textarea v-model="standing" class="context-edit" rows="14" spellcheck="false" />
      <div class="proposal-actions">
        <button class="primary" :disabled="busy || standing === context.standing" @click="saveInstructions">
          Save instructions
        </button>
        <button class="ghost" :disabled="busy" @click="resetInstructions">Restore the built-in text</button>
      </div>

      <!-- Assembled from the database; typing here would be typing on a mirror -->
      <h3 class="part-head">Learning state — read from your database</h3>
      <p class="card-note">
        Vocabulary and accuracy come from the trainers. Grammar, rules, weak points, lessons and
        preferences come from the course file and from approved lesson write-ups. Change those at
        the source, not here.
      </p>
      <pre class="context-body">{{ context.state }}</pre>
    </section>

    <!-- Written by the tutor, applied only when you say so -->
    <section v-for="proposal in proposals" :key="proposal.id" class="card proposal">
      <header class="card-head">
        <h2>Save this lesson?</h2>
        <span class="card-note">{{ proposal.summary }}</span>
      </header>

      <pre class="proposal-body">{{ pretty(proposal.payload) }}</pre>

      <div class="proposal-actions">
        <button class="primary" :disabled="busy" @click="applyProposal(proposal)">Save to the course</button>
        <button class="ghost" :disabled="busy" @click="rejectProposal(proposal)">Discard</button>
      </div>
    </section>

    <div ref="scroller" class="thread">
      <p v-if="!messages.length && !streaming" class="placeholder">
        Nothing yet. The tutor knows what is in your trainers — ask it to carry on the course.
      </p>

      <article v-for="message in messages" :key="message.id" class="turn" :class="message.role">
        <div v-if="message.role === 'user'" class="bubble user" :class="{ failed: message.failed }">
          {{ message.content }}
          <p v-if="message.failed" class="turn-error">{{ message.error }}</p>
        </div>

        <div v-else class="bubble assistant">
          <div class="md" v-html="render(message.content)" />

          <!-- Tables the importers can read, offered rather than applied -->
          <div v-if="tablesIn(message).length" class="imports">
            <button
              v-for="(table, i) in tablesIn(message)"
              :key="i"
              class="import-btn"
              :disabled="busy"
              @click="importTable(table)"
            >
              Import {{ table.rows }} {{ table.looksLikeSentences ? 'sentences' : 'words' }}
            </button>
          </div>

          <p v-if="message.outputTokens" class="turn-meta">
            {{ message.inputTokens.toLocaleString() }} in · {{ message.outputTokens.toLocaleString() }} out
            · {{ (message.latencyMs / 1000).toFixed(1) }}s
          </p>
        </div>
      </article>

      <article v-if="streaming" class="turn assistant">
        <div class="bubble assistant">
          <div class="md" v-html="render(streaming)" />
          <span class="cursor" />
        </div>
      </article>
    </div>

    <p v-if="importReport" class="notice done">{{ importReport }}</p>

    <form class="composer" @submit.prevent="send">
      <textarea
        ref="field"
        v-model="text"
        rows="3"
        placeholder="Ask for the next lesson, or answer the last exercise…"
        :disabled="busy"
        @keydown.ctrl.enter="send"
      />
      <div class="composer-row">
        <button v-if="!busy" type="submit" class="primary" :disabled="!text.trim()">Send</button>
        <button v-else type="button" class="primary stop" @click="cancel">Stop</button>
        <span class="hint">Ctrl+Enter</span>
        <button v-if="lastFailed" type="button" class="ghost" @click="retry">Retry</button>
      </div>
    </form>
  </div>
</template>

<script setup>
import { computed, nextTick, onMounted, onUnmounted, ref } from 'vue'
import { api } from '@/services/api'
import { render, findTables } from '@/services/markdown'

const state = ref(null)
const messages = ref([])
const text = ref('')
const streaming = ref('')
const busy = ref(false)
const error = ref('')
const context = ref(null)
const importReport = ref('')
const scroller = ref(null)
const field = ref(null)
const courseFile = ref(null)
const proposals = ref([])
const ending = ref(false)
const standing = ref('')

let controller = null

const budgetTight = computed(() => {
  const b = state.value?.budget
  return b ? b.left < b.limit * 0.1 : false
})

const lastFailed = computed(() => {
  const last = messages.value[messages.value.length - 1]
  return last?.role === 'user' && last.failed
})

async function load() {
  try {
    const [next, pending] = await Promise.all([api.get('/tutor'), api.get('/tutor/proposals')])
    state.value = next
    messages.value = next.messages
    proposals.value = pending
    await toBottom()
  } catch (e) {
    error.value = e.message
  }
}

async function toBottom() {
  await nextTick()
  if (scroller.value) scroller.value.scrollTop = scroller.value.scrollHeight
}

async function send() {
  const message = text.value.trim()
  if (!message || busy.value) return

  text.value = ''
  await ask(message)
}

async function retry() {
  const last = messages.value[messages.value.length - 1]
  if (!last?.failed) return

  messages.value.pop()
  await ask(last.content)
}

/**
 * Streamed over server-sent events. Read with fetch rather than EventSource, which only does GET —
 * the message has to go in a body.
 */
async function ask(message) {
  busy.value = true
  error.value = ''
  importReport.value = ''
  streaming.value = ''
  controller = new AbortController()

  try {
    const response = await fetch(`${api.base}/tutor/stream`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ text: message }),
      signal: controller.signal,
    })

    if (!response.ok || !response.body) throw new Error(`The tutor could not be reached (${response.status}).`)

    const reader = response.body.getReader()
    const decoder = new TextDecoder()
    let buffer = ''

    for (;;) {
      const { done, value } = await reader.read()
      if (done) break

      buffer += decoder.decode(value, { stream: true })

      // Events are separated by a blank line; anything after the last one is a partial event
      const events = buffer.split('\n\n')
      buffer = events.pop() ?? ''

      for (const event of events) handle(event)
      await toBottom()
    }
  } catch (e) {
    if (e.name !== 'AbortError') error.value = e.message
  } finally {
    controller = null
    busy.value = false
    streaming.value = ''
    await load()
    field.value?.focus()
  }
}

function handle(raw) {
  const type = raw.match(/^event: (.*)$/m)?.[1]
  const data = raw.match(/^data: (.*)$/m)?.[1]
  if (!type || !data) return

  let payload
  try {
    payload = JSON.parse(data)
  } catch {
    return
  }

  if (type === 'question') messages.value.push(payload)
  else if (type === 'delta') streaming.value += payload.text
  else if (type === 'error') error.value = payload.message
  else if (type === 'done' && payload.error) error.value = payload.error
}

function cancel() {
  controller?.abort()
}

function tablesIn(message) {
  return findTables(message.content)
}

/**
 * Sends the table to whichever importer fits. Vocabulary goes in flagged for review: a plausible
 * wrong tone is exactly what a model gets wrong, and the queue exists to catch it. Sentences cost
 * a synthesis each, so those are previewed and confirmed before anything is spent.
 */
async function importTable(table) {
  busy.value = true
  error.value = ''
  importReport.value = ''

  try {
    if (table.looksLikeSentences) {
      const preview = await api.post('/tts/import/preview', { text: table.text })

      if (!preview.parsed) throw new Error('No sentence could be read from that table.')
      if (
        !window.confirm(
          `${preview.parsed} sentences — ${preview.newSentences} new, one synthesis each. Import?`,
        )
      ) {
        return
      }

      const result = await api.post('/tts/import', { text: table.text })
      importReport.value = `${result.added} synthesized, ${result.reused} already held.`
    } else {
      const result = await api.post('/vocab/import?review=true', { text: table.text })
      const conflicts = result.conflicts.length ? `, ${result.conflicts.length} conflicting` : ''
      importReport.value =
        `${result.added} new, ${result.updated} updated${conflicts} — waiting in review on the vocabulary page.`
    }
  } catch (e) {
    error.value = e.message
  } finally {
    busy.value = false
  }
}

/**
 * The one-time import of the old course: grammar, sandhi rules, weak points, lessons and where
 * things stood. It does not carry vocabulary or sentences — those are already here, with their
 * practice history, and a file must not overwrite them.
 */
async function importCourse(event) {
  const file = event.target.files?.[0]
  event.target.value = ''
  if (!file) return

  busy.value = true
  error.value = ''
  importReport.value = ''

  try {
    const result = await api.post('/tutor/course/import', { json: await file.text() })

    importReport.value =
      `Imported ${result.grammar} grammar points, ${result.rules} pronunciation rules, ` +
      `${result.weakPoints} weak points, ${result.lessons} lessons.` +
      (result.notes?.length ? ' ' + result.notes.join(' ') : '')

    await load()
  } catch (e) {
    error.value = e.message
  } finally {
    busy.value = false
  }
}

/**
 * Asks the tutor to write up the lesson just finished. One extra call rather than a tool it might
 * choose to invoke, so it always happens; the reply is treated as data and parked for approval
 * rather than acted on.
 */
async function endLesson() {
  if (busy.value) return

  busy.value = true
  ending.value = true
  error.value = ''
  importReport.value = ''

  try {
    await api.post('/tutor/lesson/end')
    await load()
  } catch (e) {
    error.value = e.message
  } finally {
    busy.value = false
    ending.value = false
  }
}

async function applyProposal(proposal) {
  busy.value = true
  error.value = ''

  try {
    const r = await api.post(`/tutor/proposals/${proposal.id}/apply`)
    const bits = [
      `lesson ${r.lessons ? 'recorded' : 'unchanged'}`,
      r.grammar ? `${r.grammar} grammar` : null,
      r.rules ? `${r.rules} pronunciation rules` : null,
      r.weakPoints ? `${r.weakPoints} weak points` : null,
      r.resolved ? `${r.resolved} resolved` : null,
    ].filter(Boolean)

    importReport.value = 'Saved: ' + bits.join(', ') + '.'
    await load()
  } catch (e) {
    error.value = e.message
  } finally {
    busy.value = false
  }
}

async function rejectProposal(proposal) {
  if (!window.confirm('Discard this write-up? The lesson itself stays in the chat.')) return

  busy.value = true

  try {
    await api.post(`/tutor/proposals/${proposal.id}/reject`)
    await load()
  } catch (e) {
    error.value = e.message
  } finally {
    busy.value = false
  }
}

/** The payload arrives as a JSON string; shown indented so it can actually be read. */
function pretty(payload) {
  try {
    return JSON.stringify(JSON.parse(payload), null, 2)
  } catch {
    return payload
  }
}

/**
 * Clearing is only ever about the conversation. The course state lives in its own tables and is
 * written by approving a write-up, never by chatting — so the warning has to say that a lesson
 * nobody ended is a lesson nobody recorded.
 */
async function newConversation() {
  const unsaved = messages.value.length > 0

  const warning = unsaved
    ? [
        'Clear the conversation?',
        '',
        'This conversation is NOT saved to your course. If it was a lesson, press "End lesson"',
        'first and approve the write-up — otherwise nothing from it is recorded.',
        '',
        'Untouched either way: your vocabulary, sentences, every trainer score, and the lessons,',
        'grammar and weak points already recorded.',
      ].join(' ')
    : 'Start a fresh thread?'

  if (!window.confirm(warning)) return

  busy.value = true

  try {
    const r = await api.post('/tutor/conversation/new')
    importReport.value = r.cleared ? `Cleared ${r.cleared} messages.` : 'Fresh thread.'
    await load()
  } catch (e) {
    error.value = e.message
  } finally {
    busy.value = false
  }
}

async function showContext() {
  if (context.value) {
    context.value = null
    return
  }

  try {
    context.value = await api.get('/tutor/context')
    standing.value = context.value.standing
  } catch (e) {
    error.value = e.message
  }
}

async function saveInstructions() {
  busy.value = true
  error.value = ''

  try {
    const r = await api.put('/tutor/instructions', { text: standing.value })
    context.value = { ...context.value, standing: r.standing, isDefault: r.isDefault }
    standing.value = r.standing
    importReport.value = 'Instructions saved. They apply from your next message.'
  } catch (e) {
    error.value = e.message
  } finally {
    busy.value = false
  }
}

async function resetInstructions() {
  if (!window.confirm('Replace your instructions with the built-in text? Your edits are lost.')) return

  busy.value = true

  try {
    // An empty body is the reset: the server owns what "default" means
    const r = await api.put('/tutor/instructions', { text: '' })
    context.value = { ...context.value, standing: r.standing, isDefault: r.isDefault }
    standing.value = r.standing
    importReport.value = 'Restored the built-in instructions.'
  } catch (e) {
    error.value = e.message
  } finally {
    busy.value = false
  }
}

onMounted(load)
onUnmounted(() => controller?.abort())
</script>

<style scoped>
.tutor {
  max-width: 780px;
  margin: 0 auto;
  display: flex;
  flex-direction: column;
  gap: 1rem;
  height: calc(100vh - 7rem);
}

.head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
}

h1 {
  font-size: 1.5rem;
  font-weight: 700;
}

.subtitle {
  color: #6b7280;
  font-size: 0.82rem;
  margin-top: 0.2rem;
}

.budget.tight {
  color: #b45309;
  font-weight: 600;
}

.head-actions {
  display: flex;
  gap: 0.4rem;
}

.ghost {
  padding: 0.35rem 0.7rem;
  font-family: inherit;
  font-size: 0.78rem;
  font-weight: 600;
  color: #6b7280;
  background: white;
  border: 1px solid #e5e7eb;
  border-radius: 7px;
  cursor: pointer;
}

.ghost:hover:not(:disabled) {
  border-color: #cba6f7;
  color: #4b5563;
}

.notice {
  padding: 0.6rem 0.8rem;
  font-size: 0.85rem;
  background: #fffbeb;
  border: 1px solid #fde68a;
  border-radius: 8px;
  color: #92400e;
}

.notice.error {
  background: #fef2f2;
  border-color: #fecaca;
  color: #b91c1c;
}

.notice.done {
  background: #f0fdf4;
  border-color: #bbf7d0;
  color: #15803d;
}

.thread {
  flex: 1;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 0.9rem;
  padding-right: 0.3rem;
}

.placeholder {
  color: #9ca3af;
  font-size: 0.88rem;
  text-align: center;
  padding: 3rem 1rem;
}

.turn {
  display: flex;
}

.turn.user {
  justify-content: flex-end;
}

.bubble {
  max-width: 88%;
  padding: 0.7rem 0.9rem;
  border-radius: 12px;
  font-size: 0.92rem;
  line-height: 1.6;
}

.bubble.user {
  background: #6d5bd0;
  color: white;
  white-space: pre-wrap;
}

.bubble.user.failed {
  background: #b91c1c;
}

.bubble.assistant {
  background: white;
  border: 1px solid #eceaf5;
  max-width: 100%;
  width: 100%;
}

.turn-error {
  margin-top: 0.4rem;
  font-size: 0.78rem;
  opacity: 0.9;
}

.turn-meta {
  margin-top: 0.6rem;
  font-size: 0.72rem;
  color: #9ca3af;
}

.imports {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
  margin-top: 0.7rem;
}

.import-btn {
  padding: 0.35rem 0.75rem;
  font-family: inherit;
  font-size: 0.78rem;
  font-weight: 600;
  color: #6d5bd0;
  background: white;
  border: 2px solid #ddd6fe;
  border-radius: 7px;
  cursor: pointer;
}

.import-btn:hover:not(:disabled) {
  border-color: #6d5bd0;
}

.cursor {
  display: inline-block;
  width: 0.5rem;
  height: 1rem;
  background: #cba6f7;
  vertical-align: text-bottom;
  animation: blink 1s steps(2) infinite;
}

@keyframes blink {
  50% {
    opacity: 0;
  }
}

.context {
  max-height: 40vh;
  overflow-y: auto;
}

.proposal {
  border-color: #ddd6fe;
  background: #faf9ff;
}

.proposal-body {
  max-height: 30vh;
  overflow-y: auto;
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 0.74rem;
  line-height: 1.5;
  white-space: pre-wrap;
  color: #374151;
  margin: 0.6rem 0;
}

.proposal-actions {
  display: flex;
  gap: 0.5rem;
  align-items: center;
}

.card {
  padding: 0.9rem 1rem;
  background: #f9fafb;
  border: 1px solid #e5e7eb;
  border-radius: 10px;
}

.card-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.card-head h2 {
  font-size: 0.95rem;
}

.card-note {
  font-size: 0.78rem;
  color: #6b7280;
  margin: 0.3rem 0 0.6rem;
}

.part-head {
  font-size: 0.85rem;
  font-weight: 700;
  margin-top: 0.9rem;
  display: flex;
  align-items: center;
  gap: 0.4rem;
}

.edited {
  font-size: 0.68rem;
  font-weight: 600;
  color: #6d5bd0;
  background: #ede9fe;
  border-radius: 999px;
  padding: 0.05rem 0.45rem;
}

.context-edit {
  width: 100%;
  padding: 0.6rem 0.7rem;
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 0.76rem;
  line-height: 1.55;
  border: 2px solid #e5e7eb;
  border-radius: 8px;
  background: white;
  color: #1a1a1a;
  resize: vertical;
  margin-bottom: 0.6rem;
}

.context-edit:focus {
  outline: none;
  border-color: #cba6f7;
}

.context-body {
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 0.76rem;
  line-height: 1.55;
  white-space: pre-wrap;
  color: #374151;
}

.composer {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.composer textarea {
  width: 100%;
  padding: 0.7rem 0.85rem;
  font-family: inherit;
  font-size: 1rem;
  line-height: 1.55;
  border: 2px solid #e5e7eb;
  border-radius: 10px;
  background: white;
  color: #1a1a1a;
  resize: vertical;
}

.composer textarea:focus {
  outline: none;
  border-color: #cba6f7;
}

.composer-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.primary {
  padding: 0.55rem 1.3rem;
  font-family: inherit;
  font-size: 0.9rem;
  font-weight: 600;
  color: white;
  background: #6d5bd0;
  border: none;
  border-radius: 8px;
  cursor: pointer;
}

.primary:disabled {
  background: #c7c4d6;
  cursor: not-allowed;
}

.primary.stop {
  background: #b91c1c;
}

.hint {
  font-size: 0.72rem;
  color: #9ca3af;
}
</style>

<style>
/* Unscoped: the rendered markdown is injected as HTML, so scoped attributes never reach it */
.md h3,
.md h4,
.md h5 {
  font-size: 1rem;
  font-weight: 700;
  margin: 0.9rem 0 0.35rem;
}

.md p {
  margin: 0.5rem 0;
}

.md p:first-child {
  margin-top: 0;
}

.md ul,
.md ol {
  margin: 0.5rem 0 0.5rem 1.2rem;
}

.md li {
  margin: 0.15rem 0;
}

.md code {
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 0.86em;
  background: #f3f4f6;
  border-radius: 4px;
  padding: 0.05rem 0.3rem;
}

.md pre {
  background: #f9fafb;
  border: 1px solid #e5e7eb;
  border-radius: 8px;
  padding: 0.6rem 0.8rem;
  overflow-x: auto;
  margin: 0.6rem 0;
}

.md pre code {
  background: none;
  padding: 0;
}

/* Wide tables scroll inside themselves rather than pushing the page sideways */
.md .md-table {
  overflow-x: auto;
  margin: 0.7rem 0;
}

.md table {
  border-collapse: collapse;
  font-size: 0.88rem;
}

.md th,
.md td {
  border: 1px solid #e5e7eb;
  padding: 0.3rem 0.6rem;
  text-align: left;
  white-space: nowrap;
}

.md th {
  background: #f9fafb;
  font-weight: 600;
}

.md td[lang='zh'],
.md span[lang='zh'] {
  font-size: 1.05rem;
}

.md hr {
  border: none;
  border-top: 1px solid #e5e7eb;
  margin: 0.9rem 0;
}
</style>
