# SmoothMice — release notes

Antes de alterar `<Version>` em `Directory.Build.props`, lê este ficheiro. Cada versão nova deve ter **secção própria** (mais recente em cima). Resume alterações reais (diff pendente ou commit) em bullets.

---

## 2.2.3 — 2026-09-23

### Fixed — "Start smoothing" / animation easing curve and settings

- **High "Start smoothing" values made scrolls stop dead.** The easing curve is cut at the end of
  the animation time, and its deceleration tail only reached near-zero velocity when the
  acceleration phase was ≤ ¼ of the animation. With Start smoothing ≥ Animation time the curve
  was pure acceleration and ended at *peak* velocity (≈37% of peak at half the time). The
  acceleration phase is now capped at half the animation time, and short tails decay faster
  (velocity stays continuous) so every scroll eases out to ≤ ~5% of peak. Curves for the default
  settings (and any Start smoothing ≤ ¼ of Animation time) are unchanged.
- **Scrolling across windows with different profiles could jerk or briefly scroll backwards.**
  Every tick reshaped all in-flight notches with the settings of the *latest* notch; each notch
  now keeps the animation time, easing and curve it was pushed with.
- **Settings UI:** "Start smoothing" is disabled when Animation easing is off, and "Tail / head
  ratio" is disabled when it has no effect (easing off, or Start smoothing set explicitly).

## 2.2.2 — 2026-09-22

### Fixed — 2.2.1's fix for the oversized first-launch window was wrong; root cause and real fix

- 2.2.1 claimed to fix the main window sometimes showing much wider on first open, but the
  actual reported repro path (app auto-starts hidden in the tray, then the user opens it from
  the tray icon for the first time in that session) still showed the bug afterward.
- Root cause confirmed with live measurements against the running app (`GetWindowRect` /
  `GetWindowPlacement`, and a real STA-process repro of the exact `/tray` startup sequence): a
  window first created while minimized/hidden (the normal case, since the app launches with
  `/tray` on login) never gets a real layout pass against actual content — WPF does not resize
  the underlying HWND while minimized. Windows leaves the HWND with an arbitrary large default
  "restore" rect (`GetWindowPlacement` showed `rcNormalPosition` as wide as a full monitor width
  in one capture). When the tray's "Open" handler later flips `WindowState` to `Normal`, that
  stale rect becomes `ActualWidth`/`ActualHeight` — consistently, not transiently, so 2.2.1's
  "wait for a stable reading" approach could not catch it (the wrong value was already stable).
- Fix: `SnapClientSizeToDevicePixels` (`MainWindow.xaml.cs`) now forces a fresh
  `InvalidateMeasure`/`InvalidateArrange`/`UpdateLayout` pass once the window is genuinely Normal
  and visible, before trusting `ActualWidth`/`ActualHeight` — this makes WPF actually resync the
  HWND from real content instead of leaving the OS's stale restore rect in place. Verified with a
  real-process repro: without this fix the window settles at 468px wide (matching the exact width
  independently measured on the real installed app); with it, ~324px (matching the fixed 288px
  content + margins + chrome).

## 2.2.1 — 2026-09-22

### Fixed — settings could silently revert to defaults, losing custom app profiles

- `JsonSettingsRepository.Save` wrote `settings.json` directly with `File.WriteAllText`
  (truncate-then-write, not atomic). A crash, force-kill, or power loss mid-write left a
  truncated/invalid file; `LoadOrCreate` caught the parse failure and silently fell back to
  hard defaults — and since the app persists very frequently (every UI change, every 300 ms
  while the window is visible, on deactivate, after update checks), the very next save then
  overwrote the corrupted file with those defaults, permanently erasing any custom per-app
  profiles.
- Separately, `Save` had no locking: the UI's live-apply timer (UI thread) and a background
  update-check (thread-pool thread, via `CheckForUpdatesAsync`) could both call it on the same
  `JsonSettingsRepository` instance at the same time, racing on the same file.
