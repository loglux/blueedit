"""Proofreading benchmark: local models (llama.cpp, the editor's own prompt) and LanguageTool (the editor's settings).

Each test: (lang, kind, text, expected). kind: "gram" (grammar/punctuation/word choice) or "typo"; expected is a list of
fragments that must all be in the corrected text ("a|b" = either), or None for a correct sentence that must stay as it is.

Run:  python tools\proofbench\bench.py                     all models present and LanguageTool
      python tools\proofbench\bench.py "Gemma 3 12B"       one; results are kept in results.json next to this file
Needs the editor closed (the GPU free) and the checkers installed as in docs/proofing.md.
"""
import json, os, re, statistics, subprocess, sys, time, urllib.parse, urllib.request

HOME = os.path.join(os.environ["LOCALAPPDATA"], "BlueEdit")
LLM = os.path.join(HOME, "llm")
SERVER = os.path.join(LLM, "bin", "llama-server.exe")
OUT = os.path.join(os.path.dirname(__file__), "results.json")

TESTS = [
    ("ru", "gram", "Я пошли домой вчера.", ["пошел|пошла"]),
    ("ru", "gram", "Мы был в магазине и купили хлеб.", ["Мы были"]),
    ("ru", "gram", "Он сказал что придёт завтра.", ["сказал, что"]),
    ("ru", "gram", "В течении недели мы работали без выходных.", ["В течение"]),
    ("ru", "gram", "Она одела пальто и вышла на улицу.", ["надела"]),
    ("ru", "gram", "Мне нравится эти книги.", ["нравятся"]),
    ("ru", "gram", "Директор попросил подготовить отчёт к пятнице, не смотря на праздники.", ["несмотря"]),
    ("ru", "gram", "Я не знаю, что-бы он сказал на это.", ["что бы"]),
    ("ru", "gram", "Дети играли во дворе пока родители готовили ужин.", ["во дворе, пока"]),
    ("ru", "gram", "По приезду в город мы сразу пошли в гостиницу.", ["По приезде"]),
    ("ru", "gram", "Он ложит книги на стол.", ["кладет"]),
    ("ru", "gram", "Компания, которая выпустили этот продукт, находится в Москве.", ["которая выпустила"]),
    ("ru", "gram", "Он не разу не был в Париже.", ["ни разу"]),
    ("ru", "gram", "Я хотел бы поблагодарить вас за помощь которую вы оказали.", ["помощь, которую"]),
    ("ru", "typo", "Надеюсь, вы прийдёте вовремя.", ["придете"]),
    ("ru", "typo", "Это решение было принятно руководством.", ["принято"]),
    ("ru", "typo", "Мы обсудили все детали проэкта.", ["проекта"]),
    ("ru", "typo", "Учитель обьяснил новую тему.", ["объяснил"]),
    ("ru", "ok", "Вчера мы долго гуляли по парку и разговаривали о книгах.", None),
    ("ru", "ok", "Если будет время, я обязательно позвоню.", None),
    ("ru", "ok", "Несмотря на дождь, мы пошли гулять.", None),
    ("ru", "ok", "Она надела шапку, потому что на улице было холодно.", None),
    ("ru", "ok", "Книга, которую ты мне дал, оказалась интересной.", None),
    ("ru", "ok", "Ни один из нас не знал ответа.", None),
    ("en", "gram", "He go to school every days.", ["goes", "every day."]),
    ("en", "gram", "Their going to the park tomorrow, weather or not it rains.", ["They're|They are", "whether"]),
    ("en", "gram", "The results was better then we expected.", ["were", "than"]),
    ("en", "gram", "Its a nice day, isn't it?", ["It's|It is"]),
    ("en", "gram", "She don't like coffee.", ["doesn't|does not"]),
    ("en", "gram", "Between you and I, the plan is risky.", ["you and me"]),
    ("en", "gram", "The team finished there work early.", ["their work"]),
    ("en", "gram", "Who's book is this?", ["Whose"]),
    ("en", "gram", "I have seen him yesterday.", ["saw him"]),
    ("en", "gram", "She is married with a doctor.", ["married to"]),
    ("en", "gram", "Each of the students have a laptop.", ["has a laptop"]),
    ("en", "gram", "I am agree with you.", ["I agree"]),
    ("en", "gram", "He is more taller than his brother.", ["is taller"]),
    ("en", "gram", "There is many reasons to stay.", ["There are"]),
    ("en", "gram", "I look forward to hear from you.", ["to hearing"]),
    ("en", "gram", "The dog wagged it's tail.", ["its tail"]),
    ("en", "gram", "She suggested me to go home.", ["suggested that I|suggested I|suggested going|suggested my going"]),
    ("en", "gram", "I didn't saw anything.", ["didn't see|did not see"]),
    ("en", "gram", "He has went to the shop.", ["has gone"]),
    ("en", "gram", "Less people came than expected.", ["Fewer people"]),
    ("en", "gram", "If I would have known, I would have come.", ["If I had known"]),
    ("en", "gram", "Me and my sister went to the cinema.", ["and I went"]),
    ("en", "gram", "However I disagree with the decision.", ["However, I"]),
    ("en", "gram", "I went to the shop, I bought some milk.", ["shop and I|shop and bought|shop; I|shop. I|shop, and I|shop, where I"]),
    ("en", "gram", "The informations you sent was useful.", ["information you", "was useful"]),
    ("en", "typo", "I have recieved your letter.", ["received"]),
    ("en", "typo", "I like the color of this house.", ["colour"]),
    ("en", "typo", "We need to analyze the data.", ["analyse"]),
    ("en", "typo", "The goverment announced new rules.", ["government"]),
    ("en", "typo", "It was definately a good idea.", ["definitely"]),
    ("en", "typo", "We will meet tommorow.", ["tomorrow"]),
    ("en", "typo", "This is a seperate issue.", ["separate"]),
    ("en", "ok", "This sentence is perfectly fine.", None),
    ("en", "ok", "We organised the meeting for Tuesday afternoon.", None),
    ("en", "ok", "The committee have agreed on the budget.", None),
    ("en", "ok", "If I were you, I would apologise.", None),
    ("en", "ok", "Fewer people came than we expected.", None),
    ("en", "ok", "She has lived in London since 2010.", None),
    ("en", "ok", "I'd rather stay at home tonight.", None),
    ("en", "ok", "The data are stored on a secure server.", None),
    ("en", "ok", "Neither the manager nor the staff were informed.", None),
    ("en", "ok", "He travelled to Manchester last week.", None),
    ("en", "ok", "Whom did you invite to the party?", None),
    ("en", "ok", "I've got a new job, which I really enjoy.", None),
    ("en", "ok", "The centre will open at nine o'clock.", None),
]

