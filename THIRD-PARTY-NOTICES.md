# Third-party software and data

BlueEdit itself is under the MIT licence ([LICENSE](LICENSE)). These parts of other authors are built into it or into
its optional formula pictures module, each under its own licence:

| What | Where in BlueEdit | Licence | From |
|---|---|---|---|
| .NET runtime and libraries | inside `be.exe` / `be` (a self-contained build) | MIT, © .NET Foundation and contributors | https://github.com/dotnet/runtime |
| WeCantSpell.Hunspell 7.0.1 (a port of Hunspell) | the Hunspell spelling provider | MPL 1.1 / GPL 2.0 / LGPL 2.1, as Hunspell; used under MPL 1.1, not changed | https://github.com/aarondandy/WeCantSpell.Hunspell |
| fastText language identification model `lid.176.ftz` | which language a paragraph is in, built into `be.exe` | CC BY-SA 3.0, by Facebook (Meta); not changed ([models/README.md](models/README.md)) | https://fasttext.cc/docs/en/language-identification.html |
| CSharpMath 1.0.0-pre.2 | the formula pictures module (`src/MathImages`) | MIT, © CSharpMath contributors | https://github.com/verybadcat/CSharpMath |
| SkiaSharp 3.119.4 and Skia | the formula pictures module | MIT (SkiaSharp), BSD 3-Clause (Skia), © Microsoft, Google | https://github.com/mono/SkiaSharp |

Not built in: the spelling dictionaries, LanguageTool, Java, Harper, llama.cpp, language models and language servers.
The editor downloads them only when asked to (**Tools** menu, `be --install-proofing`, `be --install-tools`), from
their own publishers, under their own licences (`be --install-proofing` lists each model's).