- `Save` now writes to a temp file first and swaps it in with `File.Replace` (atomic on NTFS,
  and keeps the previous good file as `settings.json.bak` in the same operation), and both
  `Save`/`LoadOrCreate` take an instance lock so concurrent writers can no longer interleave.
  `LoadOrCreate` now falls back to `settings.json.bak` before ever returning defaults, and if a
  primary file is genuinely unreadable it is copied aside as `settings.json.corrupt-<timestamp>`
  instead of being silently discarded.

### Fixed — first launch could show much wider side margins than normal

- The main window's fixed-width content (288px) could end up centered in a wider-than-intended
  window on some launches, showing large empty gaps on both sides; closing and reopening the app
  always fixed it. Root cause: two independent code paths (`ContentRendered` and the `Loaded`
  handler) both raced to measure the window and freeze its size (`SizeToContent` → `Manual`) —
  on a cold first paint (JIT, style/resource resolution still settling) one of them could freeze
  the window at a transient, not-yet-final measurement.
- The window now requires the same width/height reading twice in a row (an `ApplicationIdle`
  turn apart) before freezing the size, and both callers go through one shared request path
  instead of racing each other.

## 2.2.0 — 2026-09-21

### Changed — smoothing engine rewritten as an independent pulse queue

- **Consistency:** every wheel notch now animates as its own independent pulse, with its own start
  time and distance, and overlapping pulses are summed. Each notch always delivers exactly its
  full distance over exactly `animationTime`, regardless of what else is animating.
- **Fixes the jump on resume:** scrolling again while the previous animation was still finishing
  used to produce a visibly bigger jump. The old engine kept a single shared ease-in ramp, so a
  new notch inherited whatever ramp state the previous motion had built up — measured as a ~4x
  first-tick spike purely depending on timing. There is no shared state to inherit any more.
- **Fixes weak continuous scrolling:** overlapping notches now add up instead of interfering, so
  sustained scrolling reaches the speed it should.
- **Dropped frames self-correct:** animation progress is a function of real elapsed time rather
  than tick count, so a late or skipped 4 ms timer callback no longer loses motion.
- The easing is the Michael Herf "pulse" curve ("Stopping", stereopsis.com), as used by Balazs
  Galambosi's MIT-licensed SmoothScroll — reimplemented from the public algorithm.

### New — Start smoothing (ms)

- A per-profile **Start smoothing (ms, 0 = auto)** field sets the ease-in duration of each pulse
  directly, instead of only indirectly through Tail / head ratio. `0` keeps the previous
  behaviour, so existing profiles are unchanged. Requires Animation easing to be on.

### Changed — acceleration no longer shrinks slow scrolling

- The acceleration multiplier used to fall as low as 0.10x, shrinking slower, evenly paced
  scrolling to a fraction of its distance. It now only ever multiplies up, never attenuates.

### Fixed — Scroll logs rendered every row as one string

- The shared `ListViewItem` template used a plain `ContentPresenter`, which silently ignores a
  `GridView`'s columns and falls back to `ToString()`. Rows now render as real columns with
  dividers aligned to their headers.
- Added a **PIXELS** column showing each raw pulse's pixel equivalent, and a live readout of
  scheduled vs. skipped animation ticks.

### New — Free-Spin inertia detection and calibration

- Detection and calibration for free-spin wheel inertia, with a guided calibration window and
  per-phase targets. Ships **off by default** (`read-only` detection mode, suppression disabled);
  existing settings files are unaffected until explicitly enabled.

### UI

- Selects, checkboxes and checkbox labels now show the hand cursor on hover.

> **Upgrade note:** `Animation time (ms)` now means the full duration of each pulse, which is a
> different meaning from previous versions. Existing values will feel noticeably faster — expect
> to retune. As a reference point, 300 ms with Tail / head ratio 2 approximates SmoothScroll's
> default feel.

---

## 2.1.6 — 2026-09-20

### New — persistent live dark mode

- **Dark mode:** a global **Dark mode** control now switches between the existing light palette and a sober neutral dark palette immediately, without restarting or recreating windows.
- **Coverage:** the shared semantic theme updates the main window plus profile, running-window, scroll-log, and Free-Spin surfaces already open; newly opened windows inherit the selected palette.
- **Persistence:** the setting is saved in `%APPDATA%\SmoothMice\settings.json` and starts in light mode when absent from older settings files.
- **UI polish:** main section wrappers are borderless, spacing before Updates is restored, and checkbox indicators retain their full geometry.

