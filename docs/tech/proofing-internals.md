# Spelling and grammar checking: how it is built

How BlueEdit's proofing works inside, written so that the parts can be reused in another program. User guide:
[../proofing.md](../proofing.md); installing: [../proofing-setup.md](../proofing-setup.md); what was found about Edge,
Chrome and Word: [browser-ai-research.md](browser-ai-research.md).

## The parts

```
                    EditorView (draws the text, F7, Shift+F7)
                     │                         │
        ISpellProvider (words)          IGrammarChecker (paragraphs)
         ├── Spell        Windows COM         ├── LanguageToolChecker   local LanguageTool server (Java, HTTP)
         └── OfficeSpell  Word COM            ├── LlmGrammarChecker     llama.cpp server (HTTP) + a .gguf model
                                              ├── EdgeProofreader       hidden Edge Canary (DevTools protocol)
         UserDictionary   dictionary.txt      └── GrammarCheckers       several of them as one
         Lookalike        mixed Latin/Cyrillic
                                              Prose      text as a checker should see it, its language
                                              TextDiff   corrected text → corrections with positions
```

| File | What | Depends on the editor |
|---|---|---|
| `Proofing.cs` | `ISpellProvider`, `UserDictionary` | `Settings.Directory` (default path only) |
| `Spell.cs` | Windows spell checking (`ISpellChecker` COM) | no |
| `OfficeSpell.cs` | Word's dictionaries through COM | no |
| `Lookalike.cs` | words typed partly in the other keyboard layout | no |
| `Grammar.cs` | `GrammarIssue`, `IGrammarChecker`, `LanguageToolChecker`, `JobObject`, `Prose` | `Prose.Plain` uses `EditorView.NotProseRx` |
| `LlmGrammar.cs` | `LlmGrammarChecker`, `GrammarCheckers` | no |
| `LlmServer.cs` | one llama.cpp server, shared by grammar and style | no |
| `Style.cs` | `StyleAdvisor`: style suggestions | `Prose.LanguageOf` only |
| `EdgeProofreader.cs` | Edge's Proofreader API | no |
| `TextDiff.cs` | word-level diff | no |
| `EditorView.Grammar.cs`, `EditorView.Problems.cs` | paragraphs, underlines, F7, Shift+F7 | the editor itself |

So the checkers and their helpers (about 1,300 lines) move to another .NET 8+ program as they are; only `Prose.Plain`
needs its "not prose" pattern (web addresses, e-mail, paths) passed in or copied.

## Rule one: never wait while typing

Checking must not cost a keystroke. Every source answers **from memory, at once**; anything slow happens on its own
thread and the screen is redrawn when the answer arrives:

- `ISpellProvider.IsCorrect(word)` returns `true`/`false`, or **`null` while unknown** (drawn as correct meanwhile).
  Windows answers in microseconds in-process; Office queues the word for its background thread.
- `IGrammarChecker.Issues(paragraph, language, queue)` returns the findings, or **`null` while not checked**, and
  queues the paragraph (`queue: false` asks without queueing).
- Both have **`Version`**, a counter that goes up whenever new answers arrive. The editor compares it on its 500 ms tick
  and redraws only then.
- A paragraph is queued only after it has not been edited for **1.5 s** (`GrammarQuietMs`): text being typed is not
  sent half-finished, and its old findings stay until the new ones come.
- Answers are cached per (paragraph text, language): moving around never asks again; an edit makes a new text.
- The underline mask is computed once per frame (`_grammarFrame`), not per cell.

Each grammar checker owns: a `BlockingCollection` work queue, one worker thread, a `ConcurrentDictionary` of answers,
a set of queued texts (so a text is sent once), a `volatile` status string and an `Interlocked` version.

## Preparing the text

- **Paragraphs**: consecutive prose lines (Markdown, LaTeX, plain text) between blank lines; code blocks and tables are
  skipped. Offsets of findings are into the paragraph text joined with `\n`; the editor maps them back to lines.
- **`Prose.Plain(line, markdown)`** returns the line with the same length, markup (`**`, `#`, links' URLs, inline
  code…), web addresses, e-mail and file paths **replaced by spaces**. Same length means every offset a checker
  returns is an offset in the real line — no mapping tables. (A checker sees "Write to  name ." where the line has
  an address; it does not flag the address, and the gap does not upset it.)
- **The paragraph's language**: below, "The language of a paragraph".

### The language of a paragraph

Until October 2026 it was "Russian when Cyrillic letters outnumber Latin, otherwise the user's English": right for
Russian and English, wrong for anyone else (Ukrainian checked as Russian, German as English; and a checker given
the wrong language is silent: the Obsidian plugin saw LanguageTool find nothing in a Russian paragraph sent as
`en-GB`). Now the same rules as the plugin BlueEdit shares its proofing with, the Obsidian plugin BlueProof
([loglux/obsidian-blueproof](https://github.com/loglux/obsidian-blueproof): `src/prose/language.ts`, `detect.ts`,
measured there; its `docs/design.md`, "The language of a paragraph"), in `ParagraphLanguage.cs` and
`EditorView.Language.cs`:

1. **The candidates**: the languages the Markdown front matter states (`language:` or `lang:`, as Obsidian notes say
   it; `de` spelled `de-DE` when that is a spelling language), else the spelling languages. Not read yet: LaTeX's
   `babel` options.
2. **By script**: the candidates written in the script most of the paragraph's letters are in (formulas blanked out:
   their variables would count as Latin). One left is the answer; this is all Russian and English ever need, so for
   them nothing else runs. None left (Russian text, only English listed): guessed as with no list. (The plugin keeps
   a single listed language even then; BlueEdit keeps the old rule's behaviour, Russian text checked as Russian.)
3. **Several of one script**: a candidate whose alphabet lacks a letter of the text is dropped ("ё", "ы", "э" are not
   Bulgarian; "і", "ї", "є" not Russian; "ъ" as a vowel not Russian: the plugin saw a Russian note with formulas
   called Bulgarian in six paragraphs of seven), then fastText's language identification model picks the likeliest
   candidate.
4. **A short text** (under 25 letters: a heading, "Thank you.") is not guessed from: it takes the document's main
   language, the one most of its longer paragraphs are in (at least two paragraphs and a fifth of them). That pass
   over the document (its first 400 runs of prose) is made only when two candidates share a script, after typing has
   paused, and each run's answer is kept by its text, so after an edit only the changed one is asked again.
5. **No list at all**: among the 52 languages LibreOffice has dictionaries for, of the text's script (the plugin: 95%
   of paragraphs right against 93% among all), answering a bare code (`de`).

**Why fastText, measured by the plugin** (1,500 Tatoeba texts in 12 languages and 142 of its own, share right with no
list): franc (trigram statistics) 76% / 87%, fastText `lid.176.ftz` 96% / 96%, CLD3 88% / 89%, Chrome's
`LanguageDetector` 95% / 95%. With the user's languages listed: own + English 100% of sentences and paragraphs, 96%
of short lines.

**Why our own C# port, not a library** (`FastText.cs`, about 300 lines; fastText is MIT): prediction is the average of
a few dozen rows of 16 numbers (the words' and their 2-4 letter pieces', from a product-quantized table) put through a
tree of the 176 labels; nothing a native library is needed for, and one exe on Windows and Linux stays one exe. The
model (`models/lid.176.ftz`, 0.9 MB, CC BY-SA 3.0, `models/README.md`) is a resource of the exe, loaded in the
background at start: 39 ms the first time, the code's compilation included, then 0.08 ms a paragraph (a 120-character
German sentence; measured in the unit tests' build). Until it is loaded, languages of one script take the first
listed.

