# SmoothMice — release notes

Antes de alterar `<Version>` em `Directory.Build.props`, lê este ficheiro. Cada versão nova deve ter **secção própria** (mais recente em cima). Resume alterações reais (diff pendente ou commit) em bullets.

---

## 1.0.7 — 2026-05-05

### Correção — scroll suave no Task Manager, Explorer e todas as janelas em foco

- **Dois problemas identificados:**
  1. **Task Manager / apps modernas em foco:** `PostMessage(WM_MOUSEWHEEL)` não é suficiente — apps modernas (WinUI 3, DirectUI, shell controls) respondem melhor ao input de hardware real gerado pelo `SendInput`.
  2. **Explorer "engasgando":** `DirectUIHWND` do Explorer não acumula sub-`WHEEL_DELTA` recebido via `PostMessage`; o utilizador tinha de rolar mais do que o esperado para a tela reagir.
- **Nova regra de eleição `_cachedUseSendInput`:**
  | Cenário | Método | Razão |
  |---|---|---|
  | Janela em **foco** | `SendInput` | Hardware input real → WM_MOUSEWHEEL + WM_POINTER corretos |
  | Janela em **background** | `PostMessage(hwnd)` | Bypassa "Scroll inactive windows" → entrega direta ao HWND |
  | Processo **elevado** | `SendInput` (override) | UIPI bloqueia PostMessage de processos não-elevados |
  Deteção de foco: `GetAncestor(hwndTarget, GA_ROOT) == GetForegroundWindow()`.
- **`NativeMethods`:** adicionadas P/Invoke `GetClassName` e `GetAncestor`.
- **`ActiveAppResolver`:** revertido para `(exeName, isElevated)` — deteção de classe WinUI 3 removida.

---

## 1.0.6 — 2026-05-05

### Correção — scroll suave em janelas elevadas (Task Manager, regedit, …)

- **Causa raiz:** `PostMessage(hwnd, WM_MOUSEWHEEL)` para uma janela de processo **elevado** (High integrity) é silenciosamente descartado pelo **UIPI** (User Interface Privilege Isolation) do Windows quando o processo remetente não é elevado. O SmoothMice suprimia o scroll original (hook retorna 1) mas o evento suavizado nunca chegava — resultado: sem scroll nenhum no Task Manager.
- **`ActiveAppResolver`:** deteção de elevação adicionada ao query de processo: tenta `OpenProcess(PROCESS_QUERY_INFORMATION)` — se falhar (ERROR_ACCESS_DENIED / UIPI), o processo é elevado. Resultado cacheado por HWND junto com o exe name, sem overhead adicional.
- **`ScrollCoordinator`:** estratégia de injeção adaptativa por sessão de scroll:
  - **Processo não-elevado** → `PostMessage(hwnd, WM_MOUSEWHEEL)` — entrega direta à fila de mensagens, ignora configuração "Scroll inactive windows".
  - **Processo elevado** (Task Manager, regedit, …) → `SendInput(MOUSEEVENTF_WHEEL)` — injeta input ao nível de hardware, bypassando o UIPI por completo.

---

## 1.0.5 — 2026-05-05

### Correção — scroll suave em janelas em background (2ª tentativa)

- **Causa raiz identificada:** `SendInput(MOUSEEVENTF_WHEEL)` delega o routing ao OS. Se a configuração "Scroll inactive windows when I hover over them" estiver desligada no Windows (ou em contextos específicos), o OS entrega o evento à janela **com foco** em vez da janela **sob o cursor** — tornando o smoothing inútil para janelas em background.
- **`ScrollInjector`:** substituído `SendInput` por `PostMessage(hwnd, WM_MOUSEWHEEL, ...)` direto ao HWND alvo. `PostMessage` bypassa completamente o routing do OS e entrega o evento diretamente na fila de mensagens da janela certa, independentemente de foco ou configurações do sistema.
- **`ScrollCoordinator`:** `_cachedHwnd` e `_cachedScreenPt` guardados em `OnMouseWheel` (hook time). `TickCore` usa esses valores no `PostMessage`, garantindo que o HWND é sempre o que o utilizador estava a hoverar no momento do scroll físico.
- **`PostMessage` não entra no loop:** `WH_MOUSE_LL` só interceta input de hardware; `PostMessage` vai direto para a fila de mensagens, sem passar pelo hook.