---

## 2.1.5 — 2026-09-20

### New — profile sources, controls, and English UI

- **Profile picker:** add an app profile from an executable path or a running window, with an initial window selected automatically when one is available.
- **Profile controls:** the **+** and **—** buttons now sit beside the profile selector.
- **UI:** reorganized the footer, translated visible application UI to English, and added the **Free-Spin Inertia Suppression (beta)** entry point.
- **Consolidation:** this release also includes the 2.1.3 and 2.1.4 fixes documented below.

---

## 2.1.4 — 2026-09-20

### Correção — bypass em Dying Light: The Beast + persistência imediata

- **Deteção Techland:** a classe raiz real `techland_game_class` passa a ser reconhecida como sinal forte de jogo, ainda exigindo janela em foco ou fullscreen/borderless e mantendo as exclusões explícitas.
- **Preferência:** alternar **Não ativar em jogos** grava imediatamente a opção global através do fluxo de persistência, sem depender da ordem dos eventos `Checked`/binding do WPF.
- **Regressão:** cobertura com os sinais reais capturados de Dying Light: The Beast, negativos fail-open e persistência `ViewModel → snapshot → JSON`.

---

## 2.1.3 — 2026-09-20

### Novo — bypass global conservador para jogos

- **Opção global:** adicionada **Não ativar em jogos**, persistida em JSON e desativada por padrão; quando ativa, deixa a roda física passar nativamente para jogos identificados de forma conservadora.
- **Classificador/cache:** classificação por classes fortes de engines, janela raiz e sinais de foco/fullscreen/borderless, com exclusões para browsers, players, apresentações, shell e launchers. O cache por raiz/PID tem TTL curto e falha aberta.
- Animações pendentes para um alvo de jogo são canceladas; apps normais e sinais incertos mantêm a suavização existente.

---

## 2.1.2 — 2026-09-19

### Monitor de scroll ao vivo + diagnóstico NDJSON opcional

- **Novo:** botão **Monitorar scroll** abre uma janela não modal com os pulsos físicos crus antes da suavização: horário relativo/UTC, eixo, delta/direção, intervalo e marcadores de rajada/reversão.
- **Janela:** mantém até 500 linhas, informa descartes sob sobrecarga e permite **Limpar** para iniciar uma medição controlada.
- **NDJSON opcional:** `--scroll-log` continua a gravar a captura persistente em paralelo; abrir o monitor não cria ficheiro nem writer.
- Eventos marcados como injetados (`LLMHF_INJECTED`) continuam excluídos. O diagnóstico só observa a entrada e não altera a suavização; os marcadores são heurísticas, não prova de defeito de hardware.

---

## 2.1.1 — 2026-05-05

### Correção — Enabled por app agora afeta subprocessos (steam, electron, etc.)

- **Causa raiz:** apps como o Steam exibem conteúdo em processos filhos (`steamwebhelper.exe`, helpers CEF/Electron). O matching por profile só verificava o exe direto da janela, ignorando o processo pai. Resultado: o profile de `steam.exe` com `Enabled = false` não tinha efeito nas janelas renderizadas pelos helpers.
- **Fix:** `ActiveAppResolver.QueryWindow` consulta agora o processo pai via `CreateToolhelp32Snapshot`. Se não houver profile para o exe direto, o `ProfileManager` tenta o exe pai. Isso permite que um profile de `steam.exe` (ou qualquer launcher) aplique as definições a todos os seus subprocessos.
- O resultado é cacheado por HWND — nenhum overhead adicional durante uma sessão de scroll.

---

## 2.1.0 — 2026-05-05

### Enabled por app + reestruturação do bloco Behaviour

