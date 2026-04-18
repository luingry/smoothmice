# SmoothMice — release notes

Antes de alterar `<Version>` em `Directory.Build.props`, lê este ficheiro. Cada versão nova deve ter **secção própria** (mais recente em cima). Resume alterações reais (diff pendente ou commit) em bullets.

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
