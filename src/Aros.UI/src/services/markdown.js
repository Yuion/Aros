// A small markdown renderer, enough for what a tutor actually writes: tables, code, lists,
// headings, bold and italic. Hand-rolled rather than pulled in, because the whole bundle is
// precached by the service worker and a full parser is most of it.
//
// Everything is escaped FIRST and formatted afterwards, so no text from the model can become
// markup. That ordering is the entire safety argument — do not reverse it.

function escape(text) {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

// Han characters need lang="zh" to be shaped correctly, and a cell of them should not wrap
const HAN = /[一-鿿㐀-䶿豈-﫿]/

function cell(text) {
  const inner = inline(text)
  return HAN.test(text) ? `<span lang="zh">${inner}</span>` : inner
}

// Applied to already-escaped text, so the patterns can only match what the writer wrote
function inline(text) {
  return text
    .replace(/`([^`]+)`/g, '<code>$1</code>')
    .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
    .replace(/(^|[^*])\*([^*]+)\*/g, '$1<em>$2</em>')
}

function isTableRow(line) {
  return line.trim().startsWith('|') && line.trim().endsWith('|')
}

function isRule(line) {
  return /^\|[\s:|-]+\|$/.test(line.trim())
}

function cells(line) {
  return line.trim().replace(/^\||\|$/g, '').split('|').map((c) => c.trim())
}

function renderTable(lines) {
  const rows = lines.filter((l) => !isRule(l)).map(cells)
  if (!rows.length) return ''

  const [head, ...body] = rows
  const headHtml = head.map((c) => `<th>${cell(c)}</th>`).join('')
  const bodyHtml = body
    .map((row) => `<tr>${row.map((c) => `<td>${cell(c)}</td>`).join('')}</tr>`)
    .join('')

  // Wrapped so a wide table scrolls inside itself rather than pushing the page sideways
  return `<div class="md-table"><table><thead><tr>${headHtml}</tr></thead><tbody>${bodyHtml}</tbody></table></div>`
}

export function render(source) {
  const lines = escape(source ?? '').split('\n')
  const out = []

  let i = 0
  while (i < lines.length) {
    const line = lines[i]

    // Fenced code: taken verbatim, no inline formatting inside
    if (line.trim().startsWith('```')) {
      const code = []
      i++
      while (i < lines.length && !lines[i].trim().startsWith('```')) code.push(lines[i++])
      i++
      out.push(`<pre><code>${code.join('\n')}</code></pre>`)
      continue
    }

    if (isTableRow(line)) {
      const table = []
      while (i < lines.length && isTableRow(lines[i])) table.push(lines[i++])
      out.push(renderTable(table))
      continue
    }

    const heading = line.match(/^(#{1,4})\s+(.*)$/)
    if (heading) {
      const level = Math.min(heading[1].length + 2, 6)
      out.push(`<h${level}>${inline(heading[2])}</h${level}>`)
      i++
      continue
    }

    if (/^\s*[-*]\s+/.test(line) || /^\s*\d+[.)]\s+/.test(line)) {
      const ordered = /^\s*\d+[.)]\s+/.test(line)
      const items = []

      while (
        i < lines.length &&
        (ordered ? /^\s*\d+[.)]\s+/.test(lines[i]) : /^\s*[-*]\s+/.test(lines[i]))
      ) {
        items.push(`<li>${inline(lines[i].replace(/^\s*(?:[-*]|\d+[.)])\s+/, ''))}</li>`)
        i++
      }

      out.push(ordered ? `<ol>${items.join('')}</ol>` : `<ul>${items.join('')}</ul>`)
      continue
    }

    if (/^\s*(---+|\*\*\*+)\s*$/.test(line)) {
      out.push('<hr />')
      i++
      continue
    }

    if (line.trim() === '') {
      i++
      continue
    }

    // A run of ordinary lines is one paragraph; single newlines inside it are breaks
    const paragraph = []
    while (
      i < lines.length &&
      lines[i].trim() !== '' &&
      !isTableRow(lines[i]) &&
      !lines[i].trim().startsWith('```') &&
      !/^#{1,4}\s/.test(lines[i]) &&
      !/^\s*[-*]\s+/.test(lines[i]) &&
      !/^\s*\d+[.)]\s+/.test(lines[i])
    ) {
      paragraph.push(lines[i++])
    }

    if (paragraph.length) out.push(`<p>${inline(paragraph.join('<br />'))}</p>`)
  }

  return out.join('\n')
}

/**
 * The three-column tables the importers parse, pulled back out of a message so the page can offer
 * to import one. Deliberately the same shape the server-side parser expects: it finds cells by
 * content, so a header row is harmless and column order is forgiving.
 */
export function findTables(source) {
  const lines = (source ?? '').split('\n')
  const tables = []

  let current = []
  for (const line of [...lines, '']) {
    if (isTableRow(line)) {
      current.push(line)
      continue
    }

    if (current.length >= 2) {
      const rows = current.filter((l) => !isRule(l)).map(cells)
      const chinese = rows.filter((r) => r.some((c) => HAN.test(c)))

      if (chinese.length) {
        tables.push({
          text: current.join('\n'),
          rows: chinese.length,
          // A sentence is long or ends in punctuation; a word is neither
          looksLikeSentences: chinese.some((r) => {
            const han = r.find((c) => HAN.test(c)) ?? ''
            return han.length >= 4 || /[。？！，]/.test(han)
          }),
        })
      }
    }

    current = []
  }

  return tables
}