- **"Enabled" por perfil:** a opção passou de switch global (`AppSettings`) para campo `ScrollProfileSettings.Enabled`, configurável individualmente em cada perfil (global e por app).
- **Bloco Behaviour:** "Enabled" é agora a primeira opção do bloco (sem título de secção). Para o perfil global actua como "suavizar apps não mapeadas"; para perfis por app controla apenas aquela app.
- **Removido:** checkbox "Enable for all apps by default" (substituído pelo novo `Enabled` no perfil global).
- **Tray:** o toggle Enable/Disable do tray continua funcional — inverte o `Enabled` do perfil global.
- **Hook:** passa a ficar sempre instalado; o `Enabled` por perfil controla se o evento é interceptado, sem overhead em inativo.

---

## 2.0.7 — 2026-05-05

### Correção — crash/comportamento errático no browser ao abrir o SmoothMice ou mudar parâmetros

- **Causa raiz — foco stale:** `_cachedUseSendInput` era decidido uma única vez em `OnMouseWheel` e nunca reavaliado. Se o utilizador abrisse a janela de definições (ou trocasse de janela) durante uma animação em curso, os ticks restantes continuavam a enviar `SendInput` para a nova janela em foco (SmoothMice ou outra), podendo injetar eventos no browser errado ou em estado inesperado.
- **Fix:** `TickCore` passa a chamar `GetAncestor` + `GetForegroundWindow` em cada tick. A estratégia `SendInput` vs `PostMessage` é agora dinâmica; apenas a elevação do processo (estável por sessão) permanece em cache em `_cachedIsElevated`.
- **Causa secundária — HWND reciclado:** se o browser navegava durante a animação, o `_cachedHwnd` podia ser destruído e o seu número reutilizado para outra janela noutro processo. `PostMessage` para esse handle reciclado entregava eventos a um alvo não intencionado.
- **Fix:** `ScrollInjector.TryPostWheel` valida o handle com `IsWindow(hwnd)` antes de cada `PostMessage`; descarta silenciosamente se o handle for inválido.

---

## 2.0.6 — 2026-05-05

### Correção — Explorer sem "stall then jump" (pass-through nativo)

- **Causa raiz (confirmada por runtime logs):** controlos `DirectUIHWND` e `SysListView32`/`SysTreeView32` do Explorer acumulam `WM_MOUSEWHEEL` internamente e só reagem visualmente quando o acumulado atinge ±120 (WHEEL_DELTA completo). Os nossos ticks de 1–11 units preenchiam esse acumulador lentamente → silêncio → salto de 3 linhas ao cruzar 120.
- **Fix:** `ActiveAppResolver` deteta a classe do HWND alvo via `GetClassName`. Se for um controlo legacy (`DirectUIHWND`, `SysListView32`, `SysTreeView32`, `ListBox`), o `ScrollCoordinator` faz **pass-through** — não intercepta o evento, scroll nativo intacto.
- Apps Win32 normais (browsers, apps de configuração, etc.) continuam a receber smooth scroll.

---

## 2.0.5 — 2026-05-05

### Correção — estratégia de injeção por foco (SendInput / PostMessage)

- **Dois problemas identificados:**
  1. **Task Manager / apps modernas em foco:** `PostMessage(WM_MOUSEWHEEL)` não é suficiente — apps modernas (WinUI 3, DirectUI, shell controls) respondem melhor ao input de hardware real gerado pelo `SendInput`.
  2. **Explorer "engasgando":** `DirectUIHWND` do Explorer não acumula sub-`WHEEL_DELTA` recebido via `PostMessage`.
- **Nova regra de eleição `_cachedUseSendInput`:**
  | Cenário | Método | Razão |
  |---|---|---|
  | Janela em **foco** | `SendInput` | Hardware input real → WM_MOUSEWHEEL + WM_POINTER corretos |
  | Janela em **background** | `PostMessage(hwnd)` | Bypassa "Scroll inactive windows" → entrega direta ao HWND |
  | Processo **elevado** | `SendInput` (override) | UIPI bloqueia PostMessage de processos não-elevados |
  Deteção de foco: `GetAncestor(hwndTarget, GA_ROOT) == GetForegroundWindow()`.
- **`NativeMethods`:** adicionadas P/Invoke `GetClassName` e `GetAncestor`.

---

## 2.0.4 — 2026-05-05

### Correção — scroll suave em janelas elevadas (Task Manager, regedit, …)

