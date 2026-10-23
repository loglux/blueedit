# A new interface language

BlueEdit's interface is English with a table per other language: `lang/<code>.json` (English string → translation)
and the F1 help as a whole text, `lang/help.<code>.txt`. This folder is where a new one starts:

| File | What |
|---|---|
| `strings.json` | every string of the interface, the translations empty |
| [`../help.en.txt`](../help.en.txt) | the F1 help in English, the text to translate (`../help.ru.txt` is the Russian one) |

To try one on your own computer, without building anything:

1. Copy `strings.json` to `lang/<code>.json` in BlueEdit's settings folder (`%APPDATA%\blueedit` on Windows,
   `~/.config/blueedit` on Linux), the code as the system names the language: `de`, `pt-br`, `uk`.
2. Write the translations between the empty quotes. An empty one stays English: the part done shows at once.
3. Optionally, translate `lang/help.en.txt` into `lang/help.<code>.txt` beside it. Lines stay within 86 characters.
4. **Tools → Settings… → Interface language**, or `"uiLanguage": "<code>"` in `settings.json` (`"auto"` takes the
   system's).

Rules the tests hold a built-in table to (`tests/BlueEdit.Tests/UiTests.cs`):

- `{0}`, `{1}` are values put in: keep each, anywhere in the sentence. A translation with them wrong shows English.
- An underscore marks a menu's letter (`"_File"`): one per menu label and item, on a letter no other item of that menu
  has. The menu bar's letters must not be keys the editor uses with Alt: C D G H I L M N Q R X Z.
- A count's forms are split by `|`: two as English (`{0} file|{0} files`: one, other), or three as Russian, Ukrainian
  and Belarusian (one, few, many: 21 is one, 2–4 few, 11–14 many; `Ui.N`). Three forms always follow that rule, so a
  language that counts otherwise (Polish, where 21 is many; Arabic, with six forms) is not handled right yet.
- `"context|English"` keys translate a word where it reads differently (`"hint|Save"`: the short word under F2).

To build it in: put the files in this repository's `lang/` folder (not in `template/`) and send a pull request;
`BlueEdit.csproj` embeds every `lang/*.json` and `lang/help.*.txt`. `tasks lang-template` writes `strings.json` anew
when the interface's strings change (a unit test fails until it is).