# the editor's prompt and schema (LlmGrammar.cs)
INSTRUCTIONS = (
    "You are a careful proofreader for Russian and British English (en-GB spelling). "
    "British usage is correct and must stay: a plural verb after a collective noun or data (\"the committee have "
    "decided\", \"the team were\", \"the data are\"), and -ise, -our, -re, travelled, learnt, whilst, programme are British spellings. "
    "Fix only spelling, grammar and punctuation errors in the user's text. Do not rephrase, do not change style, "
    "do not change anything that is correct, keep line breaks. Answer only with JSON: "
    "{\"corrected\": \"<the whole text, corrected>\", \"errors\": [{\"original\": \"<exact wrong fragment>\", "
    "\"replacement\": \"<fix>\", \"alternatives\": [\"<other correct fixes, if the text allows several>\"], "
    "\"explanation\": \"<short reason>\"}]}. "
    "Write every explanation in the language of the text it is about: Russian for Russian text, English for English text. "
    "When more than one fix is correct (for example the speaker's gender is unknown: \"я пошёл\" or \"я пошла\"), "
    "put the most likely one in replacement and the others in alternatives. "
    "If there are no errors, return the text unchanged and an empty errors list."
)
SCHEMA = json.loads("""{"type":"object","properties":{"corrected":{"type":"string"},"errors":{"type":"array","items":{"type":"object",
"properties":{"original":{"type":"string"},"replacement":{"type":"string"},"alternatives":{"type":"array","items":{"type":"string"}},
"explanation":{"type":"string"}},"required":["original","replacement","explanation"]}}},"required":["corrected","errors"]}""")

MODELS = {
    "Gemma 3 4B": "google_gemma-3-4b-it-Q4_K_M.gguf",
    "Phi-4-mini": "microsoft_Phi-4-mini-instruct-Q4_K_M.gguf",
    "Qwen3 8B": "Qwen3-8B-Q4_K_M.gguf",
    "YandexGPT 5 Lite 8B": "YandexGPT-5-Lite-8B-instruct-Q4_K_M.gguf",
    "Gemma 3 12B": "google_gemma-3-12b-it-Q4_K_M.gguf",
    "Qwen3 14B": "Qwen_Qwen3-14B-Q4_K_M.gguf",
}


def norm(s):
    return re.sub(r"\s+", " ", s.replace("ё", "е").replace("Ё", "Е").replace("’", "'")).strip()


def score(text, expected, corrected):
    if corrected is None:
        return "fail"
    c = norm(corrected)
    if expected is None:
        return "ok" if c == norm(text) else "false"
    return "fixed" if all(any(norm(alt) in c for alt in e.split("|")) for e in expected) else "missed"


def post(url, payload, data=None, timeout=180):
    body = data if data is not None else json.dumps(payload).encode()
    hdr = {} if data is not None else {"Content-Type": "application/json"}
    with urllib.request.urlopen(urllib.request.Request(url, data=body, headers=hdr), timeout=timeout) as r:
        return json.loads(r.read())


