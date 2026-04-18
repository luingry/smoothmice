# SmoothMice — release notes

Antes de alterar `<Version>` em `Directory.Build.props`, lê este ficheiro. Cada versão nova deve ter **secção própria** (mais recente em cima). Resume alterações reais (diff pendente ou commit) em bullets.

---

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
