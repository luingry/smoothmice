# SmoothMice

Windows-only desktop utility that smooths mouse wheel scrolling with per-application profiles, system tray controls, and JSON settings under `%AppData%\SmoothMice\settings.json`.

**Open source:** the full source is on GitHub. Anyone can **fork** the repo, **edit** the code, and ship **their own build** or forked variant (respect the license file in the repository). Pull requests and issues are welcome if you want changes upstream.

## App preview

<p align="center">
  <img src="docs/app-preview.png" alt="SmoothMice configuration window (animation and acceleration settings)." width="auto" />
</p>

## Requirements

- Windows 10/11 x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (for `dotnet build` / `dotnet publish`)

### Optional: install SDK + Inno via winget

```powershell
winget install --id Microsoft.DotNet.SDK.8 -e --accept-package-agreements --accept-source-agreements
winget install --id JRSoftware.InnoSetup -e --accept-package-agreements --accept-source-agreements
```

Inno may install under `%LocalAppData%\Programs\Inno Setup 6\`; [installer/build-installer.ps1](installer/build-installer.ps1) checks that path first.

## Build

```bash
dotnet build SmoothMice.sln -c Release
```

Run the WPF app:

```bash
dotnet run --project src/SmoothMice.App/SmoothMice.App.csproj -c Release
```

Self-contained publish (for installer payload). In Git Bash, prefer explicit MSBuild properties (`--self-contained true` alone can miss bundling the runtime):

```bash
dotnet publish src/SmoothMice.App/SmoothMice.App.csproj -c Release -p:PublishDebugSymbols=false
```

Published binaries: `src/SmoothMice.App/bin/Release/net48/publish/SmoothMice-{Version}.exe` (o `{Version}` vem de [Directory.Build.props](Directory.Build.props); o instalador Inno copia-o como `SmoothMice.exe` para `{app}`).

## Tests

```bash
dotnet test SmoothMice.sln -c Release
```

## Diagnóstico de pulsos da roda

Para inspecionar os pulsos físicos crus recebidos pelo hook, clique em **Monitorar scroll** no rodapé do SmoothMice. A janela não modal mostra em tempo real cada pulso antes da suavização: tempo relativo e UTC, eixo, delta/direção, intervalo desde o pulso anterior do mesmo eixo, contagem na janela de 120 ms e marcadores de rajada/reversão.

Use **Limpar** para zerar linhas, totais e análise antes de iniciar um teste controlado. A visualização mantém no máximo os 500 pulsos mais recentes; sob sobrecarga, pulsos antigos ou que excederem a fila local são descartados e o status informa o total. Esse modo não cria arquivo nem inicia o writer de diagnóstico.

O marcador **Rajada** aparece em cada pulso que completa ou permanece em uma janela de 4 ou mais pulsos do mesmo eixo em 120 ms — portanto o total exibido é de *pulsos marcados*, não de episódios de rajada distintos. **Reversão** marca uma mudança de direção no mesmo eixo em até 150 ms. Ambos são heurísticas para localizar trechos que merecem comparação com o movimento físico da roda; não provam, sozinhos, defeito de hardware. Teste também em outro aplicativo/porta ou computador antes de concluir a causa.

Para persistir a mesma captura em NDJSON, inicie adicionalmente com:

```powershell
dotnet run --project src/SmoothMice.App/SmoothMice.App.csproj -c Release -- --scroll-log
```

Cada execução com a opção cria um arquivo NDJSON por sessão em `%LOCALAPPDATA%\SmoothMice\Diagnostics` (por exemplo, `scroll-pulses-...ndjson`). Sem `--scroll-log`, não há arquivo nem thread de persistência. O arquivo pode coexistir com a janela ao vivo; ambos recebem o mesmo pulso físico. Pulsos marcados pelo Windows como injetados (`LLMHF_INJECTED`), incluindo os gerados pelo SmoothMice, continuam excluídos antes do registro.

Há uma linha inicial com os limiares e uma linha por pulso: `utc` e `monotonic_ticks`, `axis`, `delta`/`direction`, `interval_ms` desde o pulso anterior do mesmo eixo, `burst_120ms_count`, `short_burst`, `rapid_reversal`, `shift` e a posição de tela `x`/`y`. A linha final informa `records_written` e `dropped_pulses`. A fila é limitada a 4.096 pulsos e o arquivo a 100.000 linhas de pulso; se houver sobrecarga, o descarte aparece no resumo final para não atrasar o hook.

`short_burst` e `rapid_reversal` usam os mesmos critérios apresentados na janela ao vivo.

## Manual smoke tests (recommended)

1. Launch the app, confirm defaults match your baseline profile.
2. Toggle **Enabled** off: wheel should behave like Windows default (no interception).
3. Toggle **Enabled** on: scrolling in Explorer/Chrome/VS Code should feel smoothed.
4. **Horizontal wheel** (trackpad / tilt wheel): when **Horizontal scrolling** is on, horizontal deltas should smooth.
5. Create an app-specific profile and verify it overrides the global profile for that executable.
6. **Auto start on login**: verify `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\SmoothMice` points to the installed `SmoothMice.exe`.
7. Tray menu: Open / Enable-Disable / Exit.

## Installer (Inno Setup 6)

1. Install [Inno Setup 6](https://jrsoftware.org/isdl.php) (includes `ISCC.exe`).
2. From the repo root:

```powershell
.\installer\build-installer.ps1
```

Or double-click `installer\build-installer.cmd` (opens a window; pauses at the end).

**Instalador por defeito** (`.\installer\build-installer.ps1`): publica para **.NET Framework 4.8** sem runtime bundling e instala o `SmoothMice-{Version}.exe` com as DLLs necessárias. O .NET Framework 4.8 já vem no Windows 10/11; após instalar, o ficheiro em disco continua `SmoothMice.exe`.

```powershell
.\installer\build-installer.ps1
```

Output: `artifacts\installer\SmoothMice_Setup_{version}.exe` — o `version` é o MSBuild `Version` em [Directory.Build.props](Directory.Build.props) (o script [installer/build-installer.ps1](installer/build-installer.ps1) passa-o ao Inno). Histórico por versão: [release-notes.md](release-notes.md).

Manual steps: `dotnet publish` as in [installer/build-installer.ps1](installer/build-installer.ps1), then `ISCC.exe /DMyAppVersion=x.y.z /DMyPublishedExe=SmoothMice-x.y.z.exe installer\SmoothMice.Installer.iss` (valores alinhados a `Directory.Build.props`), or use the script.

## Repository

- **Upstream:** https://github.com/luingry/smoothmice  
- **Fork & customize:** use GitHub **Fork**, clone your fork, change whatever you need, then `dotnet build` / `dotnet publish` as below. Your fork is yours to rename, rebrand, or extend — no permission needed beyond the repo license.

## Notes

- Low-level mouse hooks require the app to keep running; keep CPU usage low by design.
- Some applications handle wheel messages uniquely; report odd cases as issues.
