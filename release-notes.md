# SmoothMice — release notes

Antes de alterar `<Version>` em `Directory.Build.props`, lê este ficheiro. Cada versão nova deve ter **secção própria** (mais recente em cima). Resume alterações reais (diff pendente ou commit) em bullets.

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
