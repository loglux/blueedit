; BlueEdit's installer for Windows (Inno Setup 6): docs/tech/installer.md says why it is made this way. Built by
; tools/installer/build.ps1 (tasks installer, the release workflow) from a published be.exe:
;   ISCC /DAppVersion=0.4.0 /DSource=<the folder with be.exe> /DOutput=<a folder> tools\installer\blueedit.iss
; For the current user only, no administrator: the checkers and models the editor downloads live in the user's
; %LOCALAPPDATA%\BlueEdit anyway. The grammar checkers (a page of their own: models by the video card, or a model
; server) and the code tools (on the tasks page) are downloaded by be.exe itself (be --install-proofing,
; be --install-tools), from the same sources and with the same checksums as Tools > Set up grammar checking and
; Tools > Code tools, and turned on by it (be --grammar, be --model-server); InstallerTests keeps the names here in
; step with the code. Silently: /GRAMMAR=harper,languagetool,yandexgpt.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Source
  #define Source "..\..\publish"
#endif
#ifndef MathImages
  #define MathImages "..\..\publish-mathimages"
#endif
#ifndef Output
  #define Output "..\..\publish-installer"
#endif

; the names be --install-proofing and be --install-tools take (ProofingTools.Models, CodeTools.All); a task's or
; message's name is the same with "_" for "-"
#define Models "qwen3-8b yandexgpt gemma3-12b gemma3-4b phi4-mini"
#define CodeTools "pyright ruff csharp-ls typescript"