---

## 1.0.4 — 2026-05-05

### Correção — scroll em janelas em background

- **`ActiveAppResolver`:** resolução do perfil passa a usar a janela sob o cursor (`WindowFromPoint`) em vez da janela em foco (`GetForegroundWindow`). O scroll é entregue pelo Windows à janela sob o cursor independentemente do foco de teclado — usar a janela errada fazia o perfil ser resolvido para a app em foco, em vez da app que ia receber o scroll.
- **`ScrollCoordinator.OnMouseWheel`:** usa `e.ScreenPoint` (coordenada exata do cursor no momento do evento, fornecida pelo hook) para identificar a janela destino via `WindowFromPoint`.

---

## 1.0.3 — 2026-05-05

### Performance — zero overhead em idle

- **Timer on-demand:** o timer de 4 ms e a resolução 1 ms do scheduler do Windows são agora activados apenas quando chega um evento de scroll e desactivados imediatamente quando ambos os motores ficam quiet (fim da animação, ~100–200 ms). Em idle: **0 chamadas Win32/segundo**, resolução do scheduler restaurada ao default do sistema.
  - Eliminado o impacto de `timeBeginPeriod(1)` permanente, que afectava o scheduler de todos os processos (incluindo browsers) e era a causa directa da quebra de FPS reportada.
- **Cache de settings em `TickCore`:** `ScrollProfileSettings` guardado em `_cachedSettings` no `OnMouseWheel`; `TickCore` usa o cache — **elimina** `GetForegroundWindow`, `ResolveForExecutable` (lock + LINQ) e `ProfileManager.Snapshot` (lock + clone) do loop de 4 ms.
- **Injecção fora do lock:** `SendInput` (chamada kernel) executada após libertar `_gate`, evitando bloquear o thread do hook durante a injecção.

---

## 1.0.2 — 2026-05-05

### Correções — Ctrl+scroll e Snap Layout

- **ScrollInjector:** migrado de `PostMessage(WM_MOUSEWHEEL)` para `SendInput(MOUSEEVENTF_WHEEL/HWHEEL)`.
  - `SendInput` usa o routing nativo do OS (DWM/compositor): a janela sob o cursor recebe o evento corretamente, incluindo overlays de sistema como o painel **Snap Layout** do Windows 11.
  - Modificadores de teclado (Ctrl, Shift) são lidos do estado real do teclado pela app destinatária — elimina a necessidade de embutir flags no wParam.
- **ScrollCoordinator:** adicionado *pass-through* de Ctrl — quando a tecla Ctrl está premida, o evento passa sem ser interceptado. Corrige **Ctrl+scroll** (zoom no Explorer, zoom em browsers, ajuste de volume, etc.).
- **MouseHookService:** eventos com flag `LLMHF_INJECTED` são ignorados pelo hook — previne que os eventos injetados pelo `SendInput` sejam re-processados (loop de suavização dupla).

---

## 1.0.1 — 2026-05-05

### Melhoria — easing de scroll mais fluído

- **SmoothScrollEngine:** substituído o modelo step-queue (cubic piecewise) pelo modelo **velocity-lerp com rampa de velocidade**.
  - `_remaining` acumula todos os eventos na mesma direção — sem steps sobrepostos com fases de ease-in independentes que criavam "barrancos" de velocidade.
  - Ease-in via rampa `_speed` (exponencial em direção a 1.0); ease-out via decaimento exponencial natural (`remaining × lerp × speed`).
  - C¹ e C² contínuo: sem jerk na inflexão (descontinuidade na 2ª derivada do modelo anterior).
  - `LerpFactor` derivado de `AnimationTimeMs` → 98 % do movimento consumido no tempo configurado.
  - `SpeedRampFactor` derivado de `TailToHeadRatio` → proporção ease-in/ease-out respeitada.

---

## 1.0.0 — 2026-04-18

### Marco estável — primeira versão de produção

