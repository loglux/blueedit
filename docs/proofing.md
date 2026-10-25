# Spelling and grammar in BlueEdit

*По-русски: [proofing.ru.md](proofing.ru.md)*

Installing all the parts, by script or by hand: [proofing-setup.md](proofing-setup.md).

Checking has two independent parts:

| | What it finds | Who checks | Where it is turned on |
|---|---|---|---|
| **Spelling** | misspelled words | Windows dictionaries, Microsoft Word or Hunspell | F8, **Tools → Spelling provider…** |
| **Grammar** | agreement, punctuation, wrong words in context | LanguageTool, a language model (here, on a model server or a cloud service), Harper, Edge's or Chrome's proofreader, any together | **Tools → Grammar checking…** |

They work at the same time: typos are underlined in one colour, grammar in another. Everything is checked on your
computer, and the text is sent nowhere, unless you choose a model server elsewhere or a cloud service for grammar:
then the paragraphs being checked go there.

## Keys

| Key | What it does |
|---|---|
| **F7** | on an underlined place: the fixes |
| **Shift+F7** | to the next problem (selects it, then F7) |
| **Tools → Previous problem** | to the previous one |
| **F8** | spelling on / off |
| **Tools → Grammar model…** | which model checks grammar (and style) |
| **Ctrl+F7** | style suggestions for the paragraph at the caret |
| **Tools → Style options…** | what style suggestions look for |

### Who found it

Every finding is signed with its source:

- F7 opens one window for a typo and for grammar: the fixes as buttons, the explanation whole under them, then
  *Ignore* and *Never show this* (for a typo: *Add to dictionary*, *Ignore*), and at the right, in grey, who found
  it: `Windows spelling` (or `Office spelling`, `Hunspell spelling`), the model, `LanguageTool · ` and its rule:

  ```
  ┏┥Grammar: "checks"┝━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┓
  ┃                                                          ┃
  ┃ [► check ◄]                                              ┃
  ┃                                                          ┃
  ┃ The plural subject "examples" takes the plural verb      ┃
  ┃ "check", not the third-person singular "checks".         ┃
  ┃                                                          ┃
  ┃ [  Ignore  ] [  Never show this  ]      claude-haiku-5-5 ┃
  ┗━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┛
  ```

  Enter takes the fix with `► ◄`; Tab or ← → go to the next button, ↓ to the actions, Esc closes. *Ignore* lasts until
  the editor closes. The message line also shows LanguageTool's rule and its category, e.g.
  `[AGREEMENT_SENT_START · GRAMMAR]`.

- the status line after Shift+F7: `Spelling (Windows): speling`, `Grammar (LanguageTool): …`,
  `Grammar (Gemma 3 12B): …`.

When several checkers flag the same place, F7 shows the fixes from all of them. Often one only explains the problem
(LanguageTool does that a lot) and another gives the fix.

## Spelling

### Provider: Windows, Office or Hunspell

**Tools → Spelling provider…**

- **Windows** (default): the dictionaries built into Windows. Instant, nothing to install.
- **Office**: Microsoft Word's dictionaries. Needs Word installed; it starts hidden in the background, so the first
  words take a couple of seconds longer. Word's dictionaries are fuller (more terms, names, rare forms).
