# SmoothMice — release notes

Antes de alterar `<Version>` em `Directory.Build.props`, lê este ficheiro. Cada versão nova deve ter **secção própria** (mais recente em cima). Resume alterações reais (diff pendente ou commit) em bullets.

---

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