- **Hook:** delegate pre-JIT antes de `SetWindowsHookEx` (`RuntimeHelpers.PrepareDelegate`) — elimina o stutter no primeiro evento do rato causado por JIT dentro do callback nativo.
- **Arranque:** instalação do hook diferida para `DispatcherPriority.Normal` — o message pump já está ativo quando `SetWindowsHookEx` é chamado; pausa da input-chain ocorre enquanto a janela ainda está invisível.
- **UI:** timer de aplicação automática (300 ms, `Background` priority) — enquanto a janela estiver aberta, definições são persistidas continuamente sem precisar de fechar a janela.
- **Publish:** `PublishReadyToRun=true` em ambos os modos (self-contained e framework-dependent) — reduz tempo de arranque a frio.

---

## 0.3.13 — 2026-04-18

### Correção crítica — crash na abertura (todas as versões ≥ 0.3.9)

- **XAML:** `ProgressBar.Value` ligado a `UpdateBannerProgress` (propriedade com `private set`) sem `Mode=OneWay` — WPF usa `BindsTwoWayByDefault` para `RangeBase.Value`, tenta escrever de volta na propriedade read-only e lança `InvalidOperationException` não tratada no arranque, matando o processo silenciosamente.
  - Corrigido: `Value="{Binding UpdateBannerProgress, Mode=OneWay}"`.

## 0.3.12 — 2026-04-18

### Correções críticas de arranque

- **Cursor travado / app não abre:** `ActiveAppResolver` substituiu `Process.MainModule.FileName` (enumeração de módulos, centenas de ms) por `QueryFullProcessImageName` (nativo, < 1 ms); bloquear a UI thread no callback do hook do rato causava o freeze do cursor e impedia a janela de aparecer.
- **Cache de HWND:** quando a janela em primeiro plano não muda, o `ActiveAppResolver` devolve o resultado em cache sem chamar qualquer API Win32 — eliminando a carga no hook e no timer.
- **Reentrada no timer:** `Tick()` (4 ms) agora tem guarda de reentrada com `Interlocked`; evita pileup de chamadas no thread pool quando a resolução da app demorava mais do que o intervalo.

## 0.3.11 — 2026-04-18

### Alterações

- **Arranque (instalação / single-file):** `SetWindowsHookEx(WH_MOUSE_LL)` passa `hMod = NULL` conforme Windows — evita falha do hook com apphost e crash silencioso antes da janela abrir.

## 0.3.9 — 2026-04-18

### Alterações

- **OTA:** após Inno, arranque com `/postota` (janela normal), layout completo, depois reinício automático com `/tray` — evita truncagem / tamanho errado da UI.
- **OTA:** barra de progresso na secção «Updates» durante a descarga (e estado «Installing update…» antes de fechar).
- **UI:** `MinHeight` maior e snap de tamanho respeita `MinWidth`/`MinHeight` para não cortar o rodapé.
- **Instalador (Inno):** `SetupIconFile` com `SmoothMice.ico` — ícone do `SmoothMice_Setup_*.exe` e do assistente.

## 0.3.8 — 2026-04-18

### Alterações

- **Distribuição:** GitHub Release com instalador **0.3.8**; inclui o que estava em **0.3.7** (correção OTA + janela de definições após `/tray`).

## 0.3.7 — 2026-04-18

### Alterações

- **UI:** janela de definições deixa de ficar “só barra de título” após arranque com `/tray` (OTA ou auto-start): não fixar `SizeToContent`/`Width`/`Height` com janela minimizada ou oculta; `MinWidth`/`MinHeight`; ao abrir pela tray, recalcular tamanho.
- **OTA:** batch de instalação — espera 5 s + `taskkill` best-effort + 2 s antes do Inno, para libertar `SmoothMice.exe` antes de substituir o ficheiro.

## 0.3.6 — 2026-04-18

### Alterações

- **Docs:** README — pré-visualização atualizada (`docs/app-preview.png`); imagem centrada com HTML (`<p align="center">`).

## 0.3.5 — 2026-04-18

### Alterações