- **Hunspell**: the dictionaries of LibreOffice and Firefox (`ru_RU.dic` + `ru_RU.aff`), read by
  [WeCantSpell.Hunspell](https://github.com/aarondandy/WeCantSpell.Hunspell), a C# port of Hunspell inside BlueEdit.
  **The only provider on Linux and macOS**, with the system's dictionaries (`apt install hunspell-ru
  hunspell-en-gb`) or those of **Tools → Get spelling dictionaries…** (LibreOffice's, downloaded without root); on Windows it reads `%LOCALAPPDATA%\BlueEdit\hunspell`. The dictionaries load in the background
  (under half a second for Russian and English, measured in WSL), meanwhile nothing is underlined. Checked on Linux
  (WSL Ubuntu 24.04 with its hunspell-ru and hunspell-en-gb); on Windows only with a test dictionary so far.

### Languages

**Tools → Spelling languages…**: tick the languages you need. **auto** takes the languages added in Windows
(Settings → Time & language → Language & region). If a language is missing from the list, add it in Windows (for
Office, install the Office language pack). On Linux and macOS **auto** takes the locale's languages (`LANGUAGE`, else
`LC_ALL`, `LC_MESSAGES` or `LANG`: `de_DE.UTF-8` → `de-DE`). When the system names none (a server's `C` locale), the
list in the settings is used, English (`en-US`) unless changed.

English: `en-GB` and `en-US` are different dictionaries (colour/color, organise/organize); pick the one you write in.

A word in letters none of the spelling languages uses (Russian with only English on) is not underlined: it is
unchecked, not wrong. F7 on it says so.

### The language of a paragraph

Grammar checking and style suggestions check each paragraph in its own language, chosen among the spelling languages:

- **By its letters** first: Cyrillic, Latin, Greek, Arabic... With Russian and English that alone decides.
- **Languages with the same letters** (English and German, Russian and Ukrainian) are told apart by fastText's language
  identification model (176 languages, built into the editor, no network): the same answers as fastText itself, about
  0.1 ms a paragraph. A letter a language does not have rules it out first ("ы" is not Ukrainian or Bulgarian).
- **A short line** (a heading, a few words) is too short to tell: it takes the language most of the document's longer
  paragraphs are in.
- **A Markdown document can say its languages** in its front matter, as Obsidian notes do, and that wins:

  ```
  ---
  language: de
  ---
  ```

  (`lang:` too; several: `language: de, en`). `de` is checked as your spelling language `de-DE` when you have it.
- Text in a script none of your languages uses (Russian with only English ticked) is guessed among the common
  languages, or left to LanguageTool's own guess.

The language model is told the paragraph's language by name. Measured on Russian and English only; for other
languages it gets the same instructions with the language named, not tuned for it. Spelling still accepts a word that
any of your languages knows.

### Your own dictionary

**F7 → Add to dictionary** adds the word to `%APPDATA%\blueedit\dictionary.txt`: one word per line, editable by
hand, shared by every spelling provider (Windows, Word, Hunspell). **F7 → Ignore** skips the word until the editor is closed.

### Not checked

Web addresses, e-mail, file paths, code and code blocks. A word typed partly in the other keyboard layout ("пpивет"
with a Latin p) is always marked; F7 offers it in one alphabet.

## Grammar

**Tools → Grammar checking…**: tick the checkers (any together, or none). Ones not installed are marked
`(not installed)`; choosing one shows what to install and where. Works in Markdown, LaTeX and plain text.

A paragraph is sent for checking once it has not been edited for about a second and a half, so the underline
appears shortly after you stop typing, not while you type. The checkers start on first use (a few seconds; the
status line shows `GRAMMAR LT+LLM starting`) and stop with the editor.

**Text with formulas** (Markdown with `$…$` and `$$…$$`, as in Obsidian): the checkers read the sentence around a
formula, but nothing inside a formula is underlined or changed — not by grammar, not by style suggestions. A display
formula is part of the sentence around it, with or without blank lines: "so putting $y=0$ gives ⏎⏎ $$…$$ ⏎⏎ where c is a
constant." is checked as one sentence — the formula joins text that has not ended (no full stop), and text after it
that goes on in lower case joins too; a comma or full stop after a formula is checked. Measured on real
academic notes in mathematics: the language models found every error put in next to formulas and flagged nothing in correct sentences
(details: [docs/tech/proofing-internals.md](tech/proofing-internals.md#formulas-markdown)). `\(…\)` and `\[…\]` count as formulas
too, `\[` over several lines when it begins its line. Two formulas side by side, `$a$$b$`, are two, as Obsidian
draws them.

### Formula errors

A formula that would not typeset is underlined as an error, as a language server's errors are: **Shift+F7** goes to
it, **F7** says what is wrong, the status line counts them (`TeX 2E`). No model and nothing to install: the editor
reads the formula itself. What it finds is what MathJax (Obsidian) and LaTeX stop on, in MathJax's words:

- a brace, `\left`, `\begin{…}` without its other half, or `\begin{aligned}` ended by `\end{array}`;
- a command without its arguments: `\frac{a}`, `\sqrt`, `x^`;
- `x^2^3` or `x_1_2` ("use braces to clarify"), an `&` outside `aligned`, `array` and the like, `\left{`.

Unknown commands are not flagged: a document may define its own. Markdown files: `$…$`, `$$…$$` (over several lines
too), `\(…\)`, `\[…\]`, not in code; LaTeX files: formulas within a line only, not yet the display environments.
Measured on 726 notes of
mathematics (about 134,000 formulas): 18 findings, 16 of them formulas that lost their
`\begin{aligned}` in a conversion from Word, 2 in program code left outside a code block, which Obsidian also takes
for a formula. **Tools → Settings → Formula errors** (`latexCheck`) turns it off.

**Rules of writing**, as [chktex](https://www.nongnu.org/chktex/) has: what typesets, but not as meant. Each is a
warning (`TeX 1W`), and **F7** puts in its fix or, with *Never show this*, switches that rule off (Tools → Ignored
rules brings it back):

| Rule | Written | Comes out as | Fix |
|---|---|---|---|
| `latex-function` | `cos x`, `ln(y)` | the letters c·o·s in italics | `\cos x` |
| `latex-function-braces` | `\cos{(x)}` | a space between cos and the bracket | `\cos(x)` |
| `latex-times` | `2*3`, `x * 4` | an asterisk | `\times` |
| `latex-operator` | `<=`, `>=`, `->`, `=>`, `<=>` | two characters | `\le`, `\ge`, `\to`, `\Rightarrow`, `\Leftrightarrow` |
| `latex-dots` | `1, 2, ..., n` | three full stops | `\dots` |
| `latex-unit` | `5 cm`, `12kg` | the unit in italics, as letters multiplied | `5\,\text{cm}` |

Not flagged, as right: `x_{max}` (a label), `z^*`, `f * g` (no number), `x<-1`, `n!=120`, a single-letter unit such as
`2m` (as often a variable). Measured on the same notes: 92 warnings, of them 2 false (a table of calculator keys,
where `*` is meant, and a text that is not a formula); the rest real: `\cos{(\angle DEF)}`, `cos\ 28^{\circ}`,
`5.536\ km`, `2*5*0.2`, `233.333...`. **Tools → Settings → Formula style hints** (`latexStyle`) turns them all off.

### LanguageTool

The free, open-source edition of [LanguageTool](https://languagetool.org) running as a local server (not the paid
cloud service). Rules rather than a neural network: it explains every finding and is good at word pairs
("одеть/надеть"), but often gives no fix. About 0.7–1 GB of memory while it runs.

Install into `%LOCALAPPDATA%\BlueEdit\`:

```
jre\            Java 21 JRE: the Eclipse Temurin zip from adoptium.net, unpacked
languagetool\   LanguageTool-stable.zip from languagetool.org, unpacked
```

Somewhere else: the `languageToolDir` setting.

### Language model

An open model in [llama.cpp](https://github.com/ggml-org/llama.cpp)'s server, used as a proofreader: it returns the
corrected text with short explanations, and the editor compares it with yours and underlines the differences. Good
at agreement and commas ("results was", "Мы был", "сказал, что") and gives the fix right away. If the model rewrote a
paragraph instead of correcting it, its answer is dropped. English is checked with British spelling, or American
when `en-US` is your English spelling language (before 0.2.9 American text was asked for British spelling, and models
"corrected" color, center, analyzed).

Install into `%LOCALAPPDATA%\BlueEdit\llm\`:

```
bin\            llama.cpp for Windows: the CUDA (NVIDIA) or Vulkan zip from its GitHub releases, unpacked
models\         a .gguf model file
```

Models tried (Q4_K_M quantization):

| Model | Video memory | Where (Hugging Face) |
|---|---|---|
| **YandexGPT 5 Lite 8B** | ~6 GB | `yandex/YandexGPT-5-Lite-8B-instruct-GGUF`: best at Russian |
| **Gemma 3 12B** | ~8 GB | `bartowski/google_gemma-3-12b-it-GGUF`: strong in both languages |
| Qwen3 8B / 14B | ~6 / ~10 GB | `Qwen/Qwen3-8B-GGUF`, `bartowski/Qwen_Qwen3-14B-GGUF` |
| Gemma 3 4B | ~3 GB | `bartowski/google_gemma-3-4b-it-GGUF`: for smaller GPUs |
| Phi-4-mini | ~3 GB | `bartowski/microsoft_Phi-4-mini-instruct-GGUF`: the fastest |

The full comparison is below, under "Comparison: which is more accurate". **Tools → Grammar model…** lists the
models installed and **Auto**, the best of them by the benchmark (YandexGPT 5 Lite, then Gemma 3 12B, Qwen3 8B,
Qwen3 14B, Gemma 3 4B, Phi-4-mini). The choice is saved as `llmModel`. Answers take
0.2–1 s per paragraph on a mid-range GPU. Somewhere else: `llmDir`.

#### A model server at an address (Ollama, LM Studio, another computer, a cloud API)

llama.cpp on this computer, started by the editor, comes first. A model server at an address is the second choice
and the fallback: anything that speaks OpenAI's chat completions — Ollama, LM Studio, llama-server on a computer with a
GPU elsewhere on the network, a cloud API. In the settings file:

```jsonc
"llmServer": "http://127.0.0.1:11434/v1",   // Ollama here; or http://192.168.1.20:8080 (llama-server), https://api.openai.com/v1
"llmServerModel": "qwen3.5:9b",             // the model's name there
"llmApiKey": "env:OPENAI_API_KEY",          // a key if the server wants one: as it is, "env:NAME" or "file:PATH" (e.g. file:~/key.txt)
```

**The cloud services** are asked each in its own way, as the Obsidian plugin
[BlueProof](https://github.com/loglux/obsidian-blueproof) found works best, so the
address is all there is to give (and the key, and the model):

| Service | `llmServer` | A model it was tried with |
|---|---|---|
| OpenAI | `https://api.openai.com/v1` | `gpt-5.4-mini` |
| Anthropic (Claude) | `https://api.anthropic.com/v1` — its own API, not OpenAI's form | `claude-haiku-5-5`, `claude-sonnet-5-5`, `claude-haiku-4-5` |
| Google (Gemini) | `https://generativelanguage.googleapis.com/v1beta` — its own API; `…/v1beta/openai` is its OpenAI form | `gemini-3.5-flash-lite` |
| DeepSeek | `https://api.deepseek.com/v1` | `deepseek-flash`, `deepseek-v4-pro` |

None of this has to be typed: **Tools → Set up grammar checking → Cloud service** lists the four, asks for the key
(saying where it is got), checks it, and lists the service's models with the recommended one first, as the installer
and the plugin do. The model can be changed later in **Tools → Grammar model**.

Each answered and found the error in an English and a Russian paragraph through the editor's own code on 23 October
2026, in 1 to 2.5 s (`CloudCheck`, `docs/tech/proofing-internals.md`); how well each does over many texts was measured by
the plugin ([its docs/servers.md](https://github.com/loglux/obsidian-blueproof/blob/main/docs/servers.md)). Any other service with an API like OpenAI's may work too; what it refuses, the
editor learns from its answers. **Tools → Set up grammar checking → Another model server** checks the server before
keeping it (its list of models: whether it answers, takes the key, has the model) and says what is wrong, keeping it
anyway if asked to. With no model named (the installer names none for Ollama, LM Studio and llama-server) the editor
takes the one the server lists first; **Tools → Grammar model** chooses another. A key file of several lines (two keys)
gives its first line. A key refused is said
as such; at a service's limit of requests the status line says until when, nothing is sent till then, and the
paragraphs are checked after.

**Tools → Grammar model…** then lists the server's models under the `.gguf` files here; choosing one sets `llmModel`
to `"server"`. With a model here chosen (or Auto), the server is used only when llama.cpp or a model is missing here or
llama.cpp does not start, and the status line says so. Grammar and style both go there.

- **Privacy**: an address outside this computer and the local network (not `127.x`, `10.x`, `192.168.x`, `172.16–31.x`,
  a bare name or `.local`) means the paragraphs leave the computer; the status line says where when it starts.
- What a server will not take, the editor learns from its own answer (`400 Unknown parameter: '…'`) and leaves out:
  llama.cpp's `chat_template_kwargs` (OpenAI refuses it), `temperature` (refused by reasoning models), `max_tokens`
  (sent as `max_completion_tokens` where wanted). Thinking is switched off with `reasoning_effort: "none"`: through
  Ollama, Qwen spent the whole answer limit thinking and returned nothing without it (found by the Obsidian plugin).
- **The key**: one typed in **Tools → Set up grammar checking** (Cloud service or Another model server) goes to a file of its own,
  `llm-key` beside `settings.json`, which then says only `"file:llm-key"`: the key is not on screen when the settings
  are opened, nor in a copy of them. On Windows the file is encrypted with DPAPI, the system's own means for saved
  passwords: only this user on this computer can read it. On Linux it is plain text that only this user may read
  (mode 600). `env:NAME` and a key written into the settings work as before; a key read from a file with a byte order
  mark (Notepad) is trimmed of it.
- The model that answered names the findings (llama-server answers with its own model whatever name is asked for).
- **Ollama's first load can take minutes**, and Ollama gives up loading when the request waiting for it is closed;
  the editor waits 5 minutes for an answer (a style suggestion holds the screen while it waits). Measured on 6 October
  2026: the first request to `qwen3.5:9b` took 6 min 45 s, the next ones about a second. Load a new model once by hand
  (`ollama run qwen3.5:9b`) before using it here.

Measured through the editor (`ProofBench`, the British academic set) on 6 October 2026: **qwen3.5:9b through Ollama
here** found 24 of 24 grammar errors, 2 of 3 punctuation, 7 of 8 spelling, with one false alarm ("learnt" → "learned"),
0.96 s per sentence — the most grammar errors of any model on this set (Gemma 3 12B in llama.cpp: 23, no false alarm,
1.1 s). The same weights through Ollama and llama.cpp were not compared: Ollama's `qwen2.5` did not load in this
llama.cpp build (it stopped while reading the vocabulary).

### Edge's proofreader (experimental)

Microsoft Edge's built-in proofreader: a small model (Aion) running on your computer, English only. It is not
officially on yet: the editor starts a hidden Edge Canary with a profile of its own and the hidden switches that turn
it on, and hands it paragraphs. Your usual Edge and its profile are not touched. Findings are signed "Edge".

Needs [Edge Canary](https://www.microsoft.com/edge/download/insider) (or another Edge that has the proofreader: the
path of its `msedge.exe` in `edgePath`). Edge downloads the model (~1.2 GB) itself on first use, into
`%LOCALAPPDATA%\BlueEdit\edge-profile`; meanwhile the status line says `starting`.

It follows American rules: British spellings (organised, centre) count as errors for it. In the benchmark it is
behind YandexGPT and Gemma, but it is kept in case Microsoft improves it. An unofficial way in: an Edge update may
break it, and the status line then says `GRAMMAR … failed`.

Microsoft's requirements for its model: a GPU with 5.5 GB of video memory or more, at least 20 GB free on the drive
with the profile (below 10 GB Edge deletes the model), an unmetered connection for the first download. Whether the
computer qualifies shows in Edge Canary at `edge://on-device-internals` (Device performance class High or above).

### Chrome's proofreader

Chrome's built-in proofreader: a small model (117 MB) made for this one task, English only. BlueEdit drives a hidden
Chrome with a profile of its own, the way it drives Edge; it needs the proofreader's flags on in your Chrome (how:
[proofing-setup.md](proofing-setup.md#chromes-proofreader)). Findings are signed "Chrome". It drops the full stop at the
end of a sentence it otherwise leaves alone; the editor puts it back. On the British academic set: 21 of 24 grammar
errors, 7 false alarms of 24 (the British "committee have", a dropped "had", capitals), 0.7 s a sentence — far ahead of
LanguageTool and Harper, behind the models, much quieter than Edge. Chrome picks among its likeliest answers at
random: two runs of the set differed in 6 of 59 sentences (4 and 7 false alarms).

**The same model without Chrome** ("chromemodel", **Chrome model** in the list): Google's LiteRT-LM runs the model file
Chrome downloaded — no browser, no flags. Install with `install.ps1 -ChromeModel` (Python 3.10+ needed; it makes a
virtual environment in `%LOCALAPPDATA%\BlueEdit\litert` and installs `litert-lm` from PyPI). The model is copied from your
Chrome when the checker starts, so Chrome must have downloaded it once. On the British academic set: 17 of 24 grammar
errors, 2 false alarms (both "the committee have" made singular), 0.3 s a sentence on the CPU — fewer finds than Chrome
itself (21), far fewer false alarms, the same text always the same answer. No GPU needed: for English on a computer
without one it finds twice what LanguageTool does and three times Harper.

### Harper

[Harper](https://github.com/Automattic/harper) (Automattic, open source): hand-written rules for English, no model and
no GPU, answers in tens of milliseconds. It follows the dialect of the text's language: for `en-GB` British (colour,
organise, programme are right), for `en-US` American, also Australian and Canadian. Findings are signed "Harper",
each with its fixes. Its own spell check is off: spelling is the spelling provider's job, as with LanguageTool.

Needs `harper-ls` (Harper's language server): unpack `harper-ls-x86_64-pc-windows-msvc.zip` from its
[releases](https://github.com/Automattic/harper/releases) into `%LOCALAPPDATA%\BlueEdit\harper` (on Linux the
`…-unknown-linux-gnu.tar.gz` into `~/.local/share/BlueEdit/harper`), or have `harper-ls` on PATH. English only.

### Which to choose

- **A model** (YandexGPT 5 Lite or Gemma 3 12B): finds the most, with fixes right away; needs a GPU. The choice for
  most writing.
- **LanguageTool**: for a computer without a GPU; finds two to six times less than a model in every set measured
  (Russian 3 of 18 against 18), but without false alarms, and in some 30 languages.
- **Harper**: the same for English only, faster still.
- **Edge's proofreader**: for American English.
- One checker at a time is the default: in Tools > Grammar checking the checkers have round marks, `(•)`, and
  choosing one switches the others off. Its last line, **Several at once** (`grammarTogether`), allows more: the
  marks become boxes, `[x]`, and the findings are shown together.

What LanguageTool adds next to a model (measured on 6 October 2026 from the runs below, the current prompt): on
the British academic set nothing — all 8 errors it found, YandexGPT and Gemma 3 12B found too; on the formula sets
nothing; on the 69 sentences one error YandexGPT missed ("Who's book") and three Gemma 3 12B missed ("одела пальто",
"If I would have known", "However I"). So it is mostly a second opinion on what the model already found, now and then
an extra find, never a false alarm. Where both find the same place, the editor stops there once (Shift+F7) and F7 shows
both: the model's fix and LanguageTool's explanation, one Ignore for both. Running it costs 0.7–1 GB of memory and no
GPU. The Obsidian plugin chose one checker at a time; here both together stay possible, the model alone is enough.

Code, code blocks and tables are not grammar-checked; markup, addresses and paths are hidden from the checkers.

**LaTeX** (`.tex`) is checked the same way since 6 October 2026. Text: the body after `\begin{document}`;
`\emph{…}`, `\textbf{…}`, `\section{…}`, `\footnote{…}`, `\item` keep their text, the commands and braces are hidden;
`\cite{…}`, `\ref{…}`, `\eqref{…}`, `\label{…}` count as formulas (a finding touching one is dropped); `%` comments
are hidden; `\%` is a per cent sign. Not text: the preamble, `figure`, `table`, `tabular`, `verbatim`, `lstlisting` and
the like. Formulas: `$…$`, `\(…\)`, `\[…\]`, and display environments (`equation`, `align`, `gather`… with `*` too),
which belong to the sentence around them as `$$` blocks do in Markdown: "We define $f$ by \begin{equation}…
\end{equation} which **are** continuous" is one sentence. A heading (`\section{…}`) is a paragraph of its own. Style
suggestions do not offer a change that would drop a LaTeX command. Measured on `tools/proofbench/cases/tex-en.json`
(11 errors, 8 correct paragraphs full of LaTeX): YandexGPT 5 Lite found 10 with 1 false alarm (it rewrote
`\textbf{Proof.}`), Gemma 3 12B 10 with none, LanguageTool 4 with none. For a paragraph of a LaTeX document the
model is told to copy the LaTeX as it is; before that Gemma lost two errors by looping while it copied LaTeX into its
answer (`docs/tech/proofing-internals.md`).
**F7 → Ignore** on a grammar finding hides it until the editor is closed: that rule at that text, in every open file
("then" stays hidden wherever it stands, the same rule on other words is still shown). **F7 → Never show this** hides
the whole rule for good: the rule (or the fix) goes
into `ignoredRules` in the settings, for every checker; **Tools → Ignored rules…** lists them, untick one to see it
again. A rule you never want — say "Hence" → "Hence," (`SENT_START_CONJUNCTIVE_LINKING_ADVERB_COMMA`), common in
maths writing — is one F7 away.

**LanguageTool's own rules** are in the settings file: `languageToolDisabledRules`, `languageToolDisabledCategories`
(off), `languageToolEnabledRules` (rules that are off by default, on), `languageToolLevel` (`"default"` or `"picky"`,
more style rules). Its dictionary rules (`MORFOLOGIK_RULE_*`, `*_SPELLER_RULE`, `HUNSPELL_*`) are always left out:
spelling is the spelling provider's job. Off by default: `BRE_STYLE_OXFORD_SPELLING` (it flags correct British -ise)
and the rules about runs of spaces and sentence starts, because the editor blanks markup with spaces. The rest of the
`TYPOS` category stays on since 6 October 2026 (found by the Obsidian plugin): it also holds then/than, its/it's,
dont → don't. Measured: English sentences 14 → 19 of 25, the British academic set 8 → 9 of 24, formulas 5 → 7 of 20,
Russian unchanged, no new false alarm. A settings file that still lists the old default (`TYPOS` and the Oxford
spelling) is read as the new one. The rule ids are those LanguageTool shows (F7's message line); the full list is on
languagetool.org.

**Memory**: `languageToolMemory` (Java's heap), `"768m"` by default. Measured on 6 October 2026, 400 paragraphs:
Russian and English took 51 s at 512m and 52 s at 1g; five languages in turn (ru, en, de, fr, es) took 534 s at 512m
— the server spent its time collecting garbage — 202 s at 768m and 205 s at 1g. No run ran out of memory. The process
then holds about 0.7–1 GB.

### Comparison: which is more accurate

Measured on 4 October 2026 on 69 sentences, each checked on its own, with the editor's own prompt and settings, on
an RTX 4070 with 12 GB.

| Set | Grammar and punctuation errors | Typos | Correct sentences |
|---|---|---|---|
| Russian | 14 | 4 | 6 |
| English | 25 | 7 | 13 |

The correct sentences catch false alarms: there is nothing to fix in them.

| Checker | RU grammar (14) | RU typos (4) | RU false (6) | EN grammar (25) | EN typos (7) | EN false (13) | Answer | File |
|---|---|---|---|---|---|---|---|---|
| **YandexGPT 5 Lite 8B** | **14** | 4 | 0 | 20 | 6 | 1 | 1.0 s | 4.9 GB |
| **Gemma 3 12B** | 12 | 4 | 0 | **21** | 6 | 2 | 1.2 s | 7.3 GB |
| Qwen3 14B | 12 | 3 | 0 | 17 | 6 | 1 | 1.2 s | 9.0 GB |
| Qwen3 8B | 10 | 3 | 0 | 18 | 6 | 1 | 0.7 s | 5.0 GB |
| Gemma 3 4B | 11 | 4 | 0 | 17 | 6 | 1 | 0.8 s | 2.5 GB |
| Phi-4-mini | 6 | 4 | 0 | 16 | 5 | 1 | 0.3 s | 2.5 GB |
| Edge Proofreader | — | — | — | 19 | 5 | 6 | 0.4 s | 1.2 GB |
| LanguageTool | 3 (+3) | off | 0 | 14 (+1) | off | 0 | 0.05 s | — |

"(+3)": noticed the error, but without the right fix.

What it shows:

- **In Russian YandexGPT 5 Lite is the best**: all 14 errors, including the hard ones ("одела/надела", "ложит",
  "по приезду", "не разу") that trip the others. Its licence is Yandex's own; personal use is fine.
- **In English YandexGPT and Gemma 3 12B are nearly equal** (20 and 21 of 25) but miss different things. YandexGPT
  misses "Who's/Whose", "have seen … yesterday", "Me and my sister" and a comma splice ("I went to the shop, I
  bought"). Gemma misses commas ("However I…"), "If I would have known", "weather/whether" and the comma splice, and
  changes a correct "Whom" to "Who".
- **Qwen3 14B** is no better than Qwen3 8B while almost twice the size.
- **Small models** (Gemma 3 4B, Phi-4-mini) miss the hard cases, sometimes change the meaning (Phi-4-mini:
  "что-бы он сказал" → "что он сказал") and sometimes answer something else: Phi-4-mini translated three English
  sentences into Russian, Gemma 3 4B returned unrelated text. The editor drops such answers as rewrites, but the
  errors in those sentences then stay unfound.
- **Edge Proofreader** (the Aion model, English only) is mid-table on grammar (19 of 25) but follows American rules:
  4 of its 6 "false alarms" are British spellings changed to American (organised → organized, centre → center,
  travelled, apologise). For American English it would do noticeably better; for British it does not. The model is
  small (1.2 GB) but is only turned on by hidden switches in Edge Canary.
- **LanguageTool** found the least: in Russian it fixed 3 ("в течении", "одела", "не смотря") and noticed 3 more
  without the right fix ("Я пошли", "Мы был", "не разу"); in English 14 of 25. But it raised no false alarm and
  answers fastest. In the editor its typo rules are off: the Windows or Office dictionary does that.
- **"The committee have agreed"** is changed to "has" by every model and Edge; British English allows both, so it
  is a borderline case. **"analyze"** (British: "analyse") was fixed by none; the en-GB dictionary catches it.

**In short:** for Russian YandexGPT 5 Lite, for English YandexGPT or Gemma 3 12B. Together with LanguageTool
(explains what it finds, never wrong here) and a dictionary (typos) this is the fullest free setup.

The sample is small: take the results as a guide, not a final ranking. To repeat it: `python tools\proofbench\bench.py`
with the editor closed, Edge with `tools\proofbench\edge.py`; every checker's answer to every sentence is in
`tools\proofbench\results.json`.

### British academic English

Measured on 5 October 2026 on `tools/proofbench/cases/academic-en-gb.json`: 35 sentences with a typical error of
student writing (24 grammar, 3 punctuation, 8 spelling — American spellings and typos) and 24 correct British sentences
that tempt a checker tuned to American English ("The committee have agreed", "whilst", "learnt", "programme"). Each
checker through the editor's own code (`tests/BlueEdit.Tests/ProofBench.cs`), the models with the prompt that knows
British usage (below).

| Checker | Grammar (24) | Punctuation (3) | Spelling (8) | False alarms (24 correct) | Answer |
|---|---|---|---|---|---|
| **YandexGPT 5 Lite 8B** ⁵ | 23 | 0 | 6 | 1 | 0.9 s |
| **Gemma 3 12B** ⁵ | 24 | 2 | 7 | **0** | 1.1 s |
| Qwen3 14B ¹ | 23 | 0 | 7 | 0 | 1.2 s |
| Qwen3 8B ¹ | 21 | 0 | 7 | 0 | 0.6 s |
| Gemma 3 4B ² | 18 | 2 | 3 | 2 | 0.4 s |
| Phi-4-mini ² | 17 | 1 | 3 | 2 | 0.3 s |
| Edge Proofreader | 22 | 2 | 3 | **29** | 0.4 s |
| Chrome's proofreader ³ | 21 | 2 | 2 | 7 | 0.7 s |
| Chrome's model without Chrome ⁴ | 17 | 0 | 2 | 2 | 0.3 s |
| gpt-5.4-mini (OpenAI) ⁴ | 24 | 3 | 7 | 0 | 0.8 s |
| qwen3.5:9b (Ollama) ⁴ | 24 | 2 | 7 | 1 | 1.0 s |
| LanguageTool | 8 | 0 | off | 0 | 0.03 s |
| Harper | 6 | 0 | off | 0 | 0.03 s |
| spelling: Windows (en-GB) | — | — | 7 | 0 | — |
| spelling: Word (en-GB) | — | — | 7 | 0 | — |

¹ with an earlier wording of the British line; ² with the old prompt, before the British line; ³ measured on 6 October
2026, with the final full stop it drops put back; ⁴ 6 October 2026, the current prompt (the language-model ones
through a server at an address); ⁵ 6 October 2026, with the errors a model lists but leaves in its corrected text:
grammar 22 → 23 and 23 → 24, and YandexGPT's one false alarm is "The committee have agreed" → "has", which it
listed without changing its text.

What it shows:

- **The models find nearly all grammar errors**; the rule-based checkers find a third or less (LanguageTool 8,
  Harper 6), but never raise a false alarm and answer in milliseconds.
- **Edge Americanises**: 29 false alarms, nearly all British spellings and words changed to American ones (organised,
  behaviour, whilst, learnt, programme, centre). Not for British English.
- **Every model changed "The committee have agreed" and "The team were" to the singular** — correct British usage —
  until the prompt said so. With one line of British usage in the prompt the false alarms went from 2 to 0 for every
  model, and they also caught more American spellings. Four wordings were measured on this set, the formula sets and
  the sentence sets; the one kept names examples ("the committee have decided", "the team were", "the data are"):
  without examples YandexGPT ignored the rule. Its price: both models now miss "I am agree with you", and Gemma 3 12B
  lets "the equation … have no real roots" through in one formula case (18 → 16 of 20 there; YandexGPT 20 of 20).
  Neither Harper nor LanguageTool catches those two.
- **Spelling** is the dictionary's: Windows' and Word's en-GB dictionaries catch 7 of 8 (all but "programme" for
  software, which British English spells "program"), with no false alarm.

**For British academic writing:** YandexGPT 5 Lite or Gemma 3 12B, with the Windows (or Word) en-GB dictionary for
spelling; LanguageTool or Harper alongside costs nothing and adds no false alarms. To repeat:
`$env:BLUEEDIT_PROOF_BENCH='report.md'; dotnet test tests\BlueEdit.Tests --filter ProofBench` (docs/tech/proofing-internals.md, "Measuring").

## Style

**Ctrl+F7** (or **Tools → Style suggestions**) on a paragraph: the language model suggests how to say it better —
without officialese, filler and repetition, more clearly. This is not error correction but another wording, so
nothing is underlined and nothing changes by itself:

```
Style (YandexGPT 5 Lite): 2 suggestions
  [x] It should be noted that the re… → The results were good   — wordiness
  [x] In order to finish → To finish                             — wordiness
  ─────────────────────────────────────
  It should be noted that the results were good
  → The results were good
  (wordiness)
```

Under the list is the selected suggestion in full: was, becomes, why. **Space** ticks or unticks, **Enter** applies
the ticked ones, **Esc** changes nothing. What was applied is undone with one **Ctrl+Z**.

- Needs a language model, as for grammar: llama.cpp and a model here, or a model server or cloud service (see
  [proofing-setup.md](proofing-setup.md)); grammar checking need not
  be on. The model is the one chosen in **Tools → Grammar model…**, and it sits in video memory once for both. The
  first request starts it (a few seconds), then about a second per paragraph.
- **Tools → Style options…**: what to look for (ticks): officialese; wordiness and filler; repetition; overlong or
  tangled sentences; the passive where the active reads better; bookish words where plain ones will do; and, off
  by default, **informal words in formal writing (academic)** — "a lot of", "really", "got", "kind of", contractions —
  for essays and papers. None ticked means the defaults (all but the academic one). Kept as `styleFocus`.
- The model is told to keep the meaning, facts, names, numbers and terms, the author's voice and the language, to add
  nothing and not to summarise. If the paragraph is fine, the answer is "nothing to improve".
- The model is also told to keep the register (no contractions put into formal text) and, for British English, British
  spelling and usage (-ise, -our, whilst, "the committee have").
- Changes in one sentence come as one: a model that reorders a sentence changes several places that only make
  sense together, so they are taken or left together.
- Guards: in British English a change that only respells words the American way (organisation → organization,
  behaviour → behavior) is not offered — YandexGPT made such changes despite being told; an answer in another language is dropped with a message (with only "do not translate" the models rewrote
  Russian paragraphs in English — now the language is named to them); a change that would lose Markdown markup, a
  link or an address is not offered.
- YandexGPT edits boldly: «В целях осуществления контроля за исполнением данного поручения необходимо произвести
  проверку документации…» became «Нужно проверить документы…», losing the purpose — check the meaning stayed. Gemma 3
  12B is more careful. Small models (Phi-4-mini, Gemma 3 4B) do worse.

**Measured** on 5 October 2026 (`tests/BlueEdit.Tests/StyleBench.cs`) on 24 British academic paragraphs
(16 with 36 style problems, 8 that read well, some with formulas) and 10 Russian ones, with the default options:

| Model | EN problems touched (36) | EN meaning lost | EN good paragraphs changed (8) | RU problems (11) | RU lost | RU good changed (4) | Answer |
|---|---|---|---|---|---|---|---|
| **Gemma 3 12B** | 33 | 1 | 3 | 10 | 0 | 2 | 1.8 s |
| Qwen3 8B | 32 | 0 | 4 | 11 | 2 | 2 | 1.3 s |
| YandexGPT 5 Lite | 30 | 3 | 6 | 11 | 1 | 3 | 1.4 s |

"Meaning lost": taking every change drops a fact, term, formula or British spelling the case keeps. Before the
register and British lines (same sets): YandexGPT lost 5 English paragraphs' meaning and changed 7 good ones with 15
changes ("organisation analysed … behaviour" → American, "cannot" → "can't"), Gemma lost 2, Qwen3 8B 1; Russian stayed
the same. **For British English style Gemma 3 12B** (or Qwen3 8B) is the safer choice; YandexGPT still drops parts of
a sentence ("to answer this question", "with regard to funding") and changes "the committee have". With the academic
option on, every model also catches most informal words (11–12 of 13).

## Settings file

**Tools → Settings…** shows the everyday settings as a list; its last row, **Edit settings.json…**, opens `%APPDATA%\blueedit\settings.json` (on Linux
`~/.config/blueedit/settings.json`) with all of them; F2 saves and applies it at once, Esc goes back to the text.

```jsonc
"spellCheck": true,                     // spelling on/off (same as F8)
"spellLanguages": "auto",               // or ["ru-RU", "en-GB"]
"spellProvider": "windows",             // or "office", "hunspell"
"grammar": ["languagetool", "llm"],     // "off", "languagetool", "llm", "harper", "edge", "chrome", "chromemodel" or a list
"languageToolDir": "",                  // empty: %LOCALAPPDATA%\BlueEdit
"llmDir": "",                           // empty: %LOCALAPPDATA%\BlueEdit\llm
"llmModel": "",                         // empty: the best one installed (Tools → Grammar model…)
"edgePath": "",                         // empty: Edge Canary
"styleFocus": ["officialese", "wordiness", "repetition", "clarity", "active", "simple"]   // what style looks for; + "informal" for academic writing
```

Each setting should appear in the file only once.

## Checking that it works

Open a `.md` file with this text:

```
Превет, это проверка орфографии.
Я пошли домой вчера. Мы был в магазине и купили хлеб.
Он сказал что придёт завтра.
This sentense have two errors.
The results was good, and I recieved them yesterday.
```

What should happen:

1. **Превет**, **sentense**, **recieved** are underlined as typos; F7 shows the fixes as buttons in the window `Spelling: "sentense"`, `Windows spelling` at its bottom right.
2. **Я пошли**, **Мы был**, **сказал что**, **have**, **results was** are grammar, underlined in another colour after
   1–3 seconds (longer on first start). What is found depends on the checker: the model usually finds all of them,
   LanguageTool some, not always with a fix.
3. Shift+F7 walks through all problems in order; the status line shows who found each.

If grammar stays silent, look at the status line: `GRAMMAR … failed` means a checker did not start (usually missing
files); **Tools → Grammar checking…** shows what is missing.