def run_model(file):
    path = os.path.join(LLM, "models", file)
    if not os.path.exists(path):
        return {"error": "missing"}
    port = 8093
    proc = subprocess.Popen([SERVER, "-m", path, "-ngl", "99", "-c", "4096", "--port", str(port), "--host", "127.0.0.1"],
                            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, creationflags=0x08000000)
    try:
        t0 = time.time()
        while time.time() - t0 < 300:
            try:
                urllib.request.urlopen(f"http://127.0.0.1:{port}/health", timeout=2); break
            except Exception:
                time.sleep(0.5)
        load = time.time() - t0
        rows = []
        for lang, kind, text, expected in TESTS:
            body = {"messages": [{"role": "system", "content": INSTRUCTIONS}, {"role": "user", "content": text}],
                    "temperature": 0, "max_tokens": 200 + len(text) * 2,
                    "response_format": {"type": "json_schema", "json_schema": {"name": "proofread", "schema": SCHEMA}},
                    "chat_template_kwargs": {"enable_thinking": False}}
            s = time.time()
            try:
                ans = json.loads(post(f"http://127.0.0.1:{port}/v1/chat/completions", body)["choices"][0]["message"]["content"])
                corrected, errors = ans.get("corrected"), ans.get("errors", [])
            except Exception as e:
                corrected, errors = None, [{"failure": str(e)}]
            rows.append({"lang": lang, "kind": kind, "text": text, "corrected": corrected, "errors": errors,
                         "ms": round((time.time() - s) * 1000), "result": score(text, expected, corrected)})
        return {"load_s": round(load, 1), "rows": rows}
    finally:
        proc.kill()


def run_languagetool():
    java = os.path.join(HOME, "jre", "bin", "java.exe")
    port = 8094
    proc = subprocess.Popen([java, "-Xmx768m", "-cp", "languagetool-server.jar", "org.languagetool.server.HTTPServer", "--port", str(port)],
                            cwd=os.path.join(HOME, "languagetool"), stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, creationflags=0x08000000)
    try:
        rows = []
        for lang, kind, text, expected in TESTS:
            # as the editor asks (Grammar.cs); typos are left to the spelling provider there, but counted here too
            form = {"text": text, "language": "ru-RU" if lang == "ru" else "en-GB",
                    "disabledCategories": "BRE_STYLE_OXFORD_SPELLING",
                    "disabledRules": "WHITESPACE_RULE,COMMA_PARENTHESIS_WHITESPACE,UPPERCASE_SENTENCE_START"}
            res = None
            for _ in range(240):
                try:
                    s = time.time()
                    res = post(f"http://127.0.0.1:{port}/v2/check", None, data=urllib.parse.urlencode(form).encode(), timeout=30)
                    break
                except Exception:
                    time.sleep(0.5)
            ms = round((time.time() - s) * 1000)
            corrected, shift, flagged = text, 0, []
            for m in sorted(res["matches"], key=lambda m: m["offset"]):
                rule = m["rule"]["id"]
                if rule.startswith("MORFOLOGIK_RULE_") or rule.endswith("SPELLER_RULE") or rule.startswith("HUNSPELL_"):
                    continue                     # the dictionary: the editor leaves it out (LanguageToolOptions.IsDictionaryRule)
                flagged.append({"original": text[m["offset"]:m["offset"] + m["length"]], "message": m["message"],
                                "replacements": [r["value"] for r in m["replacements"][:3]]})
                if m["replacements"]:
                    a, b = m["offset"] + shift, m["offset"] + m["length"] + shift
                    corrected = corrected[:a] + m["replacements"][0]["value"] + corrected[b:]
                    shift += len(m["replacements"][0]["value"]) - m["length"]
            r = score(text, expected, corrected)
            if r == "missed" and flagged:
                r = "flagged"          # noticed, but no (right) fix
            if expected is None and r == "false" or expected is None and flagged:
                r = "false"
            rows.append({"lang": lang, "kind": kind, "text": text, "corrected": corrected, "errors": flagged, "ms": ms, "result": r})
        return {"rows": rows}
    finally:
        proc.kill()


def summary(results):
    lines = []
    for name, res in results.items():
        if "rows" not in res:
            lines.append(f"{name}: {res}"); continue
        parts = []
        for lang in ("ru", "en"):
            rows = [r for r in res["rows"] if r["lang"] == lang]
            def count(kind, what): return sum(1 for r in rows if r["kind"] == kind and r["result"] in what)
            def total(kind): return sum(1 for r in rows if r["kind"] == kind)
            parts.append(f"{lang}: grammar {count('gram', {'fixed'}):2}/{total('gram')} (+{count('gram', {'flagged'})})"
                         f" typos {count('typo', {'fixed'})}/{total('typo')} false {count('ok', {'false'})}/{total('ok')}")
        ms = statistics.median(r["ms"] for r in res["rows"])
        lines.append(f"{name:20} " + " | ".join(parts) + f" | median {ms} ms")
    return "\n".join(lines)


if __name__ == "__main__":
    which = sys.argv[1:] or list(MODELS) + ["LanguageTool"]
    results = json.load(open(OUT, encoding="utf-8")) if os.path.exists(OUT) else {}
    for name in which:
        print("running", name, flush=True)
        results[name] = run_languagetool() if name == "LanguageTool" else run_model(MODELS[name])
        json.dump(results, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print(summary(results))