- **UI:** Enter nos campos numéricos passa a limpar o foco após gravar o valor, para feedback visual claro.
- **Atualizações in-app:** Inno com `CloseApplications=yes`; batch OTA com espera curta e `/CLOSEAPPLICATIONS`; ficheiro de erro em `%TEMP%` + aviso no próximo arranque se o setup falhar; mensagem se a descarga/preparação falhar mesmo quando a verificação não foi manual; remoção do “falhar em silêncio” nesses caminhos.
- **Docs:** README — pré-visualização com `docs/app-preview.png` (substitui `myProfile.png`).

## 0.3.4 — 2026-04-18

### Alterações

- **UI:** refinamento visual e estrutura do `MainWindow` / recursos em `App.xaml` (layout, tipografia, secções de definições).
- **ViewModel / arranque:** ajustes no `MainViewModel` e no fluxo em `App.xaml.cs` alinhados à nova UI.
- **Core / infra:** remoção de APIs não usadas em `ProfileManager`; comentários e documentação interna mais compactos em `SmoothScrollEngine`, `ScrollCoordinator`, `ScrollMath`; remoção da constante pública `ScrollMath.WheelDelta`.
- **Atualizações:** mensagens de erro da verificação GitHub (API / URL) passam a inglês.
- **Docs / licença:** README com orientação explícita a forks e builds próprios; ficheiro `LICENSE` (MIT).

## 0.3.3 — 2026-04-18

### Alterações

- **Atualizações:** verificação contra releases no GitHub (agendada conforme frequência + botão «Verificar atualizações»); descarga do instalador `SmoothMice_Setup_*.exe` e instalação silenciosa com reabertura em `/tray` quando aplicável; `UpdateCheckFrequency` e `LastUpdateCheckUtc` persistidos em `settings.json`.
- **UI:** secção «ATUALIZAÇÕES» na janela principal; texto da versão (semver) discreto por baixo do título «SmoothMice»; `ContentRendered` e fundo/layout no `Grid` raiz para evitar faixa vazia com `SizeToContent`.
- **Ícone:** geração de `.ico` multi-resolução (PNG) para o executável; cópia do ícone da bandeija com libertação correta do handle (`DestroyIcon`).

## 0.3.2 — 2026-04-18

### Alterações

- **UI:** versão da app (semver) discreta, centrada no rodapé da janela.
- **UI:** janela com `SizeToContent` e área de definições em altura automática (sem `ScrollViewer` desnecessário), para evitar conteúdo cortado e espaço vazio.

## 0.3.1 — 2026-04-18

### Alterações

- **Publish:** o executável em `publish\` passa a chamar-se `SmoothMice-{Version}.exe` (`AssemblyName` com `$(Version)`).
- **Inno:** copia esse ficheiro para `{app}\SmoothMice.exe` (atalhos, `Run` e arranque inalterados).
- **build-installer.ps1:** passa `MyPublishedExe` ao ISCC além de `MyAppVersion`.

## 0.3.0 — 2026-04-18

### Alterações

- **Instalador / versão:** `Directory.Build.props` como fonte de `<Version>`; `build-installer.ps1` passa `MyAppVersion` ao Inno (`/D`); `.iss` sem versão embutida; default do script = **self-contained** completo; `-FrameworkDependent` para setup leve.
- **Docs / automação:** `release-notes.md`; README (instalador, links); regras Cursor (bump em compilação, instalador completo, release em `main` com `.exe` completo + instruções do instalador menor).
- **Arranque:** registo em `Run` com `...\SmoothMice.exe" /tray`; ao arrancar com `/tray`, a janela principal inicia minimizada/oculta (só bandeja).
- **Definições / motor:** removida opção de perfil `ReverseWheelDirection` (UI, ViewModel, defaults, `SmoothScrollEngine`); testes alinhados.
- **UI:** `MainWindow` — coluna esquerda «Animation + Behaviour»; controlos de aceleração concentrados à direita.

## 0.1.0 — 2026-04-18

### Resumo

- Utilitário Windows (x64): scroll da roda mais suave, perfis por aplicação, ícone na bandeja e definições em JSON em `%AppData%\SmoothMice\settings.json`.
- Versão de produto centralizada em `Directory.Build.props`; instalador Inno recebe a mesma versão via `installer/build-installer.ps1`.