**Checked against fastText itself**: the answers of its WebAssembly build (the one the plugin runs) for 1,621 texts
(`tools/language/fasttext-reference.mts`: the plugin's Tatoeba and own sentences, edge cases): the first three
languages the same for every text, probabilities within 1e-4; one fifth place swapped between two languages of
0.000092. Seven of them are unit tests (`FastTextTests`).

**What the checkers do with it**: LanguageTool gets the tag; Edge, Chrome and Harper check English only, as before;
the language model and the style model are told the language by name: the instructions measured for Russian and
English, and for any other language the same rules naming it ("The user's text is in German: write in German, never
translate"), not measured. Spelling is not per paragraph yet: a word is accepted when any spelling language knows it.

### Formulas (Markdown)

Maths notes mix prose and LaTeX: "where $x$ is the length", "substituting gives ⏎ $$…$$ ⏎ so the area is…". A formula
is not prose, but it is part of the sentence. What the editor does (`EditorView.Grammar.cs`):

- **Where formulas are**: `$…$` and `$$…$$` on a line (`Prose.MathRx`, the same rule as the highlighter and Obsidian: no
  space just inside the dollars, not `\$`), and the lines of a display block from `$$` to `$$` (`MathIn`,
  `ParagraphMath`).
- **A display formula does not split a paragraph**: a `$$` block with text right above or below (no blank line)
  belongs to the text's paragraph, so "gives ⏎ $$…$$ ⏎ so the answer is 3." is one sentence and the punctuation after
  a formula is checked. A paragraph that is only formulas is not checked.
- **Nor do blank lines around it**, as maths is usually written ("…so putting $y=0$ gives ⏎⏎ $$…$$ ⏎⏎ where c is a
  constant."): a display formula joins the text above it when that text has not ended (no `.` `!` `?` at its end);
  formulas after it join too (a calculation going on); text below joins when it goes on in lower case ("where",
  "and"), and so does text that starts with a formula ("$$r=7$$ and slant height", which before was not checked at
  all). Inside such a paragraph a blank line becomes a space, so LanguageTool reads one sentence. Measured on real
  academic maths notes before choosing the rule: 1084 display formulas stand between blank lines; the text before had
  not ended in 906 (a word 684, `:` 156, `,` 66; `.` 55), the text after started in lower case in 92 and with a capital
  in 815 (`ParagraphAt`, `FormulaAfter`, `FormulaBefore`, `EndsSentence`, `GoesOn`).
- **A finding on markup alone is dropped**: the checker saw `**` or `- ` as spaces, so a change there is about the
  editor's blanking (the model did that on real notes); an insertion such as a missing comma is kept.
- **LaTeX spoilt by JSON escapes is put back** (`LlmGrammarChecker.RepairLatexEscapes`): a model copies `\frac` into its
  JSON answer without doubling the backslash, which reads back as a form feed and `rac` (`\neq` as a line break, `\right`
  as a carriage return, `\times` as a tab, `\beta` as a backspace). Those "changes" were dropped as touching a formula,
  but enough of them tripped the rewrite guard and lost the real fixes. Found by the Obsidian plugin.
- **An answer cut off at the token limit** (`finish_reason: "length"`; Gemma 3 12B writing `\n` on and on after a display
  formula, found by the plugin) is reported as such for that paragraph instead of as broken JSON.
- **…and then asked again with each formula as one word** (`LlmGrammarChecker.WithoutFormulas`, `MapBack`). A
  model can loop while it copies a formula into `corrected` (YandexGPT 5 Lite: `\text{ }` 3,292 tokens on, to the
  limit) and the whole paragraph had no findings. The loop comes and goes with any change of the prompt, so it is
  caught where it shows: on a cut-off answer the paragraph is sent once more with every `$…$`, `$$…$$`, `\(…\)`,
  `\[…\]` and equation environment as `X`; the findings are moved back by the formulas' lengths, and any on an `X`
  dropped, as a finding on a formula is. Formulas as placeholders were measured slightly worse as the default (below:
  YandexGPT 15 of 16 vs 16 as is), but here they only touch paragraphs that are otherwise lost. Measured with
  `MathBench` (10 October 2026), the formula way *as is*:

  | Model, sets | Before | With the retry |
  |---|---|---|
  | YandexGPT 5 Lite, long paragraphs (8 texts, 32 errors) | 16 found, 13 fixed | **24 found, 21 fixed**; off target 0 → 1 |
  | Gemma 3 12B, long paragraphs, LaTeX, formulas (27 texts, 43 errors) | — | 28 found, 27 fixed; 2 retries |

  YandexGPT lost both long Russian paragraphs with formulas (`made-ru-quadratic`, `made-ru-statistics`); with the
  retry they gave 5 and 3 of their 6 errors. Gemma looped on `made-ru-quadratic` too (3 of 6 after the retry) and on
  one clean LaTeX paragraph, which got no false alarm. Not tried: llama.cpp's DRY or repeat penalty on the retry
  (copying the text repeats it by design), a shorter limit.
- **Each checker gets the formula in the form it asks for** (`IGrammarChecker.Formulas`; `GrammarCheckers` hands each
  its own text through `Issues(Func<MathView, string>, …)`). Today every checker gets the formula **as it is**.
- **Any finding that touches a formula is dropped** (`Prose.Touches`), and so is any style suggestion (Ctrl+F7):
  nothing inside a formula is ever changed or underlined.
- **What counts is the part a fix changes** (`Prose.ChangesFormula`, found by the Obsidian plugin): the word
  diff puts a mark put in after a formula together with its closing `$` (`$a=b$` → `$a=b$,`), so a model's comma
  after a formula ("Пусть $a=b$ тогда…") was dropped as touching it. What a fix keeps at the start and at the end of a
  finding is not counted, and a mark put in at a formula's edge is outside it; one put in before the closing `$`
  (`b` → `b,` in `$a=b$`) is inside. The blanked-markup filter no longer takes the blanked formula for markup.
- **A mark moved into a formula** (gpt-5.4-mini, 3 of 3 times: `…\right)$.` → `…\right).$`) comes out of the diff as a
  change inside the formula, dropped, and the full stop removed after it, which alone read as "possibly delete '.'",
  without the model's reason and wrong for an inline formula. A removal of one or two characters right after a
  formula is dropped when a finding inside that formula was. Unit tests only (the plugin's cases and this answer); not
  run with a live model in BlueEdit.
- **A change that only puts in or takes out `{ } ^ _ $ \`** is no finding (found by the Obsidian plugin: Gemini 3.5
  Flash-Lite put a `}` after a right display formula); with the fix above such a change at a formula's edge would
  have been kept.

### Formula errors without a model

`LatexCheck.cs` reads each formula once with a stack of groups (`{`, `\left`, `\begin`) and what is still wanted
(the arguments of `\frac`, of `^`). It reports only what stops MathJax and LaTeX, with MathJax's messages, so a finding
is never a guess; a model is the wrong tool here (it rewrites what is right). Rejected for now: unknown commands
(a document defines its own; a list of MathJax's would be long and still wrong for packages).
The findings are diagnostics with the source "LaTeX" (`EditorView.LatexCheck.cs`), next to a language server's, so
underlining, Shift+F7, F7 and the status count come for free; found again for the whole file after each edit.

**Measured** with `tests/BlueEdit.Tests/LatexBench.cs` (`BLUEEDIT_LATEX_BENCH`) on 726 real academic notes, about 134,000
formulas, 12 October 2026: first 33 findings, 13 of them false — `$\$ 14.8$`, where `Prose.MathRx` ended the formula at
the escaped dollar (it now lets `\$` stand inside, which the drawing and the grammar checkers share). After that 18:
16 real (two pandoc files whose `\begin{aligned}` was lost, `&` left bare), 2 in JavaScript written as text, which
Obsidian also reads as a formula. Not measured: LaTeX files, whose display environments are not checked yet.

**Rules of writing (step 2).** `LatexCheck.Style` looks for what typesets but not as meant (`cos` without `\`,
`\cos{(x)}`, `*`, `<=` and `->`, `...`, a unit outside `\text{}`): heuristics, so warnings, not errors, each with
the name of its rule and its fix. The switch per rule is the one grammar findings already have, F7 > Never show this
into `ignoredRules`, rather than a setting per rule; `latexStyle` turns them all off. Each rule leaves out what is
as often right: names in a subscript (`x_{max}`), `*` without a number on either side (`z^*`, `f * g`), `<-` and
`!=` (`x<-1`, `n!=120`), units of one letter (`2m`). Not looked at: the argument of `\text`, `\mathrm`,
`\operatorname` and the like, comments. The fix is offered only where the formula's characters are the line's (not
over a display formula's line break). Measured on the same notes (20 October 2026): first 108 warnings, 16 of them
false — a footnote's star after a formula in OCR'd textbooks (`$x>0 *$ )`) and `$...$` written as text about
formulas; now `*` needs an operand on both sides and a formula of dots alone is left out. After that 92, 2 false
(a table of calculator keys, `2 * \mathrm{x}`; a README whose `$comment_` the formula pattern takes for a formula).
- LanguageTool's `CONSECUTIVE_SPACES` is off, like `WHITESPACE_RULE`: blanked markup leaves runs of spaces, and that
  rule made two thirds of all LanguageTool findings on real notes.

**Measured** (`tests/BlueEdit.Tests/MathBench.cs`, run with `BLUEEDIT_MATH_BENCH`; 5 October 2026). Four ways of handing a
formula to a checker:

- *spaces*: the formula as spaces, BlueEdit's way before this measurement;
- *plugins*: the formula as text, findings that start inside it hidden — what the two Obsidian LanguageTool plugins do
  (wrenger/obsidian-languagetool 1.4.2, Clemens-E/obsidian-languagetool-plugin 0.3.7, read in their code);
- *placeholder*: a word in its place ("X" and spaces, same length), findings touching it dropped — what TeXtidote and
  LanguageTool's own LaTeX support do;
- *as is*: the formula as it is, findings touching it dropped — chosen.

On the first shared cases (34 texts: sentences from real academic maths notes, the same with an error put in, and
written ones; 16 errors, 18 correct texts — since October 2026 kept outside the public repository):

| Checker | Way | Errors found | False alarms on correct texts | Off target |
|---|---|---|---|---|
| LanguageTool | spaces | 5 of 16 | 3 | 2 |
| LanguageTool | plugins | 5 | 2 | 0 |
| LanguageTool | placeholder | 4 | 1 | 0 |
| LanguageTool | **as is** | 5 | 2 | 0 |
| YandexGPT 5 Lite | spaces | 12 | 8 | 10 |
| YandexGPT 5 Lite | placeholder | 15 | 0 | 0 |
| YandexGPT 5 Lite | **as is** | **16** | **0** | **0** |
| Gemma 3 12B | spaces | 11 | 13 | 10 |
| Gemma 3 12B | placeholder | 13 | 0 | 0 |
| Gemma 3 12B | **as is** | **14** | **0** | **0** |

After the next changes (blank-line formulas joined, LaTeX escapes repaired, markup-only findings dropped), on the same
kind of set (38 texts, 19 errors, 19 correct texts; *fixed* = the first suggested fix gives the expected text):

| Checker | Way | Found | Fixed | False alarms on correct texts |
|---|---|---|---|---|
| LanguageTool | as is | 5 of 19 | 5 | 2 |
| YandexGPT 5 Lite | spaces | 15 | 15 | 8 |
| YandexGPT 5 Lite | **as is** | **19** | **19** | **0** |
| Gemma 3 12B | spaces | 12 | 12 | 13 |
| Gemma 3 12B | placeholder | 14 | 14 | 0 |
| Gemma 3 12B | **as is** | **17** | **17** | **0** |

The three errors after a display formula with blank lines around it (as those notes write maths) were found by both
models, and neither flagged the correct text of that kind; LanguageTool found none of them.

On the public sets `math-en.json` and `math-ru.json` (23 October 2026: written sentences of the same kinds in place of
the notes' own; 42 texts, 22 errors, 20 correct texts; `BLUEEDIT_BENCH_SETS="en-math;ru-math"`):

| Checker | Way | Found | Fixed | False alarms on correct texts | Off target |
|---|---|---|---|---|---|
| LanguageTool | spaces | 9 of 22 | 9 | 3 | 1 |
| LanguageTool | plugins | 8 | 8 | 2 | 0 |
| LanguageTool | placeholder | 8 | 8 | 1 | 0 |
| LanguageTool | **as is** | 8 | 8 | 2 | 0 |
| YandexGPT 5 Lite | spaces | 18 | 17 | 5 | 1 |
| YandexGPT 5 Lite | placeholder | 20 | 20 | 0 | 0 |
| YandexGPT 5 Lite | **as is** | **22** | **22** | 1 | 0 |
| Gemma 3 12B | spaces | 18 | 18 | 13 | 1 |
| Gemma 3 12B | placeholder | 16 | 15 | 6 | 0 |
| Gemma 3 12B | **as is** | **22** | **22** | 5 | 0 |

The choice holds: *as is* finds every error with both models, and for them *spaces* finds the fewest with the most
false alarms (*plugins*, not listed, also found all 22, with 1 and 5 false alarms). Gemma
flags more correct texts here than on the notes' sentences (5 of 20, before 0 of 19). The first run of this set also
found a crash: Gemma's answer to a text with blanked formulas threw while its changes were joined (`JoinByNotes`, fixed).

On real academic maths notes (course work in English, notes in Russian — not in the repository; 1397
paragraphs with formulas, about 18,000 words; no ground truth there, so these are counts of findings, not of errors):

| Checker | spaces | plugins | placeholder | as is |
|---|---|---|---|---|
| LanguageTool (without `CONSECUTIVE_SPACES`) | 90, 66 next to a formula | 25, 5 | 29, 5 | 25, 5 |
| YandexGPT 5 Lite | 558, 364 next to a formula | 234, 73 | 193, 27 | 204, 43 |

Reading the findings that only one of placeholder / as is gave, both are the same kind of noise — the model touching
`**` bold markup, asking for a comma after "Thus" before a display formula, notes with unfenced code — so the shared
cases, where "as is" found more with no false alarms, decide.

The run also found a bug: one paragraph whose answer from the model was cut off ("Expected end of string, but reached
end of data") switched the model checker off for the rest of the session, silently. LanguageTool and Edge's
proofreader had the same flaw. Now a failed paragraph is only that paragraph's (`IGrammarChecker.LastFailure`,
shown once in the message line); only a server that does not start switches a checker off.

What it says:
- **Spaces were the worst**: a formula that vanishes leaves a broken sentence; the models then "repair" it (13 false
  alarms for Gemma), LanguageTool flags "two prepositions" in Russian ("на $(-1)^k$ в").
- **The models read LaTeX**: with the formula they caught "Here, $x$ represent…", with a placeholder they did not.
- **A placeholder did not help LanguageTool either**: nothing it found that "as is" missed ("the values that lies" was
  found only with the formula), and it adds its own false alarm: "a $10\%$ discount" becomes "a X discount", and
  LanguageTool wants "an X". Other placeholders ("x", "A", "N", "1", "XXX", "formula") did no better. So it was
  measured and dropped; `MathView.Placeholder` stays for the benchmark.
- **Line breaks**: LanguageTool ends a sentence at a blank line (a line of spaces counts). Measured: "into⏎X⏎and…" is
  one sentence, "into⏎X ⏎     ⏎and…" two. So in the forms where a display formula is blanked its line breaks become
  spaces.

Also with the blank-line rule: `\(…\)` and `\[…\]` on one line are formulas too (mathpix and pandoc write them: 45 and
26 in the same notes, against 2491 `$…$`); shell code outside a code block (`$(addprefix --profile ,$(VARS))`: a `$(`
and a word of three letters or more, or ` --` inside) is not taken for a formula — 5 such in the notes, all in one file.
Commas before a display formula were measured, not changed: of the findings on the notes, 9 of 204 (the model)
and 5 of 89 (LanguageTool) sit right before a `$$` — "Thus" → "Thus,", "so" → "so,", a matter of taste that **Never show
this** switches off. One limit of not reading formulas: in "если мы знаем, что $$a \times b = c,$$ то…" the comma
is inside the formula, and LanguageTool asks for one before "то".

Not done then: `.tex` files (grammar was not checked there at all; the notes measured had none, only pandoc's debug
files of one assignment), so they were left for later (LaTeX, below).

**Two inline formulas side by side** (found by the Obsidian plugin, 8 October 2026): `$a$$b$` was read as `$a` (nothing:
the closing `$` was followed by `$`) and then `$$b$` as a display formula to the end of the line, so the words after it
went unchecked; the highlighter even took that `$$` for the opening of a block and coloured the rest of the file as a
formula. Obsidian 1.14.4 draws two inline formulas. Now the inline `$…$` has no "not next to a `$`" ends, `$$…$$` is
tried first so a real one is still whole, and the highlighter steps over inline formulas before it looks for an opening
`$$` (`Lang.Opaque`). `\[` over several lines (Mathpix and pandoc write `\[`, the formula, `\]` on lines of their own)
is a block as `$$` is, but only when `\[` begins its line (`Block.AtLineStart`): elsewhere in Markdown `\[` is an
escaped bracket ("see \[1\]"), and an unclosed one would have made the rest of the file a formula. The plugin opens a
`\[` block anywhere on a line. Cases `side-by-side` and `bracket-block` in `math-en.json`. Not changed: the Live view
shows a `\[` block as written (Obsidian does not draw one either).

**The shared cases**: `tools/proofbench/cases/*.json`, one file per set, in the form the Obsidian plugin proposed:
`{ "name", "language", "math", "cases": [ { "id", "text", "source", "errors": [ { "wrong", "nth"?, "kind", "ok": [...] } ] } ] }`
— `wrong` the wrong text (its nth occurrence), `kind` grammar / spelling / punctuation, `ok` fragments of which the
text must contain one once the first suggested fix is put in (so a benchmark counts *fixed* as well as *found*), no
errors = a correct text. `math-en.json`, `math-ru.json` (written sentences, the blank-line cases above among them;
the first sets, of sentences from real notes, are not in the public repository). Change cases here first, then in the
plugin.

### LaTeX (`EditorView.Tex.cs`, `Prose.LatexPlain`)

The Markdown machinery with LaTeX's idea of text, so paragraphs, formulas, offsets and checkers are shared:
- `Prose.LatexPlain` keeps a line's length (offsets map back as for Markdown): the comment (an unescaped `%`), command
  names, braces and optional `[…]` become spaces; the argument of `\emph`, `\section`, `\footnote`… stays as text;
  `\cite`, `\ref`, `\label`… with their arguments become spaces and are also the line's formulas
  (`LatexMathIn`): "see Section~\ref{s}" reads with the reference as one word, like a formula, and a finding on it is
  dropped. `\%` `\&` `\_` keep the sign, `~` is a space.
- Which lines are text is worked out for the whole file once per edit (`TexNonProse`): the preamble (up to
  `\begin{document}` when there is one), environments with no prose (math, `figure`, `table`, `verbatim`, `tabular`,
  `tikzpicture`…), `\[ … \]` and `$$ … $$` over lines, comment lines.
- Display formulas (math environments and `\[ … \]`) are `MathBlockLine`s, the whole line a formula, so a sentence
  goes on across them as across a Markdown `$$` block. Measured: before this, "We define the function by
  \begin{equation}…\end{equation} which are continuous" was missed by every checker ("which are continuous" alone);
  after it YandexGPT found it (10 of 11 on tex-en.json instead of 9). A `figure` or `table` does not join.
- `\section{…}` and the like are paragraphs of their own (the next line starts a new one), `\item` starts one,
  `\begin`/`\end` of a list too — but not of a display formula.
- Style (`StyleAdvisor.Markup`) counts `\command` names as markup: a change that drops `\cite{…}` or `\emph` is not
  offered.
- MathBench opens the cases of a set with `"file": "tex"` as .tex files, and now drops findings on markup alone as the
  editor does (they were counted as false alarms before: a model's change of a blanked `~`).

## Spelling

### Windows (`Spell.cs`)

COM `SpellCheckerFactory` (CLSID `7AB36653-1796-484B-BDFA-E74F1DB7C1DC`), `ISpellCheckerFactory`
(`8E018A9D-2415-4677-BF08-794EA61F94BB`) → one `ISpellChecker` (`E7ED1C71-87F7-4378-A840-C9200DACEE47`; an older IID
gives `E_NOINTERFACE`) per language. A word is correct when **any** language accepts it — mixed Russian/English text
needs no language detection for spelling. Languages: the user's list in
`HKCU\Control Panel\International\User Profile\Languages`, filtered by `IsSupported`. Answers are cached in a
`ConcurrentDictionary`.

### Office (`OfficeSpell.cs`)

Word through late-bound COM, which must live in one single-threaded apartment: a background thread with
`ApartmentState.STA` creates `Word.Application`, a hidden document (`Documents.Add(Visible: false)`), and answers the
queue in batches (up to 300 words per language per round trip: one COM call per word would be far too slow).
`GetSpellingSuggestions` for F7. It quits Word only if it started it.

### Shared

- `UserDictionary`: `dictionary.txt`, one word per line, checked before any provider; a word added in lower case also
  accepts its capitalised forms.
- `Lookalike`: a word with both Latin and Cyrillic letters ("пpивет" with a Latin p) is always marked; F7 offers it
  in one alphabet (each letter mapped to its look-alike).

### Which languages: the system's, nothing built in

`"spellLanguages": "auto"` (the default) asks the system, `Spell.SystemLanguages`: on Windows the languages added there,
elsewhere the locale's (`LocaleLanguages`: `LANGUAGE`'s list, else the first of `LC_ALL`, `LC_MESSAGES`, `LANG`;
`de_DE.UTF-8` → `de-DE`; a bare language or `C`/`POSIX` names none, since dictionaries are per country). Only when it
names none is the settings' list used, `en-US` unless changed. Until October 2026 that fallback was `["ru-RU", "en-GB"]`,
the author's own pair: on Windows it never showed, but every Linux user got Russian and British dictionaries ticked,
whatever their language. Measured on the Linux release in containers: no locale (Debian 12, Ubuntu 24.04) → `en-US`
ticked in **Get spelling dictionaries**; `LANG=fr_FR.UTF-8` (Fedora 41) → `fr-FR`, and the spelling then runs in French.
The dictionaries are listed by tag, alphabetically; the descriptions of the checkers name what they cover (LanguageTool:
31 languages, its rule folders in 6.6; the models: many languages) rather than any one language.

A word in letters no spelling language is written in is **unchecked, not wrong** (`Scripts.Readable`, asked by all
three providers): with `en-US` alone every Russian word was underlined (seen in the walk on Debian and Ubuntu servers
whose locale names no language), which says nothing about the text and hides real mistakes among the marks. The word's
alphabet is that of all its letters and a language's comes from its tag, both as the paragraph's language finds them
(`ParagraphLanguage.ScriptOfLetter`, `ScriptOf`: `ru`, `uk`, `bg`... Cyrillic, `sr-Latn` Latin, Latin unless listed).
A word of mixed alphabets is checked as before (`Lookalike`); one in a script that table does not know is not. F7 on
such a word says it is not checked and which languages are on.

## Grammar checkers

All three follow the same pattern (queue, worker, cache, status, version) and add a **`Source`** to every finding, so
the user sees who said what.

### LanguageTool (`LanguageToolChecker`)

- Starts `jre\bin\java.exe -Xmx512m -cp languagetool-server.jar org.languagetool.server.HTTPServer --port <free>` and
  polls until it answers (5–7 s).
- `POST http://127.0.0.1:<port>/v2/check` with `text`, `language`,
  `disabledCategories=TYPOS,BRE_STYLE_OXFORD_SPELLING`, `disabledRules=WHITESPACE_RULE,COMMA_PARENTHESIS_WHITESPACE,
  UPPERCASE_SENTENCE_START`. Typos are the spelling provider's; the whitespace rules fire on the blanks `Prose.Plain`
  leaves.
- **`127.0.0.1`, never `localhost`**: Windows tries IPv6 first and every request costs 2 s.
- `matches[]` → `GrammarIssue(offset, length, message, replacements, rule.id, "LanguageTool")`.

### Language model (`LlmGrammarChecker`)

The approach Edge's built-in proofreader uses (its prompt is quoted in the research): **ask the model for the
corrected text, then find what changed by comparing** — models are bad at counting characters, good at rewriting
text. BlueEdit adds explanations by asking for JSON.

- **Server**: `llm\bin\llama-server.exe -m <model> -ngl 99 -c 4096 --host 127.0.0.1 --port <free>`, ready when
  `GET /health` says so.
- **Model choice**: `llmModel` if set, otherwise the first present of a list ordered by the benchmark (YandexGPT 5 Lite,
  Gemma 3 12B, Qwen3 8B, Qwen3 14B, Gemma 3 4B, Phi-4-mini), otherwise any `.gguf` by name.
- **Request**: `POST /v1/chat/completions` (OpenAI-compatible) with the system prompt `Instructions`, the paragraph as
  the user message, `temperature: 0`, `max_tokens: min(4000, 200 + 2·length)`,
  `chat_template_kwargs: {enable_thinking: false}` (Qwen3 would otherwise "think" first), and
  `response_format: {type: "json_schema", json_schema: {name: "proofread", schema: …}}` — llama.cpp then
  **constrains generation to the schema**, so the answer always parses:

```json
{"corrected": "<the whole text, corrected>",
 "errors": [{"original": "...", "replacement": "...", "alternatives": ["..."], "explanation": "..."}]}
```

- **The prompt** (`LlmGrammarChecker.InstructionsFor`) says: proofreader of the paragraph's language, the text is in
  it, never translate; fix only spelling, grammar and punctuation; do not rephrase, do not change style, do not change
  what is correct, keep line breaks; explanations in that language; when several fixes are right (the speaker's gender: «пошёл» /
  «пошла»), the likeliest in `replacement`, the others in `alternatives`. Every clause is there because a model did
  the opposite without it. A bare "you are a proofreader" made YandexGPT *translate* a Russian sentence into English.
- **Only the paragraph's own language is named** (`ParagraphLanguage.PromptName`, 21 October 2026). Until then a
  Russian or English paragraph was told "a proofreader for Russian and British English" and its style prompt "an
  editor of Russian and British English prose" (American English too), and "канцелярит" went to the style prompt of
  every language: written for the benchmark's two languages and left in. Now the language is put in for each
  paragraph: "proofreader of Russian", "of German", "of British English (en-GB spelling)", "of English (Australia)
  (en-AU spelling)" (a region only when it is not the language's home: "Portuguese (Brazil)"), explanations asked in
  it, "канцелярит" only for Russian. The rules themselves are unchanged. Not measured in BlueEdit after the change
  (the run was stopped by a memory shortage on the test machine); the Obsidian plugin measured the same change with
  no regression.
  Before, on 21 October 2026 (`ProofBench`, found / fixed): en-gb-academic grammar Gemma 3 12B 25/25, YandexGPT 5
  Lite 24/22, Gemma 3 4B 23/19; context-ru 8/6, 10/9, 3/2 — the numbers to compare a later run with.
- **American English** (`InstructionsFor`, found by the Obsidian plugin): a paragraph in `en-US` gets "American
  English (en-US spelling)" and "-ize, -or, -er, traveled, program, license, defense are American spellings" in place
  of the British sentences (en-GB and plain `en` keep the British ones). Before, en-US text was asked for
  British spelling too. Measured on 9 October 2026 (`ProofBench`, `BLUEEDIT_BENCH_SETS=en-us-academic`: 16 correct
  American sentences, 6 with a British spelling, 5 grammar errors), before → after:

  | | false alarms of 16 | British spellings found / fixed of 6 | grammar fixed of 5 |
  |---|---|---|---|
  | Gemma 3 12B | 12 → 0 | 0 → 5 | 5 → 5 |
  | gpt-5.4-mini | 13 → 0 | 0 → 6 | 5 → 5 |

  Before, both models "fixed" color, center, analyzed, traveled into British spellings. (gpt-5.4-mini's "There
  findings" → "These findings" is right too: the case takes both.) en-GB and Russian get the same prompt as before,
  so they were not measured again.
- **Sentences checked against each other** (found and measured first by the Obsidian plugin): after "keep line
  breaks" the prompt says to read the whole text first, as a sentence can be wrong only because of an earlier one —
  pronouns, gender and number agreeing with who and what was named before, tenses with the time words. Without it the
  models checked sentences one by one: «Анна пришла домой поздно. Потом сразу лёг спать», "Mary finished the report.
  Then he sent it" passed. Measured in BlueEdit on 8 October 2026 (`ProofBench`, `MathBench`; the plugin's pair and long
  paragraph sets added to `tools/proofbench/cases`), before → after:

  | Set | YandexGPT 5 Lite | Gemma 3 12B |
  |---|---|---|
  | British academic (24 grammar) | 23 → 23 | 24 → 24 |
  | Pairs, English (12) | 8 → 11 | 7 → 8 |
  | Pairs, Russian (14) | 8 → 10 | 6 → 8 |
  | Formulas, LaTeX, long paragraphs (63) | 48 → 46 | 35 → 44 |
  | False alarms on correct texts | 2 → 2 | 0 → 1 |

  The extra findings of Gemma's are next to an error, mostly the rest of the same fix (`она` → `он`, `бросила` →
  `бросил`), none elsewhere. YandexGPT's two lost: one long Russian paragraph whose answer looped on `\text{ }` while
  copying a formula and was cut off at the token limit (a loop that a change of the prompt can set off anywhere; its
  own issue). The British set unchanged: "The committee have agreed" still passes.
- **A paragraph of a LaTeX document** (`LooksLikeLatex`: a command outside `$…$` — `\cite`, `\ref`,
  `\begin{equation}` stay in the paragraph the checker gets; Markdown's LaTeX is all inside dollars) gets one more
  sentence (`LatexInstructions`): it comes from a LaTeX document, copy every command, brace, formula, `\cite` and
  `\ref` exactly, correct only the words around them. A change that puts a LaTeX command where there was none is
  dropped (`AddsLatex`: Gemma put `\\` between the rows of an `align`). A user's suggestion.
- **From answer to findings** (`IssuesFrom`):
  1. `TextDiff.Changes(paragraph, corrected)` → the changed stretches with positions in the paragraph. Changes with
     only whitespace between them are joined into one (found by the Obsidian plugin): neighbouring words
     rewritten at once otherwise came out as pieces each wrong alone — "что бы подготовится" → "чтобы подготовиться"
     as three findings, and taking the first gave "чтобы бы подготовится". Changes with a word between them stay
     apart ("Мы был на лекцие" keeps two). Any two runs of whitespace pair up and the original's is kept: Gemma 3 12B
     began corrected sentences on lines of their own, and the unpaired blank shifted the next fix (". Красивая" →
     ".\nКрасивый").

     **Measured on real answers** (9 October 2026): 514 distinct pairs of a text and a model's corrected text from the
     benchmark runs (the plugin's 96 runs of 4–7 October, its findings put back into the text to recover the
     corrected one, and `tools/proofbench/results.json`; gpt-5.4-mini, Qwen3 8B/14B, Gemma 3 4B/12B, YandexGPT 5 Lite,
     Phi-4-mini), 2,753 marked errors. Counted as the plugin's benchmark does: an error is fixed when one finding,
     taken alone, puts a right fix in its place.

     | | Findings | Errors fixed by one finding | Findings over two marked errors | Findings adding a line break |
     |---|---|---|---|---|
     | Pieces (before) | 3,248 | 2,207 | 1 | 37 |
     | Joined | 3,005 | 2,269 | 1 | 37 |
     | Joined, blanks paired | 3,005 | 2,269 | 1 | 21 |

     70 errors are fixed by one finding only when joined — agreement across two words ("он вычислил" → "она
     вычислила", "They are" → "It is"), words merged or dropped ("would have" → "had", "В следствии" → "Вследствие").
     8 only before (6 distinct): a right fix next to a wrong or needless one of the model's, which now come together
     ("method are" → "methods is", "Ему нравиться" → "Ей нравится", "хочу чтобы" → "хочу, чтоб"); the user can still
     decline it. Rejected: joining only when the number of words changes (it would keep those 6 apart) — 2,247 fixed,
     because it splits agreement pairs ("она вычислил"). The 21 line breaks left are models' garbage (`\nabla` from
     an unescaped `\n`, which `RepairLatexEscapes` repairs; whole sentences pasted in quotes, below).

     **Measured again on fresh answers** (9 October 2026): gpt-5.4-mini through the editor's own code (ProofBench on
     the sets without formulas, MathBench's `placeholder` way on those with them, ours and the plugin's), its 265
     answers kept (`AnswerLog`) and parsed both ways. 21 answers parse differently, all the joining's: agreement pairs
     ("He was" → "They were", "Он поставил" → "Она поставила") and merged words. Of the 119 marked errors in the
     answers that match a case one to one, one finding fixes 107 before and 112 now; none fixed before is lost now.
     Findings 284 → 265.
  1a. **A stretch the paragraph already has, put in again**, is no finding. YandexGPT 5 Lite repeated the sentence
     before in quotes ("Анна пришла домой поздно. «Анна пришла домой поздно.» Потом…") — 73 changes in the plugin's
     answers of 4–7 October, none of other models. `DropQuotedRepeats` takes a «…» of 24 characters or more out of the
     corrected text before the diff when its text stands elsewhere too and the original has no such quotation, so a
     fix next to it ("нравиться" → "нравится") stays a finding of its own; `CopiesText` drops any change left that puts
     in 24 characters of the paragraph from outside its own stretch (a stretch copied without quotes, where the
     widened insertion overlapped the next change). Not taken: counting inserted text in the rewrite guard — it would
     also drop right fixes of short sentences ("He explained me the rule" → "He explained the rule to me", 0.52).
  1b. **A translation or a changed number** is no finding (found by the Obsidian plugin). `ChangesScript`: a
     change from Latin to Cyrillic or back — Gemma 3 12B put "gradient descent" into Russian, Phi-4-mini answered
     English sentences in Russian; a word of both scripts at once ("Pис" with a Latin P) is a typo and stays.
     `ChangesNumbers`: other digits — Gemma 3 12B "corrected" a right "314 cubic centimetres" to 209. **Measured** on
     the 507 saved pairs of step 1 (9 October 2026, the changes after step 1a): 21 changes dropped — 18 translations
     ("intercepts" → «перехваты», "quadratic formula" → «квадратная формула», whole English sentences in Russian), 3
     changed numbers (314 → 209, 314 → 261.8, a `\circ` turned into a degree sign); 2 of the translations stood on a
     marked error, none of them a fix; no marked error lost its fix.
  2. **Rewrite guard**: if more than half the text changed (`MaxChangedShare = 0.5`), the answer is dropped — the
     model rewrote instead of correcting.
  2a. **The pieces of a moved word are one change** (`JoinByNotes`, found by the Obsidian plugin). A moved word
     comes out of the diff in pieces with words between them, which step 1 does not cross: "where is the station" →
     "where the station is" gave "is " → "" and "station" → "station is", each wrong alone ("where the station",
     "where is the station is"), both with the note's explanation. When the model's `errors[]` names a fragment that
     holds two or more changes, and a word one of them takes out another puts in (`MovesWord`), they become one from
     the first to the last ("is the station" → "the station is"). Done after the guard, because the words the join
     takes in between were not rewritten: before it, the plugin's two-error sentence (35 characters) counted as more
     than half changed and lost both fixes.

     **Rejected: joining every change inside a listed fragment** (the plugin's rule). The real-model test caught it:
     Phi-4-mini named "results was better then" → "results were better than" as one error, and "was" → "were" and
     "then" → "than" became one fix — two errors, each right alone, under one explanation that names only the first.
     On the 265 saved gpt-5.4-mini answers (9 October 2026) that rule parsed 4 answers differently ("Me and my friend",
     an agreement pair "Он оказался очень интересным" → "Она оказалась очень интересной", the two commas around
     "которую нам задали"); the move rule parses 2 ("Me and my friend" → "My friend and I", in two answers). Both: one
     finding fixes 113 of 119 marked errors (112 before), none lost. The local models' answers of step 1's
     measurement are pairs of texts without the list, so they could not be measured. Case `order-1` in
     `academic-en-gb.json`.
  3. Changes in whitespace only are skipped (the blanks of hidden markup).
  4. Each change gets the explanation (and alternatives) of the `errors[]` entry that fits it best (`NoteFor`, found
     by the Obsidian plugin): the entry with this wrong text and this fix; else one whose `original`, with the
     fix put in, gives its `replacement` ("home," with "," → "" gives "home"), or whose `replacement` holds the fix;
     else one whose `original` holds the wrong text, when that has a letter. "Holds" means as whole words. Otherwise
     "Possibly: …" / «Возможно, должно быть: …», and for a deletion "Possibly delete: “…”" / «Возможно, лишнее: «…»»
     (before, "Possibly: " with nothing after it).

     Before, the first entry whose `original` held the change won, and a short change — a comma, a letter, a short
     word — took another error's explanation ("Yes, he go home, said that.": the deleted comma explained as
     "Agreement of the verb."). **Measured on the 265 saved gpt-5.4-mini answers** (9 October 2026): 12 of 263
     findings get another explanation; 11 now name the right error ("Duplicate word." for "the the" where "Singular
     subject 'the integral' takes 'is'." stood, "Use 'an' before a vowel sound." for "a" → "an" where "Extra spaces
     removed." stood), and a wrong alternative "положительная" is gone; one "a" → "an" the model never listed is
     explained wrongly, as before. **Rejected: the plugin's three-letter rule** for the last case (an entry that only
     holds the wrong text): it left "me" ("explained me the rule") and «за» («оплатить за») unexplained, though their
     entries were there. Without whole words, "a" → "an" took an entry about "and the integral are", because "and"
     holds "an".
  5. Alternatives are fitted to the change's stretch: put the alternative in place of `original` in the paragraph,
     diff again, take the replacement at the same position («Я пошли» → «Я пошла» gives «пошли» → «пошла»).
  6. Rule id `LLM:<wrong>→<fix>`, so Ignore hides exactly that suggestion.
  7. **An error in `errors[]` that the corrected text left in** becomes a finding as the list has it, where its
     `original` stands in the paragraph — only when it stands there once (a short fragment such as "is" could be the
     wrong place), and not where a change from step 1 already is. A change that only closes a gap of blanked markup
     ("and␣␣takes" → "andtakes" after `\emph{and}`) is not one.
- Paragraphs over 3,000 characters are left to the other checkers.

**Step 7, why and what it costs** (6 October 2026). Gemma 3 12B on a LaTeX footnote answered `"errors": [{"original":
"were", "replacement": "was"}]` with "were" still in `corrected`, and the error was lost: findings came from the diff
alone. The models do this on plain prose too. Measured with `ProofBench` (British academic set) and `MathBench`, the
two local models before → after: British set grammar Gemma 3 12B 23 → 24 of 24 (`amount` → `number`), YandexGPT 5
Lite 22 → 23 (`who's` → `whose`); false alarms on its 24 correct sentences Gemma 0 → 0, YandexGPT 0 → 1; Gemma one
more finding off target (`suggests` → `suggests;` next to the comma it already fixed); LaTeX set Gemma 7 → 8 of 11
(the footnote), YandexGPT 10 → 10; the Markdown formula sets unchanged (Gemma 16, YandexGPT 20 of 20). The cost is the British line's own case: YandexGPT listed "The committee
have agreed" → "has" yet kept "have" in its text, a false alarm the corrected text alone never showed. A list entry the
model then thought better of and one it forgot to apply look the same, so they cannot be told apart; the finds were
judged worth it, and Ignore hides the one suggestion.

**Tried for LaTeX and rejected** (same day). Gemma 3 12B misses two of the LaTeX set's errors because it loops while
copying LaTeX into `corrected` — no-break spaces on and on after `studies \cite{…}`, `\n` after `\end{equation}` — and
the answer is cut off at the token limit. (1) *`errors` before `corrected`* in the schema and the prompt, so a cut-off
answer would still hold the whole list: Gemma's LaTeX set 7 → 9 of 11, but the order changes how the models answer —
YandexGPT on the British set 0 → 4 false alarms, spelling 6 → 4, the formula sets 30 → 27 of 31; Gemma's British
grammar 23 → 22. (2) *Asking again at a higher temperature* after a cut-off: at 0.3 and 0.7, three tries each, the
`\cite` sentence looped every time; the `equation` one got out at 0.7 but with the formula rewritten. YandexGPT 5 Lite,
the editor's first choice, finds 10 of the 11 anyway.

**What worked: saying it is LaTeX** (same day, a user's suggestion). With the LaTeX sentence in the instructions Gemma
stopped looping on both. Measured with `MathBench`, the two local models: given to *every* paragraph, Gemma's LaTeX set
8 → 10 of 11 but 3 false alarms (`\\` put into an `align`), YandexGPT 10 of 11 with one false alarm less, and on the
Markdown formula sets nothing more found and 3 more findings off target. Given only to LaTeX-document paragraphs, with
`AddsLatex`: Gemma 8 → 10 of 11 with no false alarm, YandexGPT 10 of 11 as before, the Markdown sets unchanged (Gemma
16, YandexGPT 20 of 20, nothing off target). The British set has no backslash: the sentence never reaches it.

### Edge's proofreader (`EdgeProofreader`)

- Starts the Edge in `edgePath` (default Canary, then Edge) with `--headless=new --remote-debugging-port=0
  --user-data-dir=%LOCALAPPDATA%\BlueEdit\edge-profile --no-first-run --no-default-browser-check
  --enable-features=AIProofreadingAPI --enable-blink-features=AIProofreaderAPI file:///…/blueedit.html`.
- Its stdin, stdout and stderr are pipes, its output read and dropped (`BrowserProofreader.StartInfo`). With the
  editor's terminal inherited, Chrome's log was drawn over the editor on a work computer (6 October 2026):
  `ERROR:google_apis\gcm\…` lines where its push service could not reach Google, and "Created TensorFlow Lite XNNPACK
  delegate for CPU". On the development machine it logs nothing, which is why it went unseen.
- Reads the port from `<profile>\DevToolsActivePort`, finds the page in `GET /json`, opens a `ClientWebSocket` to
  its `webSocketDebuggerUrl` and sends `Runtime.evaluate` (`awaitPromise`, `returnByValue`, `userGesture: true`).
- Waits for the page to load (`document.readyState`), checks `Proofreader.availability({expectedInputLanguages:['en']})`
  with a 30 s limit (Edge stable never answers), creates `window.__proofreader` once, then per paragraph
  `proofread(text)` with up to 3 tries (an occasional `AbortError`).
- `correctedInput` goes through the same `IssuesFrom` as the model's answer (with no explanations: Edge gives none).
- English paragraphs only; other languages get an empty answer at once.
- Why each of these steps: see the research, "Driving it from another program".

### Harper (`HarperChecker`)

- Runs `harper-ls --stdio` (from `%LOCALAPPDATA%\BlueEdit\harper`, else PATH) through the editor's own `LspClient`.
  Why the language server and not `harper-cli lint`: measured on 5 October 2026, the CLI took about 0.5 s per call
  (starting the program and loading its dictionary every time); the server answers a paragraph in 10–30 ms once warm
  (0.45 s for the first).
- Each paragraph is opened as a document of its own (`didOpen`, `languageId` markdown), its `publishDiagnostics` are
  the findings, then `didClose`. The fixes of a finding come from `textDocument/codeAction`: the quick fixes with one
  edit ("Replace with: …"); "Ignore Harper error" and the like are dropped. Positions are UTF-16, as C# strings.
- The dialect goes in the answer to `workspace/configuration`, which harper-ls asks before checking each document:
  `{"harper-ls": {"dialect": "British", "linters": {"SpellCheck": false}}}` (`LspClient.Configuration`; other
  servers still get null, their defaults). The dialect follows the paragraph's language: en-GB/UK/IE/NZ British,
  en-AU Australian, en-CA Canadian, other English American; other languages get an empty answer at once.
- Harper's own spell check is off, as LanguageTool's: spelling is the spelling provider's, and a second underline of
  the same word would only be noise. Its rule names (`ThenThan`…) are the findings' rules, for Ignore and
  `ignoredRules`.

### One model server for everything (`LlmServer`)

`LlmServer` owns the llama-server process for one model: started on first `Url()` (under a lock — whoever asks first
starts it, the others wait), `Chat(instructions, text, schemaName, schema, maxTokens)` sends one schema-constrained
chat request. The editor keeps one, shared by the grammar checker and style suggestions, so the model is in video
memory once; it is replaced when `llmDir`/`llmModel` change and stopped when grammar by the model is turned off
(style starts it again when asked). `LlmGrammarChecker(dir, model)` still makes a server of its own, for use
elsewhere.

**A server at an address** (`LlmEndpoint`, `LlmServer.At`): BlueEdit starts nothing and sends the same request
to its chat completions, plus the model's name, the key (`Authorization: Bearer`) and `reasoning_effort: "none"`.
Why each piece, from the Obsidian plugin's measurements and ours:
- The local llama.cpp stays first: it holds the model as long as the editor runs and was measured; a server is chosen
  (`llmModel: "server"`) or takes over when llama.cpp is missing or does not start (`Fallback`, said once in the status).
- What a server refuses comes back as `400` with `error.param` (OpenAI: `chat_template_kwargs`; reasoning models:
  `temperature`; newer models want `max_completion_tokens`): `Adapt` leaves the field out for this server and the
  request goes again. The local llama.cpp gets exactly the old request — no `model`, no `reasoning_effort` — so what was
  measured is what runs.
- `reasoning_effort: "none"` is Ollama's switch for thinking (it ignores `chat_template_kwargs`): without it Qwen spent
  the whole limit thinking and answered nothing. Tested with qwen3.5:9b: one-second answers.
- The key may be `env:NAME` or `file:PATH`, so it need not sit in settings.json; it is trimmed of a byte order mark.
  One typed in the editor goes to `llm-key` in the settings folder (`LlmEndpoint.SaveKey`, `KeyFile`) and the setting
  says `"file:llm-key"`. Why: settings.json is opened in the editor (on screen, shared screens included) and copied
  whole (to another computer, into a bug report); a file of its own is neither. How it is kept follows what programs
  that hold a key usually do with what the system already has, nothing installed: on Windows DPAPI
  (`CryptProtectData`, the way Windows keeps saved passwords; Azure CLI keeps its tokens so), so a copy of the file is
  useless to another user or computer; on Linux plain text with mode 600, as command-line tools keep tokens where no
  keyring runs (a session over SSH has none). Rejected: the Windows Credential Manager and the Secret Service
  (GNOME Keyring, KWallet) — the second is missing on servers and over SSH, and two stores for one key is more to go
  wrong than it is worth; an encryption of our own — its key would sit in the program. Neither way stops a program
  running as the same user: it can read the key as the editor does. A file written by hand is read as plain text.
- Paragraphs leave the computer only for an address outside it and the local network (`LlmEndpoint.Private`); the
  status line says where.
- **The cloud services in their own ways** (`LlmServer.Cloud.cs`, 23 October 2026), carried over from the Obsidian
  plugin (its [`src/checkers/llm.ts`](https://github.com/loglux/obsidian-blueproof/blob/main/src/checkers/llm.ts)),
  which measured them on 10–16 October; until then the editor asked every server in OpenAI's form, and Claude could
  not be used at all. Known by the address:
  - Anthropic: its Messages API (`x-api-key`, `anthropic-version`); the answer as the input of a tool the model must
    call, or, for Haiku 5.5 and Sonnet 5.5, which refuse the forced tool, by Anthropic's schema format with room to think.
  - Google: Interactions, its own API (`x-goog-api-key`, `store: false`): through its OpenAI form gemini-3.5-flash-lite
    lost a long Russian paragraph's errors in 2 runs of 3, through this in none of 3 (the plugin). The OpenAI form
    (`…/openai`) still works.
  - DeepSeek: no schema in `response_format`; the answer as a strict function call at its `/beta` address, or any JSON
    should that fail.
  - What each refuses is known before the first request (the plugin's `KNOWN_PROFILES`: OpenAI's
    `max_completion_tokens` and strict schema, Gemini 3's default temperature and thinking room, …); the rest is learnt
    from 400 answers as before, now in Anthropic's, Google's and DeepSeek's words too.
- `CloudCheck` asks each for real (`BLUEEDIT_CLOUD_CHECK="address|model|key;…"`): one English and one Russian paragraph
  with an error. 23 October 2026, all found both errors: gpt-5.4-mini 0.9–1.3 s; claude-haiku-5-5 1.5–1.6 s,
  claude-sonnet-5-5 2.0–2.4 s, claude-haiku-4-5 1.6–2.1 s; gemini-3.5-flash-lite 0.9–1.0 s, both ways; deepseek-flash
  1.4 s, deepseek-v4-pro 1.5 s. Not measured in BlueEdit: how each does over many texts (the plugin's benchmark is).
- Also from the plugin, found by comparing the editor's older code with it line by line: a key refused (401, 403) and
  no model named are said as such; at a limit of requests (429) nothing is sent until the time the service gave
  (`Retry-After`, "try again in 1.2s", Google's "retry in 54m9s" or `retryDelay`; else a minute) and the paragraph is
  asked again then — before, every paragraph asked and failed alike and was left as "nothing found"; a server's list of
  models leaves out those that write no text (embeddings, speech, pictures, OpenAI's realtime and -pro ones: the
  plugin's list, `LlmServer.IsTextModel`); and Google's OpenAI form is `…/v1beta/openai/chat/completions` — the editor
  had added a `/v1`, which Google took anyway, so only a unit test showed it.
- It found a bug: a key file of two lines (two Gemini keys) went into the header whole, and the exception ended the
  editor from the checker's thread. Now the first line is the key, and a key a header cannot carry fails that request
  with a message.
- The HTTP timeout is 5 minutes: Ollama's first load of a model took 6 min 45 s in a test, and closing the request makes
  Ollama abandon the load. A longer wait would hold the screen during a style suggestion; the docs say to load a new
  model once by hand.

### Style suggestions (`StyleAdvisor`, `Style.cs`)

Ctrl+F7 sends the paragraph *as it is in the file* (`ParagraphSource`, markup included — the model is told to keep
it) with instructions built from the chosen kinds of improvement (`styleFocus` → `StyleAdvisor.Focuses`) and the
paragraph's language **named outright** ("The paragraph is in Russian: write … in Russian"): with only "do not
translate" YandexGPT and Gemma rewrote Russian paragraphs in English. The answer
`{improved, changes: [{original, replacement, reason}]}` goes through `ChangesFrom`:

1. If `improved` is in another language (`Prose.LanguageOf`), the answer is refused with a message.
2. `TextDiff.Changes(paragraph, improved)`; changes in whitespace only are dropped, and for British English a change
   that only respells words the American way (`StyleAdvisor.Americanised`: every differing word equals the original
   with a British ending — -isation, -ise, -yse, -our, -tre, -lled, -ence, -ogue, -mme — spelt the American way).
   This runs before the joining below, or the respelling would ride along inside a real change next to it.
3. Changes at most one word apart are **joined** into one, so a reworded phrase is one suggestion, not five; and
   **all changes in one sentence** are one: a reordered sentence ("was substituted by us" → "we substituted")
   came as pieces several words apart that only make sense together, and taking some gave a broken sentence (seen in
   the Obsidian plugin with Qwen3 14B). Measured on the style sets, four models: the text with every change taken is
   the same (102 of 102 paragraphs), the number of changes halves (Gemma 3 12B 38 → 19 in English, 15 → 9 in Russian),
   about one per sentence it changes. The price: two independent small changes in one sentence are taken together.
   Spaces both sides keep at the edges are trimmed off.
4. A change whose markup (`**`, `__`, `` ` ``, `](`, `$`, web addresses) differs from its replacement's is not offered;
   when it is a joined sentence, its pieces that keep the markup are offered one by one instead.
5. Reasons are matched to changes by text, as for grammar.

Why the filter and the two instruction lines (measured on 5 October 2026, `tests/BlueEdit.Tests/StyleBench.cs`
on `tools/proofbench/cases/style-academic-en-gb.json` and `style-ru.json`): YandexGPT turned correct British text American
("organisation analysed … behaviour") and put contractions into formal text ("cannot" → "can't"). The line "keep the
register" stopped the contractions for every model; the British line helped Gemma and Qwen but YandexGPT kept
respelling, hence the filter — a rule, not a request. Russian was measured before and after: unchanged. The academic
register ("informal": "a lot of", "got", contractions) is a focus of its own, off by default — fine in other writing.

Unlike grammar there is no rewrite guard: rewording is what was asked for. The user ticks changes in a
`CheckListDialog` (with a details pane showing the selected one in full); `StyleAdvisor.Apply` makes the ticked ones
and `EditorView.ReplaceParagraph` puts the paragraph back as one undo step. The request runs on the UI thread with a
"asking…" note first: it is an explicit command and takes about a second.

### Together (`GrammarCheckers`)

- `Issues` asks every available checker and **keeps overlapping findings** (only exact duplicates — same offset,
  length and replacements — are dropped): LanguageTool often explains without a fix while the model gives the fix;
  F7 offers all of them, each signed with its source.
- `null` only while *no* checker has answered: one slow checker does not hide the others' findings.
- `Version` is the sum of the checkers' versions; `Status` is "starting" while any starts, otherwise the first
  failure.
- The status line names every checker and which failed (`GRAMMAR LT+LLM, EDGE failed`); the reason is shown once in
  the message line.

## Installing from the editor (`ProofingTools`)

Until 0.2.25 a checker was installed by a script of the repository (`install.ps1`, `install.sh`) or by hand, and
Tools → Grammar checking only listed what to download. That is the first thing a new user runs into, so the editor
installs the parts itself: Tools → Set up grammar checking, or `be --install-proofing` in a shell. The same
sources, versions and checksums as the scripts (llama.cpp `b11371` with the SHA-256 GitHub gives, models with Hugging
Face's, Harper 2.12.0 pinned, Java from Adoptium with its sum), into the same folders, so the scripts and the editor
see each other's work. The download, checksum and unpacking are `CodeTools`' (Tools → Code tools).

Decisions:

- **No default model.** The list shows each model's size and licence and the user picks: the one best at Russian
  (YandexGPT 5 Lite) has Yandex's own licence, Gemma 3 Google's terms; Qwen3 8B (Apache 2.0) and Phi-4-mini (MIT) are
  open. A user asked for the choice (20 October 2026).
- **LanguageTool is offered, not pushed**: a module for those who want rules in many languages without a video card
  (Java, ~530 MB). It is the free LGPL edition, run locally, no account.
- **Ollama and LM Studio are choices of their own** (20 October 2026, a user's wish: a choice, not everything at
  once). Before, they were behind "a model server (API)", where the address and the model's exact name had to be
  typed — which a new user does not know. Now the editor asks the program at its usual address (Ollama 127.0.0.1:11434
  or `OLLAMA_HOST`, the variable Ollama itself reads; LM Studio 127.0.0.1:1234) for `/v1/models` and lists them; names
  with "embed" or "minilm" are left out (embedding models cannot write; the test machine's Ollama listed four). Nothing
  answers, or no model: a text with the steps instead of an error. The question is asked while the dialog is open
  (the user is not typing), as Tools → Grammar model does; a refused connection to 127.0.0.1 takes about 2 s on
  Windows (measured with curl), long enough that "Asking … for its models" is shown first. Nothing is installed for
  them by BlueEdit: they are programs of their own with their own installers.
- **What was installed can be removed** (Set up grammar checking → Remove…; a user asked for it, 20 October 2026: a
  model is 2–8 GB). Only BlueEdit's own folders are looked in (`%LOCALAPPDATA%\BlueEdit`: `llm\bin`, each
  `llm\models\*.gguf`, `languagetool` + `jre`, `harper`, our Hunspell dictionaries), never a folder the settings point
  elsewhere: that may be the user's own install. A ticked checker is first taken out of `grammar` (a model here only
  when it is the one in use; a server at an address stays), so `ApplySettings` ends its process, then the files go in
  the background, each tried for 3 s while a process lets go. Not tested by a UI test beyond opening the list: the CI
  runner that has the checkers installed would list its own models; the listing and deleting are unit-tested on
  folders of their own.
- **A model server (API) is one of the choices**, not hidden in settings.json: many computers have no video card, and
  any OpenAI-compatible address works (Ollama, LM Studio, a cloud service). Nothing is installed for it.
- **The video card decides the llama.cpp build**, as in the scripts: CUDA with an NVIDIA driver (13.4 from driver 580),
  else Vulkan when Vulkan sees a video card, else the CPU build (said to be slow, and the set-up list says so). Vulkan
  is asked as llama.cpp asks it (`VideoCards.cs`): an instance from the loader, its physical devices, each one's type; a
  type CPU (llvmpipe, SwiftShader) is software drawing and does not count. The loader alone said nothing: Windows
  Sandbox has `System32\vulkan-1.dll` from its host and no device under it (22 October 2026), and until then Windows
  always took the Vulkan build, so the sandbox's list said "uses the video card (vulkan)". The asking runs in a process
  of its own (`be --vulkan-devices`): it loads the video card's drivers into the process, and one that misbehaves must
  not take the editor's unsaved text with it. It is slow the first time after Windows starts (2.7 s, nvidia-smi 0.7 s;
  then 0.27 s and 0.05 s, measured on a Windows 11 test machine with an RTX 4070), so it is done once a run, in the
  background, started when a menu first opens; the set-up list waits only for what is left. Vulkan not asked (the probe
  gave no answer): Windows keeps the Vulkan build, which also runs on the processor, Linux the CPU build. `install.ps1`
  asks the same through `Add-Type`; `install.sh` reads the device types from `vulkaninfo --summary` (Mesa's llvmpipe
  answers `vulkaninfo` too). What `be --vulkan-devices` gave (23 October 2026): that test machine Intel UHD (type 1,
  twice, by two drivers) and the RTX 4070 (2); Windows Sandbox "none", and its set-up list said "no video card found"; a
  Linux server and WSL only `llvmpipe` (4); a Linux NAS "none" (no libvulkan). Not tried: an AMD card, or Intel alone
  without NVIDIA.
- **llama-server is started once after installing** (`--version`): on a Linux NAS the CPU build needed `libgomp.so.1`,
  which that system lacks (measured 20 October 2026); a missing library is said at once, not at the first paragraph.
- **The Microsoft Visual C++ runtime on Windows, next to the programs** (`EnsureVcRuntime`): harper-ls needs
  `vcruntime140.dll`; llama.cpp's DLLs need it, `vcruntime140_1.dll` and `msvcp140.dll` too (read from their import
  tables). A clean Windows has none: in Windows Sandbox (20 October 2026) Harper installed, then failed with "no answer
  to initialize", nothing saying why. Microsoft allows these DLLs next to the program that uses them ("app-local"),
  and a program finds them in its own folder first, so after Harper or llama.cpp is unpacked the three are copied into
  its folder (`harper\`, `llm\bin\`) when System32 lacks them. No administrator, nothing outside BlueEdit's folders.
  They come from Microsoft's own installer (`aka.ms/vs/17/release/vc_redist.x64.exe`, 24 MB), kept once in
  `tools\vcruntime\`: the link has no published sum, so Windows checks the installer's signature
  (`Get-AuthenticodeSignature`, organisation Microsoft Corporation). The installer is a WiX bundle carrying its
  packages as cabinet files after its code; Windows' own `expand.exe` opens them (the cabinets found by their header
  and size), the package cabinets hold more cabinets and those the DLLs under names of their own
  (`vcruntime140.dll_amd64`, the ARM64 ones beside them). The DLLs are chosen by what each file says of itself (its
  original file name and the x64 machine type), not by those names, and each one's signature is checked too. In
  Windows Sandbox Windows names the DLLs' signer "CN=Microsoft Windows, O=Microsoft Corporation" (its own catalog
  knows them), so the organisation is compared, not the name.
  First done by running the installer (`/install /passive`, for every user): Windows asks for an administrator, the one
  step that would — a user asked why (20 October 2026), and it was changed. Rejected: shipping the DLLs in BlueEdit's
  zip (the single-file editor becomes a folder of files for a part most users never install). Harper is started once
  after installing too (`--version`), as llama-server is, and a program that exits with `STATUS_DLL_NOT_FOUND` is said
  to miss a system library. Checked in Windows Sandbox: the three DLLs (14.44) next to harper-ls, System32 unchanged,
  Harper ran. Not checked there: llama.cpp (its model is gigabytes); a llama.cpp or Harper copied in by hand without
  the DLLs still fails.
- **Downloads go on after a break**, with a Range request, up to five tries: languagetool.org's answer once ended
  after 2.7 MB of 240 on that NAS (the next try and curl got all of it); a model is 2–7 GB.
- **Paths do not depend on the folder existing**: on Linux .NET gives an empty string for the local data folder when
  `~/.local/share` (or `$XDG_DATA_HOME`) does not exist yet, and the editor then put `BlueEdit/` into the current folder
  (found installing into an empty test folder on that NAS). All such paths now use `SpecialFolderOption.DoNotVerify`,
  the settings' folder too.

- **Spelling dictionaries too** (Tools → Get spelling dictionaries): on Linux spelling needed `apt install
  hunspell-ru`, which needs root — not there on that NAS, nor for a user without sudo. LibreOffice's dictionaries come
  from [LibreOffice/dictionaries](https://github.com/LibreOffice/dictionaries) at a pinned commit (`DictionaryCommit`)
  through raw.githubusercontent.com; the repository publishes no sums, so the SHA-256 of each .dic and .aff at that
  commit are in the code (computed 20 October 2026). Eleven languages, each saved under its tag (`de_DE_frami.dic` →
  `de_DE.dic`, `fr.dic` → `fr_FR.dic`) so `HunspellSpell` finds it. A language with a dictionary in any folder
  Hunspell reads (the system's too) is not downloaded again and not ticked at first (a user's wish). The .aff goes
  first: a .dic alone is not taken, so a broken-off install leaves nothing half-used.

Checked on 20 October 2026: on that NAS (Linux, no video card) Harper, LanguageTool and Java, llama.cpp's CPU build and
Phi-4-mini into an empty folder, 50 s; on Windows the list (the CUDA 13.4 build chosen) and Harper. Not checked: a model
installed on Windows from the editor's window, Vulkan on Linux, a CUDA install from scratch, macOS.

Dictionaries, checked on that NAS (Celeron J4125) the same day: ru-RU, en-GB, uk-UA, de-DE, fr-FR and pt-BR downloaded in
4.5 s; in the editor F7 on a misspelt word in each gave Hunspell's suggestions (Hauss → Haus). Russian and English
together were ready within 2 s of the start; all six took longer than 4 s and under 25 s (not measured closer). Not
checked: the window's download on Windows, es-ES, it-IT, pt-PT, pl-PL and en-US loaded in the editor.

## Processes that must not outlive the editor

LanguageTool (Java), llama-server and Edge are child processes holding gigabytes of memory. Each is put in a Windows
**job object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`** (`JobObject.KillOnClose`): when the editor exits — or is
killed — Windows closes the job handle and ends them. `Dispose` also kills them (Edge with its process tree).

## Settings

| Key | Meaning |
|---|---|
| `spellCheck`, `spellLanguages`, `spellProvider` | spelling on/off, languages (`"auto"` or a list), `"windows"` / `"office"` |
| `grammar` | `"off"` or a list of `"languagetool"`, `"llm"`, `"edge"` |
| `languageToolDisabledRules`, `…DisabledCategories`, `…EnabledRules`, `languageToolLevel` | `LanguageToolOptions` for the request; a change recreates the LanguageTool checker |
| `ignoredRules` | rule ids / fixes never shown, filtered by the editor (`EditorView.IgnoredRules`) for every checker |

**F7 → Ignore** (until the editor closes) is filtered by the editor too, as `rule@text`: the finding's rule and the
text it flags (`EditorView.IgnoredHere`, found by the Obsidian plugin). Before, each checker was told to ignore
the rule. LanguageTool's rule ids are general (`COMMA_PARENTHESIS_WHITESPACE`), so one Ignore hid the rule in every
paragraph, which is what Never show this is for. A model's rule (`LLM:wrong→fix`) already names the text, so for it
nothing changes. The checkers no longer keep ignore lists of their own (`IGrammarChecker.IgnoreRule` is gone).
| `languageToolDir`, `llmDir`, `llmModel`, `edgePath` | where things are; empty = defaults under `%LOCALAPPDATA%\BlueEdit` |

Changing them (file or menu) recreates only the checkers whose settings changed.

## Measuring

Formulas: `tests/BlueEdit.Tests/MathBench.cs` (see Formulas above) — `$env:BLUEEDIT_MATH_BENCH='report.md'`,
optionally `BLUEEDIT_MATH_BENCH_MODELS` (`;`-separated .gguf names, `none`) and `BLUEEDIT_MATH_CORPUS` (folders of
notes), then `dotnet test tests\BlueEdit.Tests --filter MathBench`. It uses the editor's own code and the real checkers.

Checkers side by side: `tests/BlueEdit.Tests/ProofBench.cs` runs the sets without formulas in `tools/proofbench/cases`
(`"math": false`, first `academic-en-gb.json`: British academic English) through every checker installed — the
spelling providers as the editor underlines words, the grammar checkers as it asks them — one at a time (six models at
once do not fit in 12 GB of video memory). Per kind of error found and fixed, false alarms, the median time.
Findings on a text with an error that are not on it are split, as the plugin's benchmark does: *next to
an error* (in its sentence — often the rest of the same fix, «Он поставил… ушёл» → «Она… ушла») and *off target*
(elsewhere; `MathBench.NextToError`, the same in MathBench). The sets `context-en.json`, `context-ru.json` (pairs of
sentences where the second is wrong only because of the first) and `long-made-en.json`, `long-made-ru.json` (long
paragraphs with formulas) were written by the plugin:
`$env:BLUEEDIT_PROOF_BENCH='report.md'`, optionally `BLUEEDIT_PROOF_BENCH_CHECKERS` (windows, office, hunspell,
languagetool, llm, edge, harper, server) and `BLUEEDIT_PROOF_BENCH_MODELS`, then `dotnet test tests\BlueEdit.Tests --filter ProofBench`.
A model on a server: `BLUEEDIT_PROOF_BENCH_SERVER='https://api.openai.com/v1|gpt-5.4-mini'` and
`BLUEEDIT_PROOF_BENCH_KEY='file:~/keys/openai.txt'`; MathBench takes them too, with
`BLUEEDIT_MATH_BENCH_MODELS=server` (that model only) and `BLUEEDIT_MATH_BENCH_WAYS=placeholder` (one way of four).
`BLUEEDIT_BENCH_CASES` adds folders of sets (the plugin's `scripts/bench/cases`; a set of a name already read is
skipped). Both keep the models' answers in `<report>.answers.jsonl`, so the way answers become findings can be measured
again without asking the models again.

Style: `tests/BlueEdit.Tests/StyleBench.cs` runs the sets with `"kind": "style"` (paragraphs with style problems to
touch, facts and spellings to keep, paragraphs that read well) through `StyleAdvisor.Suggest`, a table per set:
`$env:BLUEEDIT_STYLE_BENCH='report.md'`, optionally `BLUEEDIT_STYLE_BENCH_MODELS` and `BLUEEDIT_STYLE_BENCH_FOCUS`
(comma-separated focuses), then `dotnet test tests\BlueEdit.Tests --filter StyleBench`.

`tools/proofbench/bench.py` sends 69 sentences (Russian and English; grammar errors, typos, correct sentences) to every
model with the editor's prompt of its time (an early one, kept as it was: one prompt for both languages) and schema, and to LanguageTool with the editor's parameters; `edge.py` does the
English ones through Edge's proofreader. A fix counts when the expected text is in the corrected sentence; a correct
sentence that comes back changed is a false alarm. Every answer is kept in `results.json`. Use it before changing the
prompt or the default model, and for any new model.

## Tests

- `tests/BlueEdit.Tests`: `ProofingTests`, `GrammarTests`, `LlmGrammarTests` (TextDiff, answer parsing, alternatives,
  rewrite guard, model order, merging; one test runs a real llama.cpp with Phi-4-mini when installed),
  `EdgeProofreaderTests` (a real Edge with `BLUEEDIT_EDGE_PROFILE` pointing to a profile that has the model;
  stable Edge with `BLUEEDIT_EDGE_STABLE_TEST=1`), `ProblemsTests` (Shift+F7).
- `tests/ui/test_markdown.py`: underlines, F7, Shift+F7, Office, LanguageTool and the model in the real editor.
- `tools/proofing-setup/check.ps1 -Run`: the installed parts answer a test sentence.

## Using the checkers elsewhere

```csharp
// a local language model as a proofreader, outside BlueEdit
using var llm = new LlmGrammarChecker(LlmGrammarChecker.DefaultDir, wantedModel: null);
IReadOnlyList<GrammarIssue>? found = null;
while ((found = llm.Issues("Мы был в магазине.", "ru-RU")) == null && llm.Available)
    Thread.Sleep(100);                                   // a UI would redraw on llm.Version instead
foreach (var i in found ?? [])
    Console.WriteLine($"{i.Offset}+{i.Length}: {i.Message} → {string.Join(", ", i.Replacements)} ({i.Source})");
```

The same works with `LanguageToolChecker(home)`, `EdgeProofreader(edgePath)` and `GrammarCheckers([...])`; for spelling,
`new Spell(new UserDictionary(path), ["ru-RU", "en-GB"])`.

