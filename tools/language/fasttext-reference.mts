// The answers of fastText itself (the WebAssembly build the Obsidian plugin BlueProof runs) for the plugin's language
// test texts (Tatoeba sentences, CC BY 2.0 FR, and its own), to check BlueEdit's FastText.cs against:
//   node tools/language/fasttext-reference.mts <the plugin's folder> <out.json>
//   BLUEEDIT_FASTTEXT_REF=<out.json> dotnet test tests/BlueEdit.Tests --filter FastTextTests
// Needs Node 23 or newer (runs TypeScript as it is) and the plugin with its node_modules.
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

const [plugin, out] = process.argv.slice(2);
Object.assign(globalThis, { window: globalThis });
const { loadFastText } = await import(pathToFileURL(join(plugin, 'src/prose/fasttext.ts')).href);
const dir = join(plugin, 'node_modules/fasttext.wasm.js');
const wasm = readFileSync(join(dir, 'dist/core/fastText.common.wasm'));
const model = readFileSync(join(dir, 'dist/models/language-identification/assets/lid.176.ftz'));
const lm = await loadFastText(new Uint8Array(wasm), new Uint8Array(model), globalThis);

const texts: string[] = [];
const walk = (v: unknown): void => {
	if (typeof v === 'string') { if (v.length > 0) texts.push(v); }
	else if (Array.isArray(v)) v.forEach(walk);
	else if (v && typeof v === 'object') for (const [k, x] of Object.entries(v)) if (k === 'text' || k === 'texts' || k === 'cases' || k === 'items' || k === 'sentences' || typeof x === 'object') walk(x);
};
for (const f of ['tatoeba.json', 'sentences.json']) walk(JSON.parse(readFileSync(join(plugin, 'test/fixtures/language', f), 'utf8')));
texts.push('', '   ', 'a', 'x = 5', 'Привет, мир!', 'Ёлка и объём', 'naïve café résumé', '日本語のテキスト', 'line one\nline two');
const result = texts.map((t) => ({ text: t, top: lm.predict(t, 5) }));
writeFileSync(out, JSON.stringify(result, null, 0));
console.log(texts.length, 'texts');