[Setup]
AppId={{6A3F1C2E-8B4D-4E7A-9C51-2F0D7B8E4A13}
AppName=BlueEdit
AppVersion={#AppVersion}
AppVerName=BlueEdit {#AppVersion}
AppPublisher=Vladislav Sorokin
AppPublisherURL=https://github.com/loglux/blueedit
AppSupportURL=https://github.com/loglux/blueedit/issues
VersionInfoVersion={#AppVersion}
LicenseFile=..\..\LICENSE
DefaultDirName={autopf}\BlueEdit
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; the console's escape sequences the editor draws and reads with came with Windows 10
MinVersion=10.0
OutputDir={#Output}
OutputBaseFilename=blueedit-{#AppVersion}-win-x64-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=auto
UninstallDisplayIcon={app}\be.exe
UninstallDisplayName=BlueEdit
ChangesEnvironment=yes
CloseApplications=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
en.TaskPath=Add the command "be" to PATH: open files from any console (be notes.md)
ru.TaskPath=Добавить команду «be» в PATH: открывать файлы из любой консоли (be notes.md)
en.TaskMathImages=Formula pictures: display formulas typeset in Windows Terminal (the mathimages module, ~14 MB)
ru.TaskMathImages=Картинки формул: выносные формулы набраны, как в LaTeX, в Windows Terminal (модуль mathimages, ~14 МБ)
en.TaskOpenWith=Add "Open with BlueEdit" to the menu of files in Explorer
ru.TaskOpenWith=Добавить «Открыть в BlueEdit» в меню файлов в Проводнике
en.OpenWith=Open with BlueEdit
ru.OpenWith=Открыть в BlueEdit
en.GrammarTitle=Grammar checking
ru.GrammarTitle=Проверка грамматики
en.GrammarSubtitle=Nothing here is final: all of it can be changed later in the editor.
ru.GrammarSubtitle=Здесь ничего не окончательно: всё можно поменять потом в редакторе.
en.GroupRules=Rules: no video card needed
ru.GroupRules=Правила: видеокарта не нужна
en.Harper=Harper: English
ru.Harper=Harper: английский
en.HarperSize=~60 MB
ru.HarperSize=~60 МБ
en.HarperNote=Harper: fast rules for English, from its publisher on GitHub (Apache 2.0). Finds less than a language model.
ru.HarperNote=Harper: быстрые правила для английского, с GitHub авторов (Apache 2.0). Находит меньше языковой модели.
en.LanguageTool=LanguageTool: 31 languages, Russian among them
ru.LanguageTool=LanguageTool: 31 язык, в том числе русский
en.LanguageToolSize=~530 MB
ru.LanguageToolSize=~530 МБ
en.LanguageToolNote=LanguageTool: rules for 31 languages, with a Java of its own (LGPL). Finds less than a language model.
ru.LanguageToolNote=LanguageTool: правила для 31 языка, со своей Java (LGPL). Находит меньше языковой модели.
en.GroupModel=A language model here: finds the most (BlueEdit installs llama.cpp)
ru.GroupModel=Языковая модель здесь: находит больше всех (llama.cpp ставит BlueEdit)
en.ModelGroupNote=llama.cpp and a model on this computer, run on its video card, both downloaded and set up by BlueEdit.
ru.ModelGroupNote=llama.cpp и модель на этом компьютере, на его видеокарте; скачивает и настраивает их BlueEdit.
en.CardMemory=%1, %2 GB
ru.CardMemory=%1, %2 ГБ
en.CardNone=no video card found
ru.CardNone=видеокарта не найдена
en.CardUnknown=the video card could not be checked
ru.CardUnknown=видеокарту проверить не удалось
en.NoModel=None
ru.NoModel=Не нужна
en.NoModelNote=No language model: the rules above, or nothing.
ru.NoModelNote=Без языковой модели: только правила выше или ничего.
en.Recommended=recommended
ru.Recommended=рекомендуется
en.Needs=needs ~%1 GB of video memory
ru.Needs=нужно ~%1 ГБ видеопамяти
en.qwen3_8b=Qwen3 8B, 4.7 GB
ru.qwen3_8b=Qwen3 8B, 4,7 ГБ
en.qwen3_8b_note=Qwen3 8B: many languages; wants ~6 GB of video memory; Apache 2.0.
ru.qwen3_8b_note=Qwen3 8B: много языков; нужно ~6 ГБ видеопамяти; Apache 2.0.
en.yandexgpt=YandexGPT 5 Lite 8B, 4.6 GB
ru.yandexgpt=YandexGPT 5 Lite 8B, 4,6 ГБ
en.yandexgpt_note=YandexGPT 5 Lite 8B: the best at Russian here; wants ~6 GB of video memory; Yandex's own licence.
ru.yandexgpt_note=YandexGPT 5 Lite 8B: лучше всех по-русски; нужно ~6 ГБ видеопамяти; лицензия Яндекса.
en.gemma3_12b=Gemma 3 12B, 6.8 GB
ru.gemma3_12b=Gemma 3 12B, 6,8 ГБ
en.gemma3_12b_note=Gemma 3 12B: many languages; wants ~8 GB of video memory; Gemma terms (Google).
ru.gemma3_12b_note=Gemma 3 12B: много языков; нужно ~8 ГБ видеопамяти; условия Gemma (Google).
en.gemma3_4b=Gemma 3 4B, 2.3 GB
ru.gemma3_4b=Gemma 3 4B, 2,3 ГБ
en.gemma3_4b_note=Gemma 3 4B: for small video cards, ~3 GB of video memory; Gemma terms (Google).
ru.gemma3_4b_note=Gemma 3 4B: для небольших видеокарт, ~3 ГБ видеопамяти; условия Gemma (Google).
en.phi4_mini=Phi-4 mini, 2.3 GB
ru.phi4_mini=Phi-4 mini, 2,3 ГБ
en.phi4_mini_note=Phi-4 mini: the fastest and the weakest, ~3 GB of video memory; MIT.
ru.phi4_mini_note=Phi-4 mini: самая быстрая и самая слабая, ~3 ГБ видеопамяти; MIT.
en.GroupServer=Or a model somewhere else
ru.GroupServer=Или модель в другом месте
en.Ollama=Ollama, already installed
ru.Ollama=Ollama, уже установленная
en.OllamaNote=Ollama (ollama.com) on this or another computer: its address comes next; the model is the one it has (another: Tools > Grammar model). No key.
ru.OllamaNote=Ollama (ollama.com) на этом или другом компьютере: адрес — дальше; модель берётся та, что в ней есть (другую — Инструменты > Модель грамматики). Ключ не нужен.
en.LmStudio=LM Studio, already installed
ru.LmStudio=LM Studio, уже установленная
en.LmStudioNote=LM Studio (lmstudio.ai) with its server on (the Developer tab): its address comes next; the model is the one it has. No key.
ru.LmStudioNote=LM Studio (lmstudio.ai) с включённым сервером (вкладка Developer): адрес — дальше; модель берётся та, что в нём есть. Ключ не нужен.
en.LlamaServer=llama-server you run yourself
ru.LlamaServer=llama-server, запущенный вами
en.LlamaServerNote=llama.cpp's server on this or another computer, e.g. one with a bigger video card: its address, and its key if it was given one.
ru.LlamaServerNote=Сервер llama.cpp на этом или другом компьютере, например с видеокартой побольше: его адрес и ключ, если он задан.
en.OpenAI=OpenAI (ChatGPT's maker): cloud, with a key
ru.OpenAI=OpenAI (создатели ChatGPT): облако, по ключу
en.OpenAINote=Paragraphs are sent to OpenAI. The key: platform.openai.com, paid by use. gpt-5.4-mini is offered.
ru.OpenAINote=Абзацы отправляются в OpenAI. Ключ — на platform.openai.com, оплата по использованию. Предложена gpt-5.4-mini.
en.Claude=Claude (Anthropic): cloud, with a key
ru.Claude=Claude (Anthropic): облако, по ключу
en.ClaudeNote=Paragraphs are sent to Anthropic. The key: console.anthropic.com, paid by use. claude-haiku-5-5 is offered.
ru.ClaudeNote=Абзацы отправляются в Anthropic. Ключ — на console.anthropic.com, оплата по использованию. Предложена claude-haiku-5-5.
en.Gemini=Gemini (Google): cloud, with a key
ru.Gemini=Gemini (Google): облако, по ключу
en.GeminiNote=Paragraphs are sent to Google. The key: aistudio.google.com; on the free tier Google may use what is sent. gemini-3.5-flash-lite is offered.
ru.GeminiNote=Абзацы отправляются в Google. Ключ — на aistudio.google.com; на бесплатном тарифе Google может использовать отправленное. Предложена gemini-3.5-flash-lite.
en.DeepSeek=DeepSeek: cloud, with a key
ru.DeepSeek=DeepSeek: облако, по ключу
en.DeepSeekNote=Paragraphs are sent to DeepSeek. The key: platform.deepseek.com, with a balance topped up. deepseek-flash is offered.
ru.DeepSeekNote=Абзацы отправляются в DeepSeek. Ключ — на platform.deepseek.com, баланс пополняется заранее. Предложена deepseek-flash.
en.OtherCloud=Another service with a key
ru.OtherCloud=Другой сервис по ключу
en.OtherCloudNote=Any service with an API like OpenAI's: its address is in its documentation; the model is chosen later from its list (Tools > Grammar model).
ru.OtherCloudNote=Любой сервис с API как у OpenAI: адрес — в его документации; модель выбирается потом из его списка (Инструменты > Модель грамматики).
en.ServerLater=All of this can be changed later: Tools > Set up grammar checking.
ru.ServerLater=Всё это можно поменять потом: Инструменты > Настроить проверку грамматики.
en.ServerKeyKept=The key is kept apart, encrypted for your Windows account.
en.ServerNoKeyYet=The key may be left empty and given later in the editor.
en.Checking=Checking the server...
en.CheckKey=%1 did not take the key: %2
en.CheckUnreachable=%1 does not answer: %2
en.CheckOther=%1 answered: %2
en.CheckKeep=%1%n%nKeep it as it is anyway? (No: back to fix it)
ru.ServerKeyKept=Ключ хранится отдельно, зашифрованным для вашей учётной записи Windows.
ru.ServerNoKeyYet=Ключ можно не вводить сейчас и ввести потом в редакторе.
ru.Checking=Проверяю сервер...
ru.CheckKey=%1 не принял ключ: %2
ru.CheckUnreachable=%1 не отвечает: %2
ru.CheckOther=%1 ответил: %2
ru.CheckKeep=%1%n%nСохранить всё равно так? (Нет — вернуться и исправить)
en.ServerTitle=A model somewhere else
ru.ServerTitle=Модель в другом месте
en.ServerSubtitle=Where grammar checking sends paragraphs.
ru.ServerSubtitle=Куда проверка грамматики отправляет абзацы.
en.ServerAddress=Address:
ru.ServerAddress=Адрес:
en.ServerKey=Key:
ru.ServerKey=Ключ:
en.ServerMissing=The address is needed.
ru.ServerMissing=Нужен адрес.
en.ReadyGrammar=Grammar checking:
ru.ReadyGrammar=Проверка грамматики:
en.GroupCode=Language servers for code, into BlueEdit's own folder (Tools > Code tools adds them later too):
ru.GroupCode=Языковые серверы для кода, в папку BlueEdit (их можно добавить и потом: Инструменты > Инструменты для кода):
en.Code=Errors, completion and formatting for code:
ru.Code=Ошибки, подсказки и форматирование для кода:
en.pyright=pyright: Python, with Node.js (250 MB)
ru.pyright=pyright: Python, с Node.js (250 МБ)
en.ruff=ruff: formatting Python (23 MB)
ru.ruff=ruff: форматирование Python (23 МБ)
en.csharp_ls=csharp-ls: C#, with the .NET SDK (720 MB)
ru.csharp_ls=csharp-ls: C#, с .NET SDK (720 МБ)
en.typescript=typescript: JavaScript and TypeScript (27 MB)
ru.typescript=typescript: JavaScript и TypeScript (27 МБ)
en.Downloading=Downloading what was chosen; a console window shows how it goes...
ru.Downloading=Скачивается выбранное; ход виден в окне консоли...
en.SomeFailed=Some of what was chosen could not be installed: the console window said what. BlueEdit itself is installed; Tools > Set up grammar checking and Tools > Code tools can try again.
ru.SomeFailed=Часть выбранного поставить не удалось: в окне консоли было сказано, что именно. Сам BlueEdit установлен; Инструменты > Настроить проверку грамматики и Инструменты > Инструменты для кода могут попробовать ещё раз.
en.RemoveData=Also remove what BlueEdit downloaded (grammar checkers, models, code tools)?%n%n%1%n%nYour settings and dictionary stay in %2.
ru.RemoveData=Удалить и то, что скачал BlueEdit (проверку грамматики, модели, серверы для кода)?%n%n%1%n%nНастройки и словарь останутся в %2.

[Tasks]
Name: path; Description: {cm:TaskPath}
Name: openwith; Description: {cm:TaskOpenWith}
Name: mathimages; Description: {cm:TaskMathImages}
Name: desktopicon; Description: {cm:CreateDesktopIcon}; Flags: unchecked
Name: code; Description: {cm:Code}; GroupDescription: {cm:GroupCode}; Flags: unchecked
Name: code\pyright; Description: {cm:pyright}; Flags: unchecked
Name: code\ruff; Description: {cm:ruff}; Flags: unchecked
Name: code\csharp_ls; Description: {cm:csharp_ls}; Flags: unchecked
Name: code\typescript; Description: {cm:typescript}; Flags: unchecked

[Files]
Source: "{#Source}\be.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\proofing-setup\*"; DestDir: "{app}\tools\proofing-setup"; Flags: ignoreversion recursesubdirs
Source: "..\lsp-setup\*"; DestDir: "{app}\tools\lsp-setup"; Flags: ignoreversion recursesubdirs
; the formula pictures module beside be.exe, where the editor looks first (docs/formula-pictures.md)
Source: "{#MathImages}\*"; DestDir: "{app}\mathimages"; Flags: ignoreversion recursesubdirs; Tasks: mathimages
Source: "..\..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
; installed again without it: the module of an earlier install goes
Type: filesandordirs; Name: "{app}\mathimages"; Tasks: not mathimages

[Icons]
Name: "{autoprograms}\BlueEdit"; Filename: "{app}\be.exe"; WorkingDir: "{userdocs}"
Name: "{autodesktop}\BlueEdit"; Filename: "{app}\be.exe"; WorkingDir: "{userdocs}"; Tasks: desktopicon

[Registry]
; Win+R "be": App Paths finds the program without PATH
Root: HKA; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\be.exe"; ValueType: string; ValueName: ""; ValueData: "{app}\be.exe"; Flags: uninsdeletekey
; Explorer's "Open with" list: the program and the kinds of file it is offered for
Root: HKA; Subkey: "Software\Classes\Applications\be.exe"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "BlueEdit"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\be.exe\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\be.exe"" ""%1"""
Root: HKA; Subkey: "Software\Classes\Applications\be.exe\SupportedTypes"; ValueType: string; ValueName: ".txt"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\be.exe\SupportedTypes"; ValueType: string; ValueName: ".md"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\be.exe\SupportedTypes"; ValueType: string; ValueName: ".markdown"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\be.exe\SupportedTypes"; ValueType: string; ValueName: ".log"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\be.exe\SupportedTypes"; ValueType: string; ValueName: ".json"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\be.exe\SupportedTypes"; ValueType: string; ValueName: ".ini"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\be.exe\SupportedTypes"; ValueType: string; ValueName: ".py"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\be.exe\SupportedTypes"; ValueType: string; ValueName: ".cs"; ValueData: ""
; "Open with BlueEdit" on every file
Root: HKA; Subkey: "Software\Classes\*\shell\BlueEdit"; ValueType: string; ValueName: ""; ValueData: "{cm:OpenWith}"; Flags: uninsdeletekey; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\*\shell\BlueEdit"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\be.exe"; Tasks: openwith
Root: HKA; Subkey: "Software\Classes\*\shell\BlueEdit\command"; ValueType: string; ValueName: ""; ValueData: """{app}\be.exe"" ""%1"""; Tasks: openwith

[Run]
Filename: "{app}\be.exe"; Description: "{cm:LaunchProgram,BlueEdit}"; WorkingDir: "{userdocs}"; Flags: nowait postinstall skipifsilent

[Code]
const
  EnvKey = 'Environment';

{ The user's PATH (HKCU\Environment, kept as REG_EXPAND_SZ so its %variables% stay) with or without a folder. }
function SameFolder(A, B: string): Boolean;
begin
  Result := CompareText(RemoveBackslashUnlessRoot(Trim(A)), RemoveBackslashUnlessRoot(Trim(B))) = 0;
end;

procedure SetInPath(Dir: string; Wanted: Boolean);
var
  Paths, Rest, Part, Kept: string;
  P: Integer;
  Found: Boolean;
begin
  if not RegQueryStringValue(HKCU, EnvKey, 'Path', Paths) then Paths := '';
  Rest := Paths + ';';
  Kept := '';
  Found := False;
  while Rest <> '' do
  begin
    P := Pos(';', Rest);
    Part := Copy(Rest, 1, P - 1);
    Delete(Rest, 1, P);
    if Part = '' then continue;
    if SameFolder(Part, Dir) then
    begin
      Found := True;
      if not Wanted then continue;
    end;
    if Kept <> '' then Kept := Kept + ';';
    Kept := Kept + Part;
  end;
  if Wanted and not Found then
  begin
    if Kept <> '' then Kept := Kept + ';';
    RegWriteExpandStringValue(HKCU, EnvKey, 'Path', Kept + Dir);
  end
  else if not Wanted and Found then
    RegWriteExpandStringValue(HKCU, EnvKey, 'Path', Kept);
end;

{ The names (space-separated, as be.exe takes them) whose tasks are chosen, joined by commas; a task is Group\name
  with "_" for "-". }
function Chosen(Group, Names: string): string;
var
  Rest, Name, Task: string;
  P: Integer;
begin
  Result := '';
  Rest := Names + ' ';
  while Rest <> '' do
  begin
    P := Pos(' ', Rest);
    Name := Copy(Rest, 1, P - 1);
    Delete(Rest, 1, P);
    if Name = '' then continue;
    Task := Name;
    StringChangeEx(Task, '-', '_', True);
    if Group <> '' then Task := Group + '\' + Task;
    if WizardIsTaskSelected(Task) then
    begin
      if Result <> '' then Result := Result + ',';
      Result := Result + Name;
    end;
  end;
end;

{ be.exe with these arguments in a console window of its own, where its downloads show; when it fails the window
  waits for a key, so what failed can be read (not when installing silently: nobody would press it). }
function RunEditor(Args: string): Boolean;
var
  Line: string;
  Code: Integer;
begin
  Line := '"' + ExpandConstant('{app}\be.exe') + '" ' + Args;
  if not WizardSilent then Line := Line + ' || (pause & exit /b 1)';
  Result := Exec(ExpandConstant('{cmd}'), '/c "' + Line + '"', ExpandConstant('{app}'), SW_SHOW, ewWaitUntilTerminated, Code) and (Code = 0);
end;

{ be.exe for a quick answer, without a window; whether it went well }
function RunEditorQuietly(Args: string): Boolean;
var
  Code: Integer;
begin
  Result := Exec(ExpandConstant('{app}\be.exe'), Args, ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
end;

{ ---------- the grammar page: rules, a model here (by the video card) or a model server ----------
  be --video-card says which card there is and which models fit it (ProofingTools: an integrated card counts as
  none); it is asked of be.exe taken out of the installer into its temporary folder, when the page is first reached. }
var
  GrammarPage: TWizardPage;
  GrammarList: TNewCheckListBox;
  GrammarNote: TNewStaticText;
  ServerPage: TInputQueryWizardPage;
  ServerNote: TNewStaticText;    { under the fields: it can be changed later, how the key is kept }
  ItemNames: TArrayOfString;    { what each line stands for: harper, languagetool, a model, a server's kind, '' }
  ItemNotes: TArrayOfString;    { the line shown under the list for it }
  GrammarFilled: Boolean;

{ the key goes to be.exe in its environment, never on its command line }
function SetEnvironmentVariable(Name, Value: string): Boolean;
  external 'SetEnvironmentVariableW@kernel32.dll stdcall';

procedure Remember(Name, Note: string);
var
  N: Integer;
begin
  N := GetArrayLength(ItemNames);
  SetArrayLength(ItemNames, N + 1);
  SetArrayLength(ItemNotes, N + 1);
  ItemNames[N] := Name;
  ItemNotes[N] := Note;
end;

procedure GrammarListClick(Sender: TObject);
begin
  if (GrammarList.ItemIndex >= 0) and (GrammarList.ItemIndex < GetArrayLength(ItemNotes)) then
    GrammarNote.Caption := ItemNotes[GrammarList.ItemIndex];
end;

{ the line of be --video-card's answer that starts with Prefix, without it; '' when there is none }
function Answer(Lines: TArrayOfString; Prefix: string): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to GetArrayLength(Lines) - 1 do
    if Copy(Lines[I], 1, Length(Prefix)) = Prefix then
    begin
      Result := Copy(Lines[I], Length(Prefix) + 1, MaxInt);
      exit;
    end;
end;

{ A model server: a line of the list (its caption and note are the messages Message and Message + 'Note'). }
procedure AddServer(Name, Message: string);
begin
  GrammarList.AddRadioButton(CustomMessage(Message), '', 0, False, True, nil);
  Remember(Name, CustomMessage(Message + 'Note'));
end;

{ What a server is asked at: its address, the model offered (the one the Obsidian plugin's benchmark recommends; none
  for a server of the user's own, whose models are theirs), and whether it takes a key. }
procedure ServerDefaults(Kind: string; var Address, Model: string; var Key: Boolean);
begin
  Model := '';
  Key := True;
  if Kind = 'openai' then begin Address := 'https://api.openai.com/v1'; Model := 'gpt-5.4-mini'; end
  else if Kind = 'anthropic' then begin Address := 'https://api.anthropic.com/v1'; Model := 'claude-haiku-5-5'; end
  else if Kind = 'gemini' then begin Address := 'https://generativelanguage.googleapis.com/v1beta'; Model := 'gemini-3.5-flash-lite'; end
  else if Kind = 'deepseek' then begin Address := 'https://api.deepseek.com/v1'; Model := 'deepseek-flash'; end
  else if Kind = 'ollama' then begin Address := 'http://127.0.0.1:11434/v1'; Key := False; end
  else if Kind = 'lmstudio' then begin Address := 'http://127.0.0.1:1234/v1'; Key := False; end
  else if Kind = 'llamaserver' then Address := 'http://127.0.0.1:8080/v1'
  else Address := '';
end;

procedure FillGrammarList;
var
  Lines: TArrayOfString;
  Asked: Boolean;
  Code, P, MB: Integer;
  Card, Rest, Name, Task, Sub, Best, Need, AnswerFile: string;
begin
  GrammarFilled := True;
  ExtractTemporaryFile('be.exe');
  AnswerFile := ExpandConstant('{tmp}\video-card.txt');
  Asked := Exec(ExpandConstant('{cmd}'), '/c ""' + ExpandConstant('{tmp}\be.exe') + '" --video-card > "' + AnswerFile + '""',
    ExpandConstant('{tmp}'), SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0) and LoadStringsFromFile(AnswerFile, Lines)
    and (Answer(Lines, 'card ') <> '');
  Card := Answer(Lines, 'card ');
  if not Asked then Sub := CustomMessage('CardUnknown')
  else if Card = 'none' then Sub := CustomMessage('CardNone')
  else
  begin
    P := Pos(' ', Card);
    MB := StrToIntDef(Copy(Card, 1, P - 1), 0);
    Sub := FmtMessage(CustomMessage('CardMemory'), [Copy(Card, P + 1, MaxInt), IntToStr((MB + 512) div 1024)]);
  end;
  Best := Answer(Lines, 'recommended ');

  GrammarList.AddGroup(CustomMessage('GroupRules'), '', 0, nil);
  Remember('', '');
  GrammarList.AddCheckBox(CustomMessage('Harper'), CustomMessage('HarperSize'), 0, False, True, False, False, nil);
  Remember('harper', CustomMessage('HarperNote'));
  GrammarList.AddCheckBox(CustomMessage('LanguageTool'), CustomMessage('LanguageToolSize'), 0, False, True, False, False, nil);
  Remember('languagetool', CustomMessage('LanguageToolNote'));
  GrammarList.AddGroup(CustomMessage('GroupModel'), Sub, 0, nil);
  Remember('', CustomMessage('ModelGroupNote'));
  GrammarList.AddRadioButton(CustomMessage('NoModel'), '', 0, True, True, nil);
  Remember('', CustomMessage('NoModelNote'));
  Rest := '{#Models} ';
  while Rest <> '' do
  begin
    P := Pos(' ', Rest);
    Name := Copy(Rest, 1, P - 1);
    Delete(Rest, 1, P);
    if Name = '' then continue;
    Task := Name;
    StringChangeEx(Task, '-', '_', True);
    { a model the card has too little memory for (or no card) is shown greyed with what it needs; the card not
      checked: every model may be chosen, as in the editor }
    Need := Answer(Lines, 'needs ' + Name + ' ');
    if Need <> '' then Sub := FmtMessage(CustomMessage('Needs'), [Need])
    else if Name = Best then Sub := CustomMessage('Recommended')
    else Sub := '';
    GrammarList.AddRadioButton(CustomMessage(Task), Sub, 0, False, Need = '', nil);
    Remember(Name, CustomMessage(Task + '_note'));
  end;
  GrammarList.AddGroup(CustomMessage('GroupServer'), '', 0, nil);
  Remember('', '');
  AddServer('openai', 'OpenAI');
  AddServer('anthropic', 'Claude');
  AddServer('gemini', 'Gemini');
  AddServer('deepseek', 'DeepSeek');
  AddServer('cloud', 'OtherCloud');
  AddServer('ollama', 'Ollama');
  AddServer('lmstudio', 'LmStudio');
  AddServer('llamaserver', 'LlamaServer');
  GrammarNote.Caption := CustomMessage('ModelGroupNote');
end;

procedure InitializeWizard;
begin
  GrammarPage := CreateCustomPage(wpSelectTasks, CustomMessage('GrammarTitle'), CustomMessage('GrammarSubtitle'));
  GrammarNote := TNewStaticText.Create(GrammarPage);
  GrammarNote.Parent := GrammarPage.Surface;
  GrammarNote.AutoSize := False;
  GrammarNote.WordWrap := True;
  GrammarNote.Width := GrammarPage.SurfaceWidth;
  GrammarNote.Height := ScaleY(42);
  GrammarNote.Top := GrammarPage.SurfaceHeight - GrammarNote.Height;
  GrammarList := TNewCheckListBox.Create(GrammarPage);
  GrammarList.Parent := GrammarPage.Surface;
  GrammarList.Width := GrammarPage.SurfaceWidth;
  GrammarList.Height := GrammarNote.Top - ScaleY(8);
  GrammarList.OnClick := @GrammarListClick;
  ServerPage := CreateInputQueryPage(GrammarPage.ID, CustomMessage('ServerTitle'), CustomMessage('ServerSubtitle'), '');
  ServerPage.Add(CustomMessage('ServerAddress'), False);
  ServerPage.Add(CustomMessage('ServerKey'), True);
  ServerNote := TNewStaticText.Create(ServerPage);
  ServerNote.Parent := ServerPage.Surface;
  ServerNote.AutoSize := False;
  ServerNote.WordWrap := True;
  ServerNote.Width := ServerPage.SurfaceWidth;
  ServerNote.Height := ScaleY(64);
  ServerNote.Top := ServerPage.SurfaceHeight - ServerNote.Height;
end;

{ The model server chosen on the grammar page (openai, anthropic, …, llamaserver), or '' for none. }
function ServerKind: string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to GetArrayLength(ItemNames) - 1 do
    if GrammarList.Checked[I] and (ItemNames[I] <> '')
      and (Pos(' ' + ItemNames[I] + ' ', ' openai anthropic gemini deepseek cloud ollama lmstudio llamaserver ') > 0) then Result := ItemNames[I];
end;

function ServerChosen: Boolean;
begin
  Result := ServerKind <> '';
end;

{ The model kept for the server chosen: a cloud service's recommended one; none for a server of the user's own, where
  the editor takes the model the server has (Tools > Grammar model chooses another from its list). Nobody types one. }
function ServerModel: string;
var
  Address: string;
  Key: Boolean;
begin
  ServerDefaults(ServerKind, Address, Result, Key);
end;

var
  ServerFilledFor: string;

procedure CurPageChanged(CurPageID: Integer);
var
  Address, Model: string;
  Key: Boolean;
begin
  { the list in hand, as on the tasks page: arrows, Space }
  if CurPageID = GrammarPage.ID then WizardForm.ActiveControl := GrammarList;
  if CurPageID <> ServerPage.ID then exit;
  { the service's address and model offered, the key asked only of a server that takes one; what was typed stays when
    the same service is chosen again }
  ServerDefaults(ServerKind, Address, Model, Key);
  if ServerKind <> ServerFilledFor then
  begin
    ServerPage.Values[0] := Address;
    ServerPage.Values[1] := '';
    ServerFilledFor := ServerKind;
  end;
  ServerPage.PromptLabels[1].Visible := Key;
  ServerPage.Edits[1].Visible := Key;
  { a sentence a line: the label's own wrapping broke Russian words in two (seen in Windows Sandbox) }
  if Key then ServerNote.Caption := CustomMessage('ServerNoKeyYet') + #13#10 + CustomMessage('ServerKeyKept') + #13#10 + CustomMessage('ServerLater')
  else ServerNote.Caption := CustomMessage('ServerLater');
end;

{ A key typed, checked by be.exe (be --check-model-server: the service's list of models, free, a few seconds), so
  that a key mistyped is said now; True to go on: it answered well, or the user keeps it anyway (Ollama not started yet,
  no network now). The key goes in be.exe's environment, as when it is kept. }
function ServerChecked: Boolean;
var
  Lines: TArrayOfString;
  Code, P: Integer;
  Address, Model, Host, Why, AnswerFile: string;
begin
  Result := True;
  { only a key typed is checked: none typed, nothing is asked (it can be given later in the editor) }
  if not ServerPage.Edits[1].Visible or (Trim(ServerPage.Values[1]) = '') then exit;
  Address := Trim(ServerPage.Values[0]);
  Model := ServerModel;
  StringChangeEx(Address, '"', '', True);
  StringChangeEx(Model, '"', '', True);
  AnswerFile := ExpandConstant('{tmp}\server-check.txt');
  if ServerPage.Edits[1].Visible then SetEnvironmentVariable('BLUEEDIT_LLM_KEY', Trim(ServerPage.Values[1]))
  else SetEnvironmentVariable('BLUEEDIT_LLM_KEY', '');
  WizardForm.NextButton.Caption := CustomMessage('Checking');
  try
    Exec(ExpandConstant('{cmd}'), '/c ""' + ExpandConstant('{tmp}\be.exe') + '" --check-model-server "' + Address + '" "' + Model + '" > "' + AnswerFile + '""',
      ExpandConstant('{tmp}'), SW_HIDE, ewWaitUntilTerminated, Code);
  finally
    SetEnvironmentVariable('BLUEEDIT_LLM_KEY', '');
    WizardForm.NextButton.Caption := SetupMessage(msgButtonNext);
  end;
  { the model is not held to here: what a service has is for the editor's set-up to show (Tools > Grammar model) }
  if not LoadStringsFromFile(AnswerFile, Lines) or (GetArrayLength(Lines) = 0) or (Lines[0] = 'ok') or (Lines[0] = 'model') then exit;
  Host := Address;
  P := Pos('://', Host);
  if P > 0 then Delete(Host, 1, P + 2);
  P := Pos('/', Host);
  if P > 0 then Host := Copy(Host, 1, P - 1);
  if GetArrayLength(Lines) > 1 then Why := Lines[1] else Why := '';
  if Lines[0] = 'key' then Why := FmtMessage(CustomMessage('CheckKey'), [Host, Why])
  else if Lines[0] = 'unreachable' then Why := FmtMessage(CustomMessage('CheckUnreachable'), [Host, Why])
  else Why := FmtMessage(CustomMessage('CheckOther'), [Host, Why]);
  Result := MsgBox(FmtMessage(CustomMessage('CheckKeep'), [Why]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  { asked on leaving the tasks page, so the grammar page comes up filled (a second or two the first time) }
  if (CurPageID = wpSelectTasks) and not GrammarFilled then FillGrammarList;
  if (CurPageID = ServerPage.ID) and (Trim(ServerPage.Values[0]) = '') then
  begin
    MsgBox(CustomMessage('ServerMissing'), mbError, MB_OK);
    Result := False;
  end
  else if CurPageID = ServerPage.ID then Result := ServerChecked;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = ServerPage.ID) and not ServerChosen;
end;

{ What to install (be --install-proofing's names, by commas) and which checkers to turn on (the "grammar" setting):
  from the page, or when installing silently from /GRAMMAR=harper,languagetool,yandexgpt. }
procedure GrammarChosen(var Proofing, Engines: string);
var
  I: Integer;
  Rest, Name: string;
begin
  Proofing := '';
  Engines := '';
  if WizardSilent then Rest := ExpandConstant('{param:GRAMMAR|}') + ','
  else
  begin
    Rest := '';
    for I := 0 to GetArrayLength(ItemNames) - 1 do
      if (ItemNames[I] <> '') and GrammarList.Checked[I] then Rest := Rest + ItemNames[I] + ',';
  end;
  while Rest <> '' do
  begin
    I := Pos(',', Rest);
    Name := Lowercase(Trim(Copy(Rest, 1, I - 1)));
    Delete(Rest, 1, I);
    if Name = '' then continue;
    if Pos(' ' + Name + ' ', ' openai anthropic gemini deepseek cloud ollama lmstudio llamaserver ') = 0 then
    begin
      if Proofing <> '' then Proofing := Proofing + ',';
      Proofing := Proofing + Name;
    end;
    if Engines <> '' then Engines := Engines + ',';
    if (Name = 'harper') or (Name = 'languagetool') then Engines := Engines + Name else Engines := Engines + 'llm';
  end;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo,
  MemoGroupInfo, MemoTasksInfo: String): String;
var
  I: Integer;
  Grammar: string;
begin
  Grammar := '';
  for I := 0 to GetArrayLength(ItemNames) - 1 do
    if (ItemNames[I] <> '') and GrammarList.Checked[I] then
    begin
      Grammar := Grammar + NewLine + Space + GrammarList.ItemCaption[I];
      if ItemNames[I] = ServerKind then
      begin
        Grammar := Grammar + ': ' + Trim(ServerPage.Values[0]);
        if ServerModel <> '' then Grammar := Grammar + ', ' + ServerModel;
      end;
    end;
  Result := MemoDirInfo;
  if MemoTasksInfo <> '' then Result := Result + NewLine + NewLine + MemoTasksInfo;
  if Grammar <> '' then Result := Result + NewLine + NewLine + CustomMessage('ReadyGrammar') + Grammar;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Proofing, Engines, Tools, Address, Model: string;
  Failed: Boolean;
begin
  if CurStep <> ssPostInstall then exit;
  SetInPath(ExpandConstant('{app}'), WizardIsTaskSelected('path'));
  GrammarChosen(Proofing, Engines);
  Tools := Chosen('code', '{#CodeTools}');
  if (Engines = '') and (Tools = '') then exit;
  WizardForm.StatusLabel.Caption := CustomMessage('Downloading');
  Failed := False;
  if (Proofing <> '') and not RunEditor('--install-proofing ' + Proofing) then Failed := True;
  if not WizardSilent and ServerChosen then
  begin
    Address := Trim(ServerPage.Values[0]);
    Model := ServerModel;
    StringChangeEx(Address, '"', '', True);
    StringChangeEx(Model, '"', '', True);
    if ServerPage.Edits[1].Visible then SetEnvironmentVariable('BLUEEDIT_LLM_KEY', Trim(ServerPage.Values[1]));
    if not RunEditorQuietly('--model-server "' + Address + '" "' + Model + '"') then Failed := True;
    SetEnvironmentVariable('BLUEEDIT_LLM_KEY', '');
  end;
  { the checkers chosen are turned on once they are there; one that failed is left for Tools > Set up grammar checking }
  if (Engines <> '') and not Failed then RunEditorQuietly('--grammar ' + Engines);
  if (Tools <> '') and not RunEditor('--install-tools ' + Tools) then Failed := True;
  if Failed then SuppressibleMsgBox(CustomMessage('SomeFailed'), mbInformation, MB_OK, IDOK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Data: string;
begin
  if CurUninstallStep <> usUninstall then exit;
  SetInPath(ExpandConstant('{app}'), False);
  { what the editor downloaded stays unless the user says otherwise: models are gigabytes, slow to fetch again }
  Data := ExpandConstant('{localappdata}\BlueEdit');
  if DirExists(Data) and (SuppressibleMsgBox(FmtMessage(CustomMessage('RemoveData'), [Data, ExpandConstant('{userappdata}\blueedit')]),
    mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES) then
    DelTree(Data, True, True, True);
end;