- **Causa raiz:** `PostMessage(hwnd, WM_MOUSEWHEEL)` para uma janela de processo **elevado** (High integrity) é silenciosamente descartado pelo **UIPI** (User Interface Privilege Isolation) do Windows quando o processo remetente não é elevado. O SmoothMice suprimia o scroll original (hook retorna 1) mas o evento suavizado nunca chegava — resultado: sem scroll nenhum no Task Manager.
- **`ActiveAppResolver`:** deteção de elevação adicionada ao query de processo: tenta `OpenProcess(PROCESS_QUERY_INFORMATION)` — se falhar (ERROR_ACCESS_DENIED / UIPI), o processo é elevado.
- **`ScrollCoordinator`:** estratégia de injeção adaptativa:
  - **Processo não-elevado** → `PostMessage(hwnd, WM_MOUSEWHEEL)`
  - **Processo elevado** → `SendInput(MOUSEEVENTF_WHEEL)` — bypassa UIPI por completo.

---

## 2.0.3 — 2026-05-05

### Correção — scroll suave em janelas em background (2ª tentativa)

- **Causa raiz identificada:** `SendInput(MOUSEEVENTF_WHEEL)` delega o routing ao OS. Se a configuração "Scroll inactive windows when I hover over them" estiver desligada, o OS entrega o evento à janela com foco em vez da janela sob o cursor.
- **`ScrollInjector`:** substituído `SendInput` por `PostMessage(hwnd, WM_MOUSEWHEEL, ...)` direto ao HWND alvo. `PostMessage` bypassa completamente o routing do OS.
- **`ScrollCoordinator`:** `_cachedHwnd` e `_cachedScreenPt` guardados em `OnMouseWheel` (hook time).
- **`PostMessage` não entra no loop:** `WH_MOUSE_LL` só interceta input de hardware.

---

## 2.0.2 — 2026-05-05

### Correção — scroll em janelas em background

- **`ActiveAppResolver`:** resolução do perfil passa a usar a janela sob o cursor (`WindowFromPoint`) em vez da janela em foco (`GetForegroundWindow`).
- **`ScrollCoordinator.OnMouseWheel`:** usa `e.ScreenPoint` para identificar a janela destino via `WindowFromPoint`.

---

## 2.0.1 — 2026-05-05

### Performance — zero overhead em idle

- **Timer on-demand:** o timer de 4 ms e a resolução 1 ms do scheduler são agora activados apenas quando chega um evento de scroll e desactivados imediatamente no fim da animação. Em idle: **0 chamadas Win32/segundo**.
  - Eliminado o impacto de `timeBeginPeriod(1)` permanente que afectava o scheduler de todos os processos (incluindo browsers) e era a causa directa da quebra de FPS reportada.
- **Cache de settings em `TickCore`:** elimina `GetForegroundWindow`, `ResolveForExecutable` e `ProfileManager.Snapshot` do loop de 4 ms.
- **Injecção fora do lock:** `SendInput` executada após libertar `_gate`, evitando bloquear o thread do hook.

---

## 2.0.0 — 2026-05-05

### Refatoração total — nova arquitetura de injeção

- **ScrollInjector:** migrado de `PostMessage(WM_MOUSEWHEEL)` para `SendInput(MOUSEEVENTF_WHEEL/HWHEEL)`.
  - `SendInput` usa o routing nativo do OS (DWM/compositor): a janela sob o cursor recebe o evento corretamente, incluindo overlays de sistema como o painel **Snap Layout** do Windows 11.
  - Modificadores de teclado (Ctrl, Shift) são lidos do estado real do teclado pela app destinatária.
- **ScrollCoordinator:** adicionado *pass-through* de Ctrl — quando a tecla Ctrl está premida, o evento passa sem ser interceptado. Corrige **Ctrl+scroll** (zoom no Explorer, zoom em browsers, ajuste de volume, etc.).
- **MouseHookService:** eventos com flag `LLMHF_INJECTED` são ignorados pelo hook — previne re-processamento (loop de suavização dupla).

---

## 1.0.1 — 2026-05-05

### Melhoria — easing de scroll mais fluído

