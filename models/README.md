# Models built into the editor

## lid.176.ftz

fastText's language identification model: 176 languages, about 0.9 MB, the compressed (`.ftz`) version. BlueEdit
uses it to tell which language a paragraph is in (`FastText.cs`, `LanguageDetect.cs`); it is built into `be.exe`
as a resource.

- From: https://fasttext.cc/docs/en/language-identification.html, downloaded on 12 October 2026 from
  `https://dl.fbaipublicfiles.com/fasttext/supervised-models/lid.176.ftz`.
- SHA-256: `8f3472cfe8738a7b6099e8e999c3cbfae0dcd15696aac7d7738a8039db603e83` (the same file the Obsidian plugin
  BlueProof bundles through the `fasttext.wasm.js` package).
- Licence: [Creative Commons Attribution-Share-Alike 3.0](https://creativecommons.org/licenses/by-sa/3.0/), by
  Facebook (Meta). Not changed.
- Papers: A. Joulin, E. Grave, P. Bojanowski, T. Mikolov, *Bag of Tricks for Efficient Text Classification* (2016);
  A. Joulin, E. Grave, P. Bojanowski, M. Douze, H. Jégou, T. Mikolov, *FastText.zip: Compressing text classification
  models* (2016).
