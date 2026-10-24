# Исследование: встроенный ИИ Edge и Chrome, проверка Word и Windows

*In English: [browser-ai-research.md](browser-ai-research.md)*

Записи исследования, сделанного для проверки орфографии и грамматики в BlueEdit (октябрь 2026, тестовая машина:
Windows 11, RTX 4070 12 ГБ, 64 ГБ памяти). Всё наблюдалось на этой машине, если не сказано иное; версии важны — эти функции
меняются от сборки к сборке. Что из этого вошло в редактор — в [proofing-internals.md](proofing-internals.md)
(на английском) и в руководстве [../proofing.ru.md](../proofing.ru.md).

Метод: только то, что программа показывает о себе сама, — страницы браузера, протокол DevTools, строки в файлах
программы, файлы настроек, которые браузер скачал. Без дизассемблирования и без правки программ.

## 1. Proofreader API в Edge

### Что это

Веб-API (`Proofreader`) из черновика стандарта группы W3C Web Machine Learning
([спецификация](https://webmachinelearning.github.io/proofreader-api/)): проверяет текст маленькой языковой
моделью на самом компьютере. Microsoft описывает его как экспериментальную предварительную версию для разработчиков в
Edge Canary и Dev с версии 142 ([документация](https://learn.microsoft.com/en-us/microsoft-edge/web-platform/proofreader-api)).

### Версии на машине

| | Версия | Proofreader |
|---|---|---|
| Edge (обычный) | 154.0.4258.48 | с ключами ниже объект есть, но `availability()` не отвечает (таймаут 20 с) |
| Edge Canary | 157.0.4315.0 | с ключами ниже работает |

### Как включить: документация и действительность

По документации Microsoft: включить флаг **«Proofreader API for Phi mini»** в `edge://flags` и перезапустить. В Canary
157 такого флага нет. `edge://flags` (страница из веб-компонентов: обычное извлечение текста ничего не видит, список
флагов — внутри shadow root) показывает только такие флаги встроенного ИИ:

- `edge-llm-prompt-api-for-phi-mini`, `edge-llm-summarization-api-for-phi-mini`, `edge-llm-writer-api-for-phi-mini`,
  `edge-llm-rewriter-api-for-phi-mini`, `edge-llm-on-device-model-debug-logs`
- только в Canary: `edge-llm-on-device-model-performance-param`, `edge-llm-on-device-model-prerelease`,
  `edge-on-device-speech-recognition`

(Флаги можно включать и без интерфейса: `browser.enabled_labs_experiments` в файле `Local State` профиля, например
`"edge-llm-prompt-api-for-phi-mini@1"`.) С ними в Canary появились `LanguageModel`, `Rewriter`, `Summarizer`,
`LanguageDetector`, `Translator` — а `Proofreader` остался `undefined`. Угаданные имена
(`AIProofreaderAPIForPhiMini`, `EdgeLLMProofreaderAPIForPhiMini`, флаг `edge-proofreader-api@1`) не помогли.

Имена нашлись в самой программе: в `msedge.dll` Canary (338 МБ) 73 разные строки со словом «proofread», среди них
`AIProofreaderAPI`, `AIProofreadingAPI`, `kAIProofreadingAPI`, `kProofreaderApi`, `edge-proofreader-api`,
`third_party\blink\renderer\modules\ai\proofreader.cc`, `blink.mojom.AIManagerCreateProofreaderClient` и текст флага
«Enables the Proofreader API, allowing you to proofread a piece of text with the on-device small language model in
Microsoft Edge…». Включить нужно две вещи — функцию браузера и функцию движка страниц (Blink):

```
msedge.exe --enable-features=AIProofreadingAPI --enable-blink-features=AIProofreaderAPI
```

После этого в Canary `Proofreader` — функция; `availability({expectedInputLanguages: ['en']})` → `downloadable`,
`['ru']` → `unavailable`. **Только английский.**

### Требования (документация Microsoft)

Видеокарта от 5,5 ГБ памяти; не меньше 20 ГБ свободного места на диске с профилем — **при остатке меньше 10 ГБ Edge
удаляет модель**; безлимитное подключение (на лимитном модель не скачивается); Windows 10/11 или macOS 13.3+. Подходит
ли компьютер — `edge://on-device-internals`, «Device performance class» High и выше.

**Она действительно работает на видеокарте** (замер 7 октября 2026, Edge Canary 157.0.4323.0, RTX 4070 12 ГБ): пока
проверка Edge в BlueEdit шла по британскому академическому набору (59 предложений, медиана 0,4 с), `nvidia-smi` раз в
секунду показывал до 12 002 МБ занятой видеопамяти (вся карта) и до 71 % нагрузки, а на видеокарте были только
`msedge.exe` и системный `dwm.exe`. В отличие от проверки Chrome, которая на этом же компьютере шла на процессоре:
компьютеру без подходящей видеокарты доступна проверка Chrome, но не Edge.

### Как управлять им из другой программы

Скрытый Edge со своим профилем, управление по протоколу Chrome DevTools (CDP):

```
msedge.exe --headless=new --remote-debugging-port=0 --user-data-dir=<профиль> --no-first-run
           --no-default-browser-check --enable-features=AIProofreadingAPI
           --enable-blink-features=AIProofreaderAPI file:///<профиль>/page.html
```

- `--remote-debugging-port=0`: Edge сам выбирает свободный порт и пишет его в `<профиль>\DevToolsActivePort`.
- `GET http://127.0.0.1:<порт>/json` — список страниц; WebSocket к `webSocketDebuggerUrl` нужной страницы, и
  `Runtime.evaluate` с `awaitPromise: true, returnByValue: true, userGesture: true`.

Ловушки и что помогло:

| Что видно | Причина | Решение |
|---|---|---|
| `NotAllowedError: Requires a user gesture when availability is "downloading" or "downloadable".` | `create()` требует действия пользователя, пока модель не скачана | `userGesture: true` в `Runtime.evaluate` |
| `ReferenceError: Proofreader is not defined` на `about:blank` | API нужен защищённый контекст, а `about:blank` им не считается | страница `https://` или, без сети, локальный файл `file:///…html` (работает) |
| `Proofreader` нет сразу после запуска | страница попала в список раньше, чем загрузился её документ | ждать `document.readyState == "complete"` (и заголовок нашей страницы) |
| иногда `AbortError: The request was cancelled.` | не выяснено | сначала вызывать `availability()`; повторять `proofread()` до 3 раз |
| обычный Edge 154: `availability()` не отвечает | — | ограничение 30 с и сообщение «try Edge Canary» |

### Модель

После `create()` Edge скачал модель примерно за 70 с (первый `proofread()` — 9 с, следующие 0,15–0,4 с). Она лежит в
профиле:

```
<профиль>\EdgeLLMOnDeviceModel\2026.10.2.1\     1 248 МБ
    model.onnx (1 МБ), model.onnx.data (1 261 МБ), genai_config.json, manifest.json, tokenizer.json (15 МБ),
    edge_on_device_model_execution_config.pb (7 КБ), program_cache.bin, adapter_cache.bin, encoder_cache.bin
```

- `manifest.json`: «Edge On Device Model», базовая модель **`Aion-1.1-Instruct`**, версия 2026.10.2.1, тип `webgpu`.
- `genai_config.json`: среда `onnxruntime-genai`, провайдер WebGPU; архитектура `qwen3`, контекст 32 768, скрытый
  размер 2048, 28 слоёв, 24 головы внимания (8 KV), словарь 200 029. Системный текст: «You are Aion Instruct, a large
  language model trained by Microsoft.»

Значит, **документация устарела**: в ней Phi-4-mini, а Canary 157 использует собственную модель Microsoft Aion 1.1.
Microsoft объявила Aion 1.0 на Build 2026 (2 июня) и обещала открыть веса Aion 1.0 Instruct на Hugging Face в июле
2026; на октябрь 2026 их там нет (поиск по Hugging Face ничего не находит). Лицензию модели внутри Edge не изучали —
используем её только через Edge.

### Как Edge проверяет текст: обвязка модели

В `edge_on_device_model_execution_config.pb` (protobuf, строки читаются) лежат промпты, которыми Edge оборачивает
модель. Проверка — дословно:

> You are a skilled proofreader that can identify and correct grammatical errors in a given text in the
> 'GIVEN_TEXT' section. Your task is to proofread the 'GIVEN_TEXT' and output the 'PROOFREAD_TEXT'. Output ONLY the
> 'PROOFREAD_TEXT' and nothing else.

И второй промпт, разметка (по одному вызову на исправление):

> You are a skilled proofreading teacher that correctly labels the correction made to the 'ORIGINAL_TEXT' for
> proofreading… 'Correcting <error> to <correction>'… Label the 'CORRECTION' from one of the following labels:
> Spelling, Punctuation, Capitalization, Preposition, Missing words, Grammar. Give the output in the following json
> format: '{"label": "<label0>"}' Output ONLY the json and DO NOT ADD ANY HEADLINE SENTENCES.

Типы сообщений: `optimization_guide.proto.ProofreaderApiRequest` / `ProofreaderApiResponse` (там же
`PromptApiRequest`, `SummarizeRequest`, `WritingAssistanceApiRequest` с промптами Summarizer, Writer и Rewriter).

**Модель возвращает только исправленный текст; исправления Edge находит, сравнивая его с исходным.** Проверка
языковой моделью в BlueEdit устроена так же (см. proofing-internals.md), только ответ — JSON с пояснениями.

### Что он возвращает

```json
{"correctedInput": "He goes to school.", "corrections": [{"correction": "goes", "startIndex": 3, "endIndex": 5}]}
```

Черновик W3C описывает ещё `includeCorrectionTypes`, `includeCorrectionExplanations`, `correctionExplanationLanguage`
и у каждого исправления `types` (spelling, punctuation, capitalization, grammar…) и `explanation`. Edge 157 эти
параметры принимает, но ни типов, ни пояснений не возвращает.

### Качество

В замере BlueEdit (tools/proofbench, английская часть: 25 грамматических ошибок, 7 опечаток, 13 правильных
предложений): грамматика 19/25, опечатки 5/7, **6 ложных срабатываний из 13, из них 4 — британское написание,
исправленное на американское** (organised → organized, centre → center, travelled, apologise). Средний уровень среди
локальных моделей; проверяет по американским правилам.

### Другие встроенные API Edge (Canary 157)

- **Prompt API** (`LanguageModel`): `availability()` → `available` (та же модель Aion). Принимает *наш* промпт с
  JSON-схемой (`responseConstraint`), поэтому может проверять и русский. Первая проба с промптом BlueEdit: «Мы был» →
  «Мы были» (0,7 с), «сказал что» → «сказал, что» (0,8 с), «She don't» → «She does not» (0,5 с); «Я пошли» →
  «Мы пошли» (неверно: меняет смысл; этот первый вызов шёл 66 с и дописывал пробелы). Пояснения — по-английски, что
  бы ни просил промпт. По словам Microsoft, Aion работает и на процессоре. Замер ещё не сделан.
- **Translator**: ru→en и en→ru `downloadable`; после загрузки «Вчера мы долго гуляли по парку и разговаривали о
  книгах.» → «Yesterday we walked around the park for a long time and talked about books.» (7,4 с вместе с загрузкой),
  en→ru 1,2 с, качество хорошее.
- **LanguageDetector**: есть; возвращает языки с уверенностью и `und`.

### С чем не путать

- **Microsoft Editor** в `edge://settings/languages/writingAssistanceLanguages` («Enhanced spell check», «Grammar»):
  *облачный* сервис Microsoft для полей ввода на сайтах, тот же, что в Word и Outlook. Грамматика только для
  английского (для русского переключатель неактивен). Публичного API нет.
- **Copilot** «check your work with an AI proofreader» (справочный центр microsoft.com): вставить текст в чат Copilot.
  Облако, API нет.
- Расширения, установленные в Edge (Reverso, Grammarly, LanguageTool), — не сам Edge.

## 2. Chrome

Chrome **154.0.8037.58** (стабильная версия), скрытый, со своим профилем, локальная страница:

| API | Есть сразу |
|---|---|
| `LanguageModel` (Prompt API, Gemini Nano) | да — `availability()` → `downloadable` |
| `Translator`, `LanguageDetector`, `Summarizer` | да |
| `Writer`, `Rewriter`, `Proofreader` | нет |

Документация Google ([proofreader-api](https://developer.chrome.com/docs/ai/proofreader-api)): Proofreader был
**пробным периодом (origin trial) в Chrome 141–145** — проверка на живых сайтах: сайт регистрируется и получает ключ,
который включает API только на нём и только в этих версиях; модель Gemini Nano; `includeCorrectionTypes` и
`includeCorrectionExplanation` не поддерживаются; для локальной проверки — `chrome://flags/#proofreader-api`.
Требования: 22 ГБ свободного места; видеокарта больше 4 ГБ или процессор с 16 ГБ памяти и 4 ядрами; безлимитное
подключение.

**Она работает на процессоре, есть видеокарта или нет** (замер 7 октября 2026 на тестовой машине, Chrome
154.0.8037.98, i9-11900KB, 64 ГБ, RTX 4070): пока проверка Chrome в BlueEdit шла по британскому академическому
набору (59 предложений, медиана 0,6 с), `nvidia-smi` раз в секунду показывал 0–83 МБ занятой видеопамяти и около 0 %
нагрузки — как при простаивающем Chrome; процесс видеокарты Chrome значился без своей памяти. На другом компьютере в
журнале Chrome то же самое: «Created TensorFlow Lite XNNPACK delegate for CPU» (XNNPACK — процессорный движок LiteRT).
Так что отказ проверки объясняет не отсутствие подходящей видеокарты, а скачивание модели (нужны серверы Google и безлимитное подключение),
16 ГБ памяти и 22 ГБ свободного места.

В 154 код на месте — в `chrome.dll` есть `blink::Proofreader`, `blink::Proofreader::proofread`,
`blink::Proofreader::availability`, `blink::Proofreader::GetCorrectionsTypes`,
`blink::mojom::blink::AIManagerCreateProofreaderClient`, `ProofreaderApiRequest`, `kAIProofreadingAPI`,
`kProofreaderApi`, `proofreader-api` и, в отличие от Edge, **`ProofreaderSmallExpert`** и
**`proofreader_small_expert_model_component`** — отдельная маленькая «экспертная» модель для проверки, не только
Gemini Nano. Флаг тоже есть: «Proofreader API — Enables the Proofreader API, allowing you to proofread a piece of text
with a built-in small expert model…». Рядом в `chrome://flags` — `#semantic-embedder-api` (смысловые векторы текста) и
`#gemma4-for-built-in-ai` («Enables all built-in AI APIs (Prompt, Summarizer, Writer, etc.) to use the Gemma 4 model»).

Как устроено «код есть, а функции нет»: программа собирается целиком, а видимость функции решает её переключатель
(feature flag). Пока он выключен, страницы видят `typeof Proofreader === 'undefined'`, как будто его нет вовсе. У
флагов Chrome есть срок годности; флаги с истёкшим сроком возвращает `#temporary-unexpire-flags-m153` (в 154 есть
`…-m152` и `…-m153`).

Что пробовали в отдельном профиле — `Proofreader` не появился ни разу:

- `--enable-blink-features=AIProofreadingAPI`
- `--enable-features=AIProofreadingAPI,ProofreaderSmallExpert --enable-blink-features=AIProofreadingAPI,AIProofreaderAPI`
- флаг `proofreader-api@1` в `Local State` (одним — Chrome убрал его при запуске; вместе с
  `temporary-unexpire-flags-m152@1` и `-m153@1` — сохранился, но не помог)
- `proofreader-api@1`, `semantic-embedder-api@1`, `gemma4-for-built-in-ai@1` — и в скрытом режиме, и в обычном окне
- страницы `file://` и `https://example.com/`.

### Работает с флагами *и* конфигурацией экспериментов Google

В Chrome 154, которым пользуются каждый день (как включить флаги — по шагам в
[../proofing-setup.ru.md](../proofing-setup.ru.md#корректор-chrome)), с включёнными `#proofreader-api`,
`#semantic-embedder-api` и `#gemma4-for-built-in-ai` и полным перезапуском
[площадка](https://chrome.dev/web-ai-demos/proofreader-api-playground/) заработала; модель скачалась при первом
нажатии кнопки. От тестового профиля отличались не флаги (те же три), а **конфигурация экспериментов (variations
seed)**, которую Chrome скачивает с серверов Google и применяет при следующем запуске (ключи `variations_*` в
`Local State`). В новом профиле её не было — и Chrome даже стирал из него флаги при запуске. Когда в отдельный профиль
скопировали только ключи `variations_*` повседневного профиля и его список флагов (без личных данных), `Proofreader`
появился и там: `en` → `downloadable`, `ru` → `unavailable`. Значит, поверх флага API включается на стороне сервера,
у части пользователей.

**Модель** — не Gemini Nano, а крошечная, скачивается в папку данных:

```
User Data\OptGuideManifestModel\6fe836b7…\2026.6.3.1603\     117 МБ
    manifest.json: "Optimization Guide On-Device Tinymodel Proofreader", версия 2026.6.3.1603
    weights.bin (117 МБ), cache.bin, adapter_cache.bin, encoder_cache.bin, _metadata\verified_contents.json
```

Посмотрели 5 октября 2026: `weights.bin` начинается с `LITERTLM` — это контейнер
[LiteRT-LM](https://github.com/google-ai-edge/LiteRT-LM) (открытая среда Google для моделей на устройстве, Apache-2.0)
с моделью TFLite (prefill/decode, 18 слоёв: `kv_cache_k_0` … `_17`) и шаблоном запроса читаемым текстом: текст
пользователя, за ним ` <ctrl1>`, затем ответ модели — без инструкций, модель обучена на одну задачу. Значит, её,
вероятно, можно запустить и без Chrome — в LiteRT-LM с этим шаблоном; **ещё не пробовали**, как и то, что ответ — это
исправленный текст (как у Edge, который сравнивает тексты). Веса принадлежат Google и идут в составе Chrome без
отдельной лицензии: попробовать на своём компьютере можно, включать в BlueEdit — нет. Рядом,
`OptGuideOnDeviceModel\2025.8.8.1141`, — Gemini Nano (`BaseModelSpec` «v3Nano», 4,3 ГБ, у `weights.bin` нет читаемого
заголовка), модель Prompt API.

**Возвращает типы исправлений** (Edge — нет): с `includeCorrectionTypes: true`

```json
{"correction": "is", "startIndex": 5, "endIndex": 8, "types": ["grammar"]}
{"correction": "random", "startIndex": 11, "endIndex": 17, "types": ["spelling"]}
{"correction": "doesn't", "startIndex": 4, "endIndex": 9, "types": ["grammar", "punctuation"]}
```

`includeCorrectionExplanations: true` принимается, но `explanation` не приходит. Первые пробы: «This are a radnom text
with a few typicla typso.» → «This is a random text with a few typical typos.» (2,9 с); «She don't like coffee, and
their going home.» → «doesn't», «they're» (1,7 с); британские «organised», «centre» не тронул — но **убрал точку** в
конце «We organised the meeting at the centre.» (ложное срабатывание). На тексте площадки ещё сделал «the Proofreader
API» → «The proofreader API». `create()` вместе с загрузкой — 3,6 с. Русский: `NotSupportedError: The requested
language options are not supported.` Только английский.

В BlueEdit (`BrowserProofreader.Chrome`): скрытый Chrome с `%LOCALAPPDATA%\BlueEdit\chrome-profile`, в `Local State`
которого перед каждым запуском копируются ключи `variations_*` и список флагов пользователя (только они). Замер 6
октября 2026 на британском академическом наборе: 21 из 24 грамматических ошибок (20 с верным исправлением), 2 из 3
пунктуационных замечены, 26 ложных тревог на 24 верных предложениях — 20 из них убранная точка в конце; с возвращённой
точкой (`KeepFinalStop`) 7: «committee have» / «team were» в единственное число, дважды выброшенное «had», заглавные в
«student handbook», один раз «The» → «Few». 0,65 с на предложение. Скрытый (headless) Chrome работает (прежние попытки
не удавались из-за отсутствия конфигурации, а не из-за headless).

**Без Chrome** (6 октября 2026): модель работает в LiteRT-LM от Google (`pip install litert-lm`, 0.17.1).
Выяснилось по ходу:
- `litert-lm describe` читает файл: только текст, без рассуждений, температура выборки 0, top-k 3. `litert-lm run model
  --prompt "She don't like coffee, and their going home."` → «She doesn't like coffee, and they're going home.» (1,3 с
  с загрузкой). Движок смотрит на расширение: открывает копию с именем `.litertlm`, а `weights.bin` из Chrome — нет.
- Его инструменты держат своё хозяйство в `~/.litert-lm` (`os.path.expanduser`); с `USERPROFILE` (или `HOME`),
  указывающим в другое место, домашнюю папку пользователя они не трогают.
- `litert-lm serve` (сервер, совместимый с OpenAI, новая беседа на каждый запрос) нашёл 14 из 24 ошибок британского
  набора, с `temperature: 0` и без. Python-API с голым запросом (`текст <ctrl1>`, как в шаблоне) при самом вероятном
  ответе дал то же: «effect» → «affect» верно, «weather», «raised» пропущены.
- **Chrome выбирает случайно**: два прогона одного набора в Chrome разошлись в 6 предложениях из 59 (в одном переписано
  «findings», в другом выброшено «from»; 7 и 4 ложные тревоги). Выборка в LiteRT-LM (top-k 3) с фиксированным зерном:
  15–17 из 24 при температуре 1,0 на трёх зёрнах, 17 при 0,5 — всё равно меньше 21 у Chrome; что ещё Chrome делает
  иначе, не нашли. Вариант на видеокарте для этой модели не запустился (`Failed to create LiteRT-LM engine`); она
  работает на процессоре (XNNPACK).
- Проверка BlueEdit (`ChromeModelChecker`) запускает `tools/chrome-model/proofread.py` (встроен в редактор) в
  виртуальном окружении, которое создаёт `install.ps1 -ChromeModel`: модель грузится один раз, строки JSON туда и
  обратно, top-k 3 при 0,5 с зерном 1: 17 из 24, 2 ложные тревоги, 0,3 с.

## 3. Microsoft Word (Office 16, COM)

- Word 16.0 через COM: запуск 1,6 с, 5 слов 0,5 с. LanguageID 1049 = ru-RU, 2057 = en-GB.
- **Орфография работает**: «превет» → привет, преет, поревёт, прервёт, поревет; «color» в en-GB отмечается.
- **Грамматика для русского — нет**: `Range.GrammaticalErrors` находит 1 ошибку в «He go to school every days.» (всё
  предложение), но 0 в «Я пошли домой вчера.» и «Мы был в магазине.»; 1 в «Он сказал что придёт.» (пунктуация).
  `Application.CheckGrammar()` возвращает True для всех трёх. Сам Word «Я пошли» тоже не подчёркивает.
- Средства проверки установлены: русские `MSSP7RU.LEX` (орфография) / `MSGR8RU.LEX` (грамматика), английские
  `MSSP7EN.LEX` / `MSGR8EN.LEX`; `CheckGrammarWithSpelling` и `CheckGrammarAsYouType` включены; стиль для русского —
  «Грамматика и стиль». Значит, ничего не недостаёт: классический офлайновый модуль русской грамматики проверяет в
  основном пунктуацию. Современная грамматика — Microsoft Editor в облаке, без публичного API (адреса Word Online не
  использовали — условия использования).
- Что BlueEdit делает с Word (OfficeSpell.cs): фоновый поток в однопоточном апартаменте (`ApartmentState.STA`)
  владеет `Word.Application`; скрытый документ `Documents.Add(..., false)`; слова пачками до 300 на язык; варианты из
  `GetSpellingSuggestions`; закрывает только тот Word, который запустил сам.

## 4. Проверка орфографии Windows

- COM `SpellCheckerFactory`, CLSID `7AB36653-1796-484B-BDFA-E74F1DB7C1DC`; `ISpellCheckerFactory` IID
  `8E018A9D-2415-4677-BF08-794EA61F94BB`.
- `ISpellChecker`: первый испробованный IID (`B6FD0B71-…`) дал `E_NOINTERFACE` (0x80004002); рабочий —
  `E7ED1C71-87F7-4378-A840-C9200DACEE47`.
- Теги, поддерживаемые на тестовой машине: en-029 … en-ZW, ru-MO, ru-RU. Языки пользователя:
  `HKCU\Control Panel\International\User Profile`, значение `Languages` (на тестовой машине `en-GB`, `ru`), с фильтром
  по `IsSupported`.
- API грамматики в Windows нет.

## 5. LanguageTool (бесплатный, локальный)

- `OpenJDK21U-jre_x64_windows_hotspot_21.0.12.1_1.zip` (Adoptium, 46,7 МБ, SHA-256 сверена) и `LanguageTool-stable.zip`
  (240 МБ, LanguageTool 6.6); в распакованном виде 531 МБ.
- `java -Xmx512m -cp languagetool-server.jar org.languagetool.server.HTTPServer --port <порт>`; готов через 5–7 с;
  640–710 МБ памяти.
- `POST /v2/check` с `text`, `language`; `disabledCategories=TYPOS,BRE_STYLE_OXFORD_SPELLING` (опечатки — дело
  проверки орфографии; оксфордская категория отмечала «organised»), `disabledRules=WHITESPACE_RULE,
  COMMA_PARENTHESIS_WHITESPACE,UPPERCASE_SENTENCE_START`.
- **`localhost` стоил 2 с на запрос** (2071–2134 мс: Windows сначала пробует IPv6), `127.0.0.1` — 58–115 мс.

## 6. Языковые модели через llama.cpp: наблюдения

- Со слишком коротким промптом («ты корректор, верни JSON с исправленным текстом») YandexGPT 5 Lite **перевела**
  русское предложение на английский вместо исправления. С полным промптом редактора (языки, «не перефразируй»,
  «не переводи») — исправляет. Детали промпта важны.
- Маленькие модели иногда отвечают не тем: Phi-4-mini переводила английские предложения на русский, Gemma 3 4B
  вернула посторонний русский текст. Защита от переписывания в редакторе такие ответы отбрасывает.
- Промпт в стиле Edge (только исправленный текст) на этих моделях работал хуже, чем JSON с пояснениями.
- Сборка llama.cpp под Vulkan на NVIDIA: первый ответ 16 с (компиляция шейдеров), CUDA — 0,4 с.

## 7. Что ещё смотрели

- Hunspell (WeCantSpell.Hunspell 7.0.1, словари LibreOffice) — для переноса на Linux.
- Harper (Apache-2.0, английский, `harper-ls` / `harper-cli --format json`).
- Яндекс.Спеллер: бесплатное облако, 10 000 запросов и 10 млн символов в сутки, обязательна подпись «Проверка
  правописания: Яндекс.Спеллер».
- LanguageTool в облаке: бесплатно 20 запросов и 75 000 символов в минуту; Premium около 60 € в год.
- Не подходят: Grammarly (SDK закрыт 10 января 2024), ОРФО (нет SDK), Phi Silica (убирают в январе 2027), Главред и
  Литера5 (платные).