- **SmoothScrollEngine:** substituído o modelo step-queue (cubic piecewise) pelo modelo **velocity-lerp com rampa de velocidade**.
  - `_remaining` acumula todos os eventos na mesma direção — sem steps sobrepostos com fases de ease-in independentes que criavam "barrancos" de velocidade.
  - Ease-in via rampa `_speed` (exponencial em direção a 1.0); ease-out via decaimento exponencial natural.
  - C¹ e C² contínuo: sem jerk na inflexão.

---

## 1.0.0 — 2026-04-18

### Marco estável — primeira versão de produção

- **Hook:** delegate pre-JIT antes de `SetWindowsHookEx` (`RuntimeHelpers.PrepareDelegate`) — elimina o stutter no primeiro evento do rato causado por JIT dentro do callback nativo.
- **Arranque:** instalação do hook diferida para `DispatcherPriority.Normal`.
- **UI:** timer de aplicação automática (300 ms, `Background` priority).
- **Publish:** `PublishReadyToRun=true`.

---

## 0.3.13 — 2026-04-18

### Correção crítica — crash na abertura (todas as versões ≥ 0.3.9)

- **XAML:** `ProgressBar.Value` ligado a `UpdateBannerProgress` sem `Mode=OneWay` — WPF usa `BindsTwoWayByDefault` para `RangeBase.Value`, tenta escrever de volta na propriedade read-only e lança `InvalidOperationException` não tratada no arranque.
  - Corrigido: `Value="{Binding UpdateBannerProgress, Mode=OneWay}"`.

## 0.3.12 — 2026-04-18

### Correções críticas de arranque

- **Cursor travado / app não abre:** `ActiveAppResolver` substituiu `Process.MainModule.FileName` por `QueryFullProcessImageName` (nativo, < 1 ms).
- **Cache de HWND:** resultado em cache quando a janela em primeiro plano não muda.
- **Reentrada no timer:** guarda de reentrada com `Interlocked` em `Tick()`.

## 0.3.11 — 2026-04-18

- **Arranque:** `SetWindowsHookEx(WH_MOUSE_LL)` passa `hMod = NULL` — evita falha do hook com apphost.

## 0.3.9 — 2026-04-18

- **OTA:** arranque com `/postota`, barra de progresso, reinício automático.
- **UI:** `MinHeight` maior e snap de tamanho.
- **Instalador:** `SetupIconFile` com `SmoothMice.ico`.

## 0.3.8 — 2026-04-18

- **Distribuição:** GitHub Release com instalador 0.3.8.

## 0.3.7 — 2026-04-18

- **UI:** janela de definições corrigida após arranque com `/tray`.
- **OTA:** batch de instalação com espera + taskkill.

## 0.3.6 — 2026-04-18

- **Docs:** README — pré-visualização atualizada.

## 0.3.5 — 2026-04-18

- **UI:** Enter nos campos numéricos limpa foco após gravar.
- **Atualizações in-app:** Inno com `CloseApplications=yes`; batch OTA melhorado.

## 0.3.4 — 2026-04-18

- **UI:** refinamento visual e estrutura do `MainWindow`.
- **Core / infra:** remoção de APIs não usadas; comentários compactos.

## 0.3.3 — 2026-04-18

- **Atualizações:** verificação contra releases no GitHub; descarga e instalação silenciosa.
- **UI:** secção «ATUALIZAÇÕES»; ícone multi-resolução.

## 0.3.2 — 2026-04-18

- **UI:** versão da app discreta no rodapé; `SizeToContent` e altura automática.

## 0.3.1 — 2026-04-18

- **Publish:** executável `SmoothMice-{Version}.exe`; `build-installer.ps1` passa `MyPublishedExe`.

## 0.3.0 — 2026-04-18

- **Instalador / versão:** `Directory.Build.props` como fonte de `<Version>`; `build-installer.ps1`.
- **Arranque:** registo em `Run` com `/tray`.

## 0.1.0 — 2026-04-18

- Utilitário Windows (x64): scroll da roda mais suave, perfis por aplicação, ícone na bandeja e definições em JSON em `%AppData%\SmoothMice\settings.json`.
