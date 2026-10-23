# BlueEdit на Linux

English: [linux.md](linux.md).

Тот же редактор, что на Windows, собранный для Linux x64 (один самодостаточный файл, .NET не нужен). Работает в любом
терминале с обычными VT-последовательностями — GNOME Terminal, Konsole, xterm, kitty, WezTerm, Windows Terminal с WSL,
терминал по SSH. Статус: предварительная версия. Что проверено и где — сказано ниже; что не проверено — тоже.

## Установка

Из релиза: скачать `blueedit-<версия>-linux-x64.tar.gz` (и сверить его SHA-256 с описанием релиза), затем

```bash
tar xzf blueedit-*-linux-x64.tar.gz
cd blueedit-*-linux-x64
./be --version     # печатает версию: сборка здесь запускается
./be notes.md
```

При желании положить в PATH (`ln -s "$PWD/be" ~/.local/bin/be`). Из исходников: `.\tasks.ps1 publish-linux`
(на Windows: сборка и такой же архив в `publish-linux\`) или `dotnet publish -c Release -r linux-x64 --self-contained
-p:PublishSingleFile=true -o out` на Linux.

## Что есть на Linux и как

| Часть | На Linux |
|---|---|
| Редактор, меню, диалоги, мышь, живой просмотр Markdown | как на Windows: у терминального слоя есть часть для Unix (сырой режим termios) |
| Орфография | **Hunspell**: **Tools → Get spelling dictionaries…** скачивает словари LibreOffice в `~/.local/share/BlueEdit/hunspell` (без root; в командной строке `be --install-proofing ru-RU,en-GB`), или словари дистрибутива: `sudo apt install hunspell-ru hunspell-en-gb hunspell-en-us` (ставятся в `/usr/share/hunspell`; читаются также `~/.local/share/hunspell`, `/usr/share/myspell`, `/usr/local/share/hunspell`, `/opt/homebrew/share/hunspell`, `~/Library/Spelling`) |
| Грамматика: LanguageTool, локальная языковая модель; советы по стилю | `tools/proofing-setup/install.sh --all` (Java, LanguageTool, llama.cpp — сборка CUDA, Vulkan или для процессора — и модель, в `~/.local/share/BlueEdit`) |
| Языковые серверы (Python, C#), ruff | **Tools → Code tools** в редакторе или `be --install-tools` (в `~/.local/share/blueedit/tools`, без root и apt: годится и для NAS); или `tools/lsp-setup/install.sh --all` (apt, sudo) |
| Буфер обмена | `wl-copy`/`wl-paste` (Wayland), `xclip` или `xsel` (X11), `pbcopy` (macOS) — первый, что работает; если нет ни одного (консоль, SSH), копия уходит в терминал через OSC 52, и большинство терминалов кладут её в свой буфер |
| Настройки, личный словарь | `~/.config/blueedit/settings.json`, `dictionary.txt` (`$XDG_CONFIG_HOME/blueedit`) |
| Шрифт терминала | задаётся в настройках самого терминала; **Tools → Terminal font** так и говорит (профиль Windows Terminal — только для Windows) |
| Орфография Microsoft Word, корректор Edge | только Windows |

## WSL

Всё перечисленное работает в WSL (проверено в Ubuntu 24.04 на Windows 11): редактор во вкладке WSL в Windows Terminal,
Hunspell, LanguageTool, модель на видеокарте NVIDIA компьютера (CUDA-сборка llama.cpp работает с драйвером Windows;
`install.sh` выбирает её, если работает `nvidia-smi`), языковые серверы. Буфер обмена общий с Windows через X11 из WSLg
(`sudo apt install xclip`); Wayland-сокет WSLg лежит не там, куда указывает `WAYLAND_DISPLAY`, поэтому BlueEdit
проверяет, что сокет есть, прежде чем звать `wl-copy`. Linux-версия пропускает папки Windows в `PATH` (`/mnt/c/…`).

## Что проверено и чего ещё нет

Проверено в WSL (Ubuntu 24.04, RTX 4070): юнит-тесты (на Linux проходят все, один — только для Windows — пропускается);
редактор в псевдотерминале — рисование, набор кириллицы, стрелки, сохранение, выход, подчёркивание опечаток и F7,
грамматика LanguageTool и Phi-4-mini подчёркнута за 7 с, F7 показывает обе; копирование в буфер Windows; тесты с
настоящими LanguageTool, моделью, pyright и csharp-ls; оба скрипта установки от начала до конца; весь набор UI-тестов
на Linux-версии (`tools/ci/check-linux.sh`, 5 октября 2026: 169 прошли, 5 пропущены: четыре проверяют то, что есть
только в Windows — профиль Windows Terminal, его шрифт, буфер обмена Windows, — и одному нужен Word). Каждый PR
проверяется и на Linux-раннере CI (`docs/ci.md`).

Проверено на рабочем столе Linux (6 октября 2026, #89): Ubuntu 26.04.1 Desktop (GNOME на Wayland) в VirtualBox, сборка
из `tasks publish-linux`:
- в терминале GNOME (Ptyxis): рисование, меню, кириллица, подчёркивания Hunspell (`hunspell-ru`, `hunspell-en-gb`),
  режим чтения с выносной формулой в несколько строк; `--version`;
- буфер обмена Wayland в обе стороны (`sudo apt install wl-clipboard`): Ctrl+A, Ctrl+C в редакторе — и `wl-paste`
  выдаёт текст, кириллица цела; текст, положенный в буфер другой программой, вставляется по Ctrl+V. (`wl-copy`,
  запущенный из SSH-сессии, не захватывает буфер GNOME: у него нет окна в фокусе. Это правило GNOME, не редактора:
  по SSH редактор копирует через OSC 52.)
- по SSH из Windows (`ssh.exe` в псевдоконсоли Windows, как его запускает Windows Terminal, на виртуальную машину):
  F9 и Esc, Alt+F, стрелки — по десять повторов, ни одна клавиша не потерялась и не рассыпалась в текст; также с
  задержкой 60 ± 40 мс и 200 ± 150 мс на каждом входящем в машину пакете (`tc netem` за `ifb`) — больше паузы в 25 мс,
  которой одиночный Esc отличается от начала последовательности клавиши. Последовательность клавиши приходит целиком;
  паузу менять не стали. Копирование по SSH отправляет OSC 52 с текстом (видно в выводе); кладёт ли его затем Windows
  Terminal в свой буфер — не проверялось.

Проверено на голых системах (23 октября 2026; `tasks newcomer-linux`, docs/ci.md): чистые контейнеры Debian 12,
Ubuntu 24.04 и Fedora 41, ничего не установлено. Что редактору нужно от системы и что он делает без этого:
- **ICU (libicu)**, языковые данные: без неё редактор работает в инвариантном режиме .NET. Набор, кириллица и регистр
  букв работают; языки называются тегами (`ru-RU` вместо «Русский»). В трёх образах её нет, нет и на минимальном
  сервере Ubuntu 24.04 (его облачный образ, проверено в виртуальной машине); в облачном образе Debian 12 она есть, как
  и на рабочих столах и наших серверах (`libicu74` в Ubuntu, своя копия на NAS в `/usr/builtin/lib`).
- **OpenSSL (libssl) и корневые сертификаты** — для загрузки словарей, проверяющих, моделей и инструментов кода по
  https: без них окно говорит, что поставить (`sudo apt install openssl ca-certificates`), а редактор работает дальше.
  В образе Debian 12 нет libssl, в Ubuntu 24.04 — сертификатов, в Fedora 41 есть и то и другое.

Пока нет:
- macOS (должна собираться, ничего не запускалось); рабочих столов на X11 (пока только GNOME на Wayland);
- если редактор убит, LanguageTool, llama-server и языковые серверы остаются работать (на Windows их завершает объект
  задания; при обычном выходе они завершаются и на Linux).

План и его состояние — задача #29.
