# HSK syllabus

`hsk.tsv` is the vocabulary of HSK 3.0 as revised for **2026** — the standard that takes effect
in the second half of 2026, with 300 words at level 1 rather than the 500 of the 2021 list or
the 150 of HSK 2.0.

One row per word per level: `level`, `word`, `pinyin`, `rank`.

The words and levels come from
[Punpuf/hsk-syllabus-vocabulary-parser](https://github.com/Punpuf/hsk-syllabus-vocabulary-parser),
which extracts them from the official syllabus PDF. Level counts check out against the
published figures: 300 at level 1, 204 at 2, 507 at 3, 1019 at 4, 1638 at 5, 1815 at 6 and
5622 across 7–9.

`rank` is corpus frequency from
[drkameleon/complete-hsk-vocabulary](https://github.com/drkameleon/complete-hsk-vocabulary)
(MIT) — **1 is the most common word in the language**, so ascending order is "teach this first".
294 of the 300 level-1 words carry one; the handful without are left blank and sort last. This
is what stops the tutor working alphabetically and teaching 杯子 before 的.

The syllabus writes homographs as 本1 and 本2. The trailing digit is not part of the word and
is stripped when the file is built, so a lookup by characters matches the vocabulary table.

To rebuild it, re-run the generator in the commit that added this folder: download the parser's
`hsk_word_list.tsv` and the vocabulary project's `complete.json`, join on the simplified form,
and write `level, word, pinyin, rank`.
