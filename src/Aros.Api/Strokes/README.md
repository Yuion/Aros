# Stroke order data

`strokes.pack.gz` is the [Make Me a Hanzi](https://github.com/skishore/makemeahanzi) character
data, as packaged by [hanzi-writer-data](https://github.com/chanind/hanzi-writer-data) 2.0.1.
9574 characters, each with its strokes as SVG paths **in writing order** and the median line
of each stroke — the centreline a brush would follow, which is what lets the page animate one.

The pack is one gzip member holding a `character<TAB>json` line per character, 12 MB on disk
against 31 MB of loose JSON. It is here rather than in `Aros.UI` for two reasons: the service
worker precaches the app shell, and 9574 files is not a shell; and the browser must never be
asked to fetch a character from a CDN, which is what the upstream library does by default.

The data is derived from Arphic's fonts and carries the Arphic Public License (`ARPHICPL.TXT`).
The tooling around it is MIT. Neither is expected to change — the pack is rebuilt only to take
a new upstream release:

    npm install hanzi-writer-data
    node pack.js      # character + '\t' + {strokes, medians}, joined by newlines, gzip -9
