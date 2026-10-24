# Установка и настройка проверки правописания

*In English: [proofing-setup.md](proofing-setup.md)*

Как поставить всё, чем BlueEdit проверяет орфографию и грамматику, — скриптом или вручную, — и как убедиться, что
работает. Как этим пользоваться — в [proofing.ru.md](proofing.ru.md); как это устроено — в
[tech/proofing-internals.md](tech/proofing-internals.md); исследование Edge, Chrome и Word — в
[tech/browser-ai-research.md](tech/browser-ai-research.md).

Всё бесплатное, без подписок, работает на вашем компьютере и не требует прав администратора — кроме облачного
сервиса, который можно выбрать вместо модели здесь: ему нужен ключ, и он платный по использованию.

## Что бывает

| Компонент | Для чего | Обязателен | Размер | Ставится |
|---|---|---|---|---|
| Языки Windows | орфография | да (уже есть) | — | Параметры Windows |
| Microsoft Word | орфография словарями Office (вариант) | нет | — | вместе с Office |
| Java 21 + LanguageTool | грамматика по правилам | нет | ~540 МБ | `install.ps1 -LanguageTool` |
| llama.cpp | запускает языковые модели | нет | 35–720 МБ | `install.ps1 -Llm` |
| Модель `.gguf` | грамматика нейросетью | для llama.cpp | 2,5–9 ГБ | `install.ps1 -Models …` |
| Edge Canary | корректор Edge (английский) | нет | ~1 ГБ + модель 1,2 ГБ | `install.ps1 -EdgeCanary` |
| Harper | грамматика правилами для английского, британского или американского | нет | 60 МБ | `install.ps1 -Harper` |
| LiteRT-LM | модель корректора Chrome без Chrome (английский) | нет | ~200 МБ + Python | `install.ps1 -ChromeModel` |

Что выбрать — см. «Сравнение: кто точнее» в [proofing.ru.md](proofing.ru.md). Коротко: **LanguageTool + llama.cpp +
YandexGPT 5 Lite** — самый полный вариант; без видеокарты — LanguageTool.

## Быстрее всего: из редактора

**Tools → Set up grammar checking…** включает одну проверку, ничего не надо знать заранее:

| Выбор | Что происходит |
|---|---|
| A language model here (модель здесь) | список проверенных моделей с размером и лицензией (Qwen3 8B — Apache 2.0, YandexGPT 5 Lite — собственная лицензия Яндекса, Gemma 3 — условия Google, Phi-4-mini — MIT); ставятся llama.cpp под видеокарту этого компьютера (CUDA, Vulkan или процессор, если карты нет) и модель, модель выбирается, проверка грамматики включается |
| Ollama | модели запущенного на этом компьютере [Ollama](https://ollama.com) (127.0.0.1:11434 или `OLLAMA_HOST`, как его читает сам Ollama) — списком, остаётся выбрать; модели-эмбеддинги не показываются. Не запущен или моделей ещё нет: что сделать по шагам (установить, `ollama pull gemma3:4b`, выбрать снова). BlueEdit ничего не ставит, абзацы не покидают компьютер |
| LM Studio | то же для сервера [LM Studio](https://lmstudio.ai) (127.0.0.1:1234; включается на вкладке Developer или `lms server start`) |
| Another model server (другой сервер) | адрес (llama-server на другой машине, облачный сервис), имя модели там и ключ: здесь ничего не ставится, абзацы отправляются туда |
| LanguageTool | для тех, кому нужны правила для 31 языка без видеокарты: Java 21 и LanguageTool, ~540 МБ |
| Harper | правила для английского, ~60 МБ |
| кнопка **Remove what was installed here...** под списком (Tab) | поставленное редактором в свои папки, у каждого размер, ничего не отмечено: llama.cpp, каждая модель, LanguageTool с его Java, Harper, словари. Отмеченная проверка сначала выключается, потом удаляется; папка, на которую настройки указывают в другое место (свой LanguageTool), не предлагается никогда |

Всё в фоне (шаги в строке состояния, набор не прерывается), из тех же источников и с теми же контрольными суммами, что
у скриптов ниже, в те же папки. То же самое в командной строке, например на сервере без экрана редактора:

```
be --install-proofing                 # что можно поставить и что уже стоит
be --install-proofing qwen3-8b,harper # эти части
```

**Словари орфографии** (Hunspell — орфография на Linux и macOS): **Tools → Get spelling dictionaries…** показывает
словари LibreOffice для одиннадцати языков с размерами (0,6–9,1 МБ) и скачивает отмеченные в папку самого BlueEdit
(`~/.local/share/BlueEdit/hunspell`), без root; новые языки добавляются к языкам проверки. Язык, словарь которого уже
есть в системе (`apt install hunspell-ru`), помечен «in the system» и повторно не скачивается; сразу отмечены языки
проверки, для которых словаря нет нигде. В командной строке: `be --install-proofing ru-RU,en-GB`.

На Linux llama.cpp нужна системная библиотека OpenMP (`libgomp.so.1`; Debian и Ubuntu: `sudo apt install libgomp1`):
если её нет, это сообщается сразу. Edge Canary и модель Chrome остаются за скриптами.

## Быстро: скриптом

Скрипты лежат в `tools\proofing-setup\` репозитория. В PowerShell:

```powershell
cd tools\proofing-setup
.\install.ps1 -All          # LanguageTool, llama.cpp, рекомендуемая модель (YandexGPT), Edge Canary, Harper
.\check.ps1 -Run            # что установлено + пробная проверка предложения с ошибкой
```

По частям:

```powershell
.\install.ps1 -LanguageTool                          # Java 21 и LanguageTool
.\install.ps1 -Llm                                   # llama.cpp (+ YandexGPT, если моделей ещё нет)
.\install.ps1 -Models yandexgpt,gemma3-12b           # модели (llama.cpp поставится, если его нет)
.\install.ps1 -EdgeCanary                            # Edge Canary
.\install.ps1 -Harper                                # Harper (harper-ls)
.\install.ps1 -ChromeModel                           # LiteRT-LM для модели Chrome без Chrome (Python 3.10+)
.\install.ps1 -List                                  # какие модели знает скрипт и какие уже стоят
.\install.ps1 -Llm -Force                            # переустановить (например, после -LlamaBuild latest)
```

Параметры:

| Параметр | Что делает |
|---|---|
| `-Gpu auto\|cuda\|vulkan\|cpu` | сборка llama.cpp. `auto`: CUDA, если есть драйвер NVIDIA (CUDA 13.4 при драйвере 580+, иначе 12.4 в Windows и 12.8 в Linux), иначе Vulkan, если Vulkan видит видеокарту, иначе сборка для процессора (что он видит, показывает `be --vulkan-devices`) |
| `-LlamaBuild b11371\|latest` | версия llama.cpp; по умолчанию та, с которой BlueEdit проверен |
| `-Root <папка>` | куда ставить; по умолчанию `%LOCALAPPDATA%\BlueEdit` (тогда в настройках ничего указывать не нужно) |
| `-Force` | переустановить уже установленное |

Что скрипт гарантирует:

- **Только официальные источники**: adoptium.net (Java), languagetool.org, GitHub-релизы llama.cpp, Hugging Face
  (модели), сайт Microsoft (Edge Canary).
- **Проверка целостности**: для Java, llama.cpp и моделей — контрольная сумма SHA-256, которую публикует источник
  (Adoptium, GitHub, Hugging Face); при несовпадении файл удаляется и установка останавливается. LanguageTool сумм не
  публикует — проверяется, что в архиве есть сервер. Установщик Edge Canary проверяется по цифровой подписи Microsoft.
- **Докачка**: прерванная загрузка продолжается с места остановки (файлы в `%LOCALAPPDATA%\BlueEdit\downloads`, их
  можно удалить после установки).
- **Повторный запуск безопасен**: установленное пропускается. Если BlueEdit запущен и держит заменяемый файл, скрипт
  остановится и попросит его закрыть.

### Linux (и WSL)

`tools/proofing-setup/install.sh` делает то же на Linux, в `~/.local/share/BlueEdit` (`$XDG_DATA_HOME/BlueEdit`), где
их ищет Linux-версия BlueEdit; нужны `curl`, `tar` и `python3`, права root не нужны:

```bash
cd tools/proofing-setup
./install.sh --all                                   # LanguageTool, llama.cpp, рекомендуемая модель (YandexGPT), Harper
./install.sh --harper                                # Harper (harper-ls)
./install.sh --languagetool                          # Java 21 и LanguageTool
./install.sh --llm --models yandexgpt,gemma3-12b     # llama.cpp и модели
./install.sh --list                                  # какие модели скрипт знает и какие установлены
```

Параметры — как на Windows: `--gpu auto|cuda|vulkan|cpu` (`auto`: CUDA, если работает `nvidia-smi` — в WSL тоже, с
драйвером NVIDIA из Windows; иначе Vulkan, если `vulkaninfo` показывает видеокарту, а не только llvmpipe; иначе сборка для процессора), `--llama-build`,
`--root`, `--force`. Те же источники и проверка SHA-256; Linux-архивы llama.cpp работают сразу после распаковки (их
библиотеки лежат рядом с `llama-server`). Орфография на Linux — Hunspell со словарями дистрибутива (`sudo apt install
hunspell-ru hunspell-en-gb`) или из Tools → Get spelling dictionaries; корректор Edge есть только в Windows.

## Вручную

Всё раскладывается в одну папку — по умолчанию `%LOCALAPPDATA%\BlueEdit`:

```
%LOCALAPPDATA%\BlueEdit\
    jre\bin\java.exe                    Java 21
    languagetool\languagetool-server.jar
    llm\bin\llama-server.exe            llama.cpp
    llm\models\*.gguf                   модели
    edge-profile\                       создаёт сам BlueEdit для корректора Edge
```

Другие места задаются настройками `languageToolDir` (папка с `jre\` и `languagetool\`), `llmDir` (папка с `bin\` и
`models\`), `edgePath` (путь к `msedge.exe`).

### Языки Windows (орфография)

Параметры → Время и язык → Язык и регион → Добавить язык. Для проверки орфографии достаточно базового языкового
пакета. Английский: `en-GB` и `en-US` — разные словари, добавьте нужный. В BlueEdit — **Tools → Spelling languages…**
(режим `auto` берёт добавленные в Windows).

### Microsoft Word (орфография словарями Office)

Нужен установленный Word (Office 2016 и новее, Microsoft 365). Языки проверки — в самом Office (Файл → Параметры →
Язык); для русского нужен русский пакет средств проверки. В BlueEdit — **Tools → Spelling provider… → Office**.
Грамматику Word для русского BlueEdit не использует: через COM она находит почти только пунктуацию (подробно — в
исследовании).

### Словари Hunspell (орфография на Linux и macOS; на Windows по желанию)

Проще всего: **Tools → Get spelling dictionaries…** (выше), на любой системе. Linux: также пакеты дистрибутива,
например в Debian/Ubuntu `sudo apt install hunspell-ru hunspell-en-gb hunspell-en-us` (ставятся в `/usr/share/hunspell`). BlueEdit смотрит также в `~/.local/share/hunspell`, `/usr/share/myspell`,
`/usr/local/share/hunspell`, `/opt/homebrew/share/hunspell` и `~/Library/Spelling`. Windows: положите пары файлов —
`ru_RU.dic` + `ru_RU.aff`, `en_GB.dic` + `en_GB.aff` — в `%LOCALAPPDATA%\BlueEdit\hunspell`; словари LibreOffice лежат
в [LibreOffice/dictionaries](https://github.com/LibreOffice/dictionaries) (папки `ru_RU` и `en`); затем **Tools →
Spelling provider… → Hunspell**. Файл ищется по имени языка: `ru-RU` → `ru_RU.dic`, просто `ru` → первый `ru_*.dic`.
Код Hunspell (WeCantSpell.Hunspell) — под MPL 1.1 / GPL 2.0 / LGPL 2.1, унаследованными от Hunspell; у словарей свои
лицензии, они лежат в их пакетах.

### LanguageTool

1. **Java 21 (JRE)**: [adoptium.net](https://adoptium.net/temurin/releases/?version=21&package=jre&os=windows&arch=x64) →
   Windows x64, JRE, `.zip` (не `.msi`). Распакуйте так, чтобы получилось `BlueEdit\jre\bin\java.exe` (содержимое папки
   `jdk-21…-jre` из архива — в `jre\`).
2. **LanguageTool**: <https://languagetool.org/download/LanguageTool-stable.zip> (~240 МБ). Распакуйте так, чтобы
   получилось `BlueEdit\languagetool\languagetool-server.jar` (содержимое папки `LanguageTool-6.x` — в `languagetool\`).
3. В BlueEdit — **Tools → Grammar checking… → LanguageTool**. Запускается при первой проверке (5–12 с), занимает
   около 0,7–1 ГБ памяти, закрывается вместе с редактором.

Проверить вручную (как это делает BlueEdit):

```powershell
cd $env:LOCALAPPDATA\BlueEdit\languagetool
..\jre\bin\java.exe -Xmx768m -cp languagetool-server.jar org.languagetool.server.HTTPServer --port 8081
# в другом окне:
Invoke-RestMethod http://127.0.0.1:8081/v2/check -Method Post -Body @{ text = 'Мы был в магазине.'; language = 'ru-RU' }
```

Адрес — `127.0.0.1`, не `localhost`: с `localhost` Windows сначала пробует IPv6 и теряет 2 секунды на каждом запросе.

### llama.cpp

Релизы: <https://github.com/ggml-org/llama.cpp/releases> (сборки выходят несколько раз в день; BlueEdit проверен с
`b11371`). Какую сборку брать:

| Видеокарта | Архивы |
|---|---|
| NVIDIA, драйвер 580 и новее | `llama-bNNNNN-bin-win-cuda-13.4-x64.zip` **и** `cudart-llama-bin-win-cuda-13.4-x64.zip` |
| NVIDIA, драйвер старше | `…-cuda-12.4-x64.zip` и `cudart-…-cuda-12.4-x64.zip` |
| AMD, Intel, любая другая | `llama-bNNNNN-bin-win-vulkan-x64.zip` |
| без видеокарты | `llama-bNNNNN-bin-win-cpu-x64.zip` (медленно) |

Версия драйвера NVIDIA: `nvidia-smi` (последний столбец заголовка). Распакуйте всё в `BlueEdit\llm\bin\`, чтобы там
лежал `llama-server.exe` (для CUDA — вместе с `cudart64_*.dll` и `cublas*.dll` из второго архива). У каждого файла на
странице релиза указана SHA-256 (`digest`); сверить: `Get-FileHash <файл>`.

### Модели

Файлы `.gguf` кладутся в `BlueEdit\llm\models\`. Проверенные (квантование Q4_K_M):

| Ключ в скрипте | Файл | Где (huggingface.co/…) | Видеопамять |
|---|---|---|---|
| `yandexgpt` | `YandexGPT-5-Lite-8B-instruct-Q4_K_M.gguf` | `yandex/YandexGPT-5-Lite-8B-instruct-GGUF` | ~6 ГБ |
| `gemma3-12b` | `google_gemma-3-12b-it-Q4_K_M.gguf` | `bartowski/google_gemma-3-12b-it-GGUF` | ~8 ГБ |
| `qwen3-8b` | `Qwen3-8B-Q4_K_M.gguf` | `Qwen/Qwen3-8B-GGUF` | ~6 ГБ |
| `qwen3-14b` | `Qwen_Qwen3-14B-Q4_K_M.gguf` | `bartowski/Qwen_Qwen3-14B-GGUF` | ~10 ГБ |
| `gemma3-4b` | `google_gemma-3-4b-it-Q4_K_M.gguf` | `bartowski/google_gemma-3-4b-it-GGUF` | ~3 ГБ |
| `phi4-mini` | `microsoft_Phi-4-mini-instruct-Q4_K_M.gguf` | `bartowski/microsoft_Phi-4-mini-instruct-GGUF` | ~3 ГБ |

Ссылка на файл: `https://huggingface.co/<репозиторий>/resolve/main/<файл>`. SHA-256 файла — на его странице
Hugging Face (Files → файл → «SHA256»). Подойдёт и любая другая модель `.gguf`, но качество проверки у моделей очень
разное — прогоните её через замер (`tools\proofbench\bench.py`).

Модель должна помещаться в видеопамять **вместе с тем, что там уже есть** (браузер, игры, другая модель). Если не
помещается, llama.cpp не запустится — в строке состояния будет `GRAMMAR LLM failed`.

В BlueEdit — **Tools → Grammar checking… → Language model**, модель — **Tools → Grammar model…** (Auto берёт лучшую
из установленных: YandexGPT, Gemma 3 12B, Qwen3 8B, Qwen3 14B, Gemma 3 4B, Phi-4-mini).

### Edge Canary (корректор Edge)

1. Установщик с сайта Microsoft: [microsoft.com/edge/download/insider](https://www.microsoft.com/edge/download/insider) →
   Canary. Ставится для текущего пользователя в `%LOCALAPPDATA%\Microsoft\Edge SxS\`, рядом с обычным Edge, не мешая
   ему. (Пакет Canary в winget на момент проверки не проходил проверку хэша — поэтому скрипт берёт установщик с сайта
   Microsoft и проверяет его подпись.)
2. Флаги включать не нужно: BlueEdit сам запускает скрытый Canary с нужными ключами
   (`--enable-features=AIProofreadingAPI --enable-blink-features=AIProofreaderAPI`) и своим профилем
   `BlueEdit\edge-profile`. Ваш Canary и его профиль не затрагиваются.
3. В BlueEdit — **Tools → Grammar checking… → Edge**. При первом включении Edge скачивает модель (~1,2 ГБ, около
   минуты).

Требования Microsoft: видеокарта от 5,5 ГБ, 20 ГБ свободного места на диске с профилем (при остатке меньше 10 ГБ Edge
удаляет модель), безлимитное подключение. Подходит ли компьютер — в Canary на `edge://on-device-internals`
(Device performance class: High и выше). Только английский, по американским правилам. В обычном Edge 154 объект
корректора есть, но не отвечает; BlueEdit сообщает об этом через 30 секунд.

### Корректор Chrome

В Chrome 154 корректор (Proofreader API, «маленькая экспертная модель», 117 МБ) по умолчанию выключен — после
пробного периода Chrome 141–145. Его можно включить флагами. Флаги действуют на все профили одной установки Chrome
(одной папки данных); в другой установке их надо включать отдельно.

1. В адресной строке Chrome наберите `chrome://flags` и нажмите Enter. Откроется страница «Experiments».
2. В поле **Search flags** вверху введите `proofreader`. Найдётся **Proofreader API** (`#proofreader-api`).
   Можно сразу открыть его по адресу `chrome://flags/#proofreader-api` — флаг будет подсвечен.
3. В выпадающем списке справа от флага выберите **Enabled** (вместо Default).
4. Так же включили ещё два — неизвестно, обязательны ли они, но с ними работает:
   - `chrome://flags/#gemma4-for-built-in-ai` — **Gemma 4 for Built-in AI** (модель Gemma 4 для встроенных API Chrome);
   - `chrome://flags/#semantic-embedder-api` — **Semantic Embedder API**.
5. Если флага в списке нет (срок годности истёк): включите `chrome://flags/#temporary-unexpire-flags-m153`
   (**Temporarily unexpire M153 flags**), перезапустите Chrome и вернитесь к шагу 2.
6. Нажмите **Relaunch** внизу страницы. Если Chrome остаётся работать в фоне (значок в трее у часов) — закройте и
   его: правой кнопкой → Выход, потом запустите Chrome снова.
7. Откройте <https://chrome.dev/web-ai-demos/proofreader-api-playground/> и нажмите **Proofread**. При первом нажатии
   скачается модель (117 МБ), потом появится исправленный текст. С галочкой **Include correction types** — с типами
   ошибок.

Проверить в консоли (F12 → Console): `typeof Proofreader` должно дать `"function"`, а
`await Proofreader.availability({expectedInputLanguages: ["en"]})` — `available` или `downloadable`.

Кроме флагов корректору нужна конфигурация экспериментов, которую Chrome сам получает от Google в обычном профиле: в
новом пустом профиле корректора не будет. Поэтому BlueEdit (**Tools → Grammar checking → Chrome**) запускает скрытый
Chrome со своим профилем (`%LOCALAPPDATA%\BlueEdit\chrome-profile`) и перед каждым запуском копирует в него из
`Local State` вашего Chrome только список флагов и ключи `variations_*` (ту самую конфигурацию) — ни истории, ни
cookies, ни паролей, ничего больше. Модель (117 МБ) скачивается в этот профиль при первом использовании. Только
английский. Вернуть как было — те же флаги в **Default** и Relaunch. Подробно — в исследовании.

## Проверить всё

```powershell
.\check.ps1         # что установлено: языки Windows, Word, LanguageTool, llama.cpp и модели, видеокарта, Edge Canary
.\check.ps1 -Run    # плюс запускает LanguageTool и модель и проверяет предложение «Мы был в магазине…»
```

`-Model <файл.gguf>` — проверить конкретную модель. Пример ответа:

```
== LanguageTool: "Мы был в магазине и купили хлеб."
  [ok]   answer                 in 5.1 s:
         Мы был: Неверное спряжение глагола или неверное местоимение
== language model YandexGPT-5-Lite-8B-instruct-Q4_K_M.gguf: "Мы был в магазине и купили хлеб."
  [ok]   loaded                 in 3.6 s
  [ok]   answer                 in 0.4 s: Мы были в магазине и купили хлеб.
```

## Обновить и удалить

- Обновить: `.\install.ps1 -LanguageTool -Force`, `.\install.ps1 -Llm -LlamaBuild latest -Force` (после обновления
  llama.cpp стоит прогнать `check.ps1 -Run`).
- Удалить: закрыть BlueEdit и удалить папки `jre`, `languagetool`, `llm` (или отдельные модели в `llm\models`),
  `edge-profile`, `downloads` в `%LOCALAPPDATA%\BlueEdit`. В реестре и системе ничего не остаётся. Edge Canary
  удаляется как обычная программа (Параметры → Приложения).

Сколько места занимает (на тестовой машине): `jre` 146 МБ, `languagetool` 391 МБ, `llm\bin` 712 МБ (CUDA; Vulkan
— ~35 МБ), модели 2,3–8,4 ГБ каждая, `edge-profile` ~1,7 ГБ.

## Если не работает

В строке состояния видно, какие проверяльщики работают и какие нет: `GRAMMAR LT+LLM`, `GRAMMAR LT+LLM starting`
(запускаются), `GRAMMAR LT+LLM, EDGE failed` (Edge не запустился). Причину редактор один раз пишет в строке
сообщений, например `[Grammar (LanguageTool): LanguageTool not installed in …]`.

| Сообщение | Причина и что делать |
|---|---|
| `Grammar (LanguageTool): LanguageTool not installed in …` | нет `jre\bin\java.exe` или `languagetool\languagetool-server.jar` по этому пути — `install.ps1 -LanguageTool`, `check.ps1` |
| `Grammar (Language model): llama.cpp or a model not found in …` | нет `llm\bin\llama-server.exe` или ни одной модели в `llm\models` — `install.ps1 -Llm` |
| `Grammar (Language model): language model did not start: …` | не хватило видеопамяти (закройте то, что её занимает, или возьмите модель меньше в **Tools → Grammar model…**); для CUDA не распакован `cudart-…zip`; драйвер NVIDIA старше нужного (возьмите сборку CUDA 12.4 или Vulkan: `install.ps1 -Llm -Gpu vulkan -Force`) |
| `Grammar (Edge): Microsoft Edge not found` | нет Edge Canary (и обычного Edge) — `install.ps1 -EdgeCanary`, или путь в `edgePath` неверный |
| `Grammar (Edge): … has no proofreader` или `… does not answer (try Edge Canary)` | выбран Edge без работающего корректора (обычный Edge 154: объект есть, но не отвечает), или обновление его убрало — поставьте Canary или укажите в `edgePath` другую сборку |
| `Grammar (Edge): … unavailable on this computer` | компьютер не проходит по требованиям Microsoft (`edge://on-device-internals`) |
| долго `starting` | первый запуск: LanguageTool 5–12 с, модель 3–10 с, корректор Edge при первом включении скачивает модель (~1 мин) |
| первый ответ модели очень медленный (10–20 с) | сборка Vulkan компилирует шейдеры при первом запуске; на NVIDIA быстрее сборка CUDA |
| модель отвечает переводом или пересказом | промпт BlueEdit это запрещает, пересказ отбрасывается защитой; со слишком коротким промптом YandexGPT действительно переводит на английский; своя модель может не подойти — прогоните замер |
| `Settings: … set twice` | настройка записана в `settings.json` дважды — оставьте одну |
