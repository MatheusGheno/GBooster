# GBooster

**🇧🇷 Português** · [🇺🇸 English](#english)

> Projeto pessoal, gratuito e de código aberto (licença MIT). Versão **0.1**: primeira versão pública, ainda em testes.

## O que é

O GBooster é um programa para **Windows 10/11 (64 bits)** que fica na bandeja do sistema e cuida da tela
para você quando joga. Quando um jogo cadastrado abre, ele troca a **resolução** do monitor em que o jogo
está (por exemplo, uma resolução "esticada" como 1280×960), e opcionalmente a **taxa de atualização (Hz)** e a
**saturação** (placas NVIDIA). Quando o jogo fecha, **devolve tudo como estava**.

Já vem com **Counter-Strike 2**, **Delta Force** e **Battlefield 6**, e dá para ajustar cada jogo separadamente.

## Para quem é

Para quem joga competitivo com resolução diferente da nativa e cansou de trocar nas configurações do Windows
toda vez que abre e fecha o jogo.

## O que ele faz

- Detecta o jogo pelo nome do programa (`cs2.exe`, `bf6.exe`...) e aplica a configuração desse jogo.
- Resolução, Hz e saturação **por jogo**; a saturação só muda durante o jogo.
- Volta ao normal ao fechar o jogo, ao clicar em **Restaurar agora**, ao usar o atalho **Ctrl+Alt+R** (pode mudar) ou ao fechar o app.
- Se o app ou o PC fecharem sem aviso, **na próxima abertura ele desfaz** o que ficou aplicado.
- Botão **Testar resolução** (volta sozinho em alguns segundos), histórico de eventos, avisos, início com o Windows, backup das configurações (exportar/importar) e app em **português e inglês** (Geral → Sistema → Idioma).

## O que ele garante (e o que não garante)

**Garante:**
- **Não toca no jogo.** Não abre o processo do jogo, não lê nem escreve na memória, não injeta DLL, não usa hooks, drivers, overlay nem captura de tela.
- **Só usa recursos públicos do Windows e da NVIDIA** (a mesma API do programa QRes para a resolução; a API pública NvAPI para a saturação).
- **Sem internet, sem conta, sem telemetria.** Nada sai do seu computador. Os dados ficam em `%AppData%\GBooster`.
- **Código aberto**: qualquer pessoa pode conferir tudo o que ele faz.
- **Tudo reversível**: ele só desfaz o que ele mesmo aplicou.

**Não garante:**
- **Não existe garantia de "100% sem banimento".** Nenhuma desenvolvedora de jogo publica uma lista de programas permitidos. O GBooster não faz nada do que as regras públicas proíbem, mas a decisão final é sempre do anti-cheat. A **saturação** é a parte mais sensível (pode ser vista como "vantagem visual"); leia o [relatório](RELATORIO-ANTICHEAT.md) e use por sua conta e risco. Com saturação em 50% (padrão), o app nem mexe no driver.
- Sem garantias de qualquer tipo (ver licença MIT).

## Como instalar

1. Em **Releases**, baixe `GBooster-win-x64.zip`.
2. Extraia numa pasta (por exemplo, `C:\GBooster`).
3. Dê dois cliques em `GBooster.exe`. **Não precisa instalar o .NET.**

> **Aviso do Windows (SmartScreen):** como o programa não tem assinatura digital paga, o Windows pode mostrar
> "O Windows protegeu o computador". Clique em **Mais informações → Executar assim mesmo**.
> Você pode conferir o arquivo com o `SHA256.txt` da versão.

## Primeiro teste (nesta ordem)

1. Abra o app, aba **Jogos**, escolha o jogo e a resolução (a mesma do jogo).
2. Deixe a saturação desligada e clique em **Testar resolução**. A tela troca e volta sozinha.
3. Confira que **Restaurar agora** (ou Ctrl+Alt+R) devolve a tela.
4. Só depois teste com o jogo aberto e, por último, a saturação.

Se algo travar a tela: **Ctrl+Alt+R**.

## Configuração da placa de vídeo (o app não faz por você)

Para a resolução esticada ocupar a tela toda, no Painel de Controle NVIDIA: *Ajustar o tamanho e a posição da
área de trabalho* → Escala: **Tela inteira**, executar em **GPU**, e marque *Substituir o modo de escala definido
por jogos e programas*. A tela de ajuda dentro do app tem o passo a passo.

## Limitações conhecidas

- A saturação funciona **somente com placas NVIDIA**. Resolução e Hz funcionam em qualquer placa.
- Os nomes dos programas dos jogos podem mudar com atualizações; dá para corrigir no app (lupa).
- Com vários monitores, o app muda o monitor em que o jogo abrir.

## Tecnologia e transparência

| Ação | Como | Toca o jogo? |
|---|---|---|
| Saber se o jogo abriu | Lista de processos do Windows, a cada 2 s | Não |
| Achar o monitor do jogo | Posição da janela | Não |
| Trocar resolução/Hz | `ChangeDisplaySettingsEx` (temporário, sem gravar no registro) | Não |
| Saturação | NvAPI ("Vibrância digital") | Não |
| Atalho global | `RegisterHotKey` (sem hook de teclado) | Não |

Arquivos do app em `%AppData%\GBooster\`: `settings.json`, `applied.json` (some ao restaurar), `log.json`.
"Iniciar com o Windows" cria uma entrada em `HKCU\...\Run`; desmarcar remove.

## Compilar você mesmo

Instale o **.NET 8 SDK** e dê dois cliques em `COMPILAR.bat` (ou rode
`dotnet publish -c Release -r win-x64 --self-contained true -o publicar`). O programa fica em `publicar\GBooster.exe`.

## Segurança, licença e agradecimentos

- Achou um problema de segurança? Veja [SECURITY.md](SECURITY.md).
- Código sob licença **MIT** (arquivo `LICENSE`), © 2026 [Matheus Gheno](https://github.com/matheusgheno).
- Usa **NvAPIWrapper.Net** (LGPL-3.0), sem modificações. Veja [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
- Não tem relação com NVIDIA, Valve, Tencent/Level Infinite/Garena ou EA. Marcas pertencem aos seus donos.

---

<a id="english"></a>

# GBooster (English)

> A free, open-source personal project (MIT license). Version **0.1**: first public release, still being tested.

## What it is

GBooster is a **Windows 10/11 (64-bit)** tray app that handles your display while you play. When a configured
game starts, it switches the **resolution** of the monitor the game is on (for example a stretched
resolution like 1280×960), and optionally the **refresh rate (Hz)** and the **color saturation** (NVIDIA GPUs).
When the game closes, it **puts everything back exactly as it was**.

It comes preconfigured for **Counter-Strike 2**, **Delta Force** and **Battlefield 6**, and each game can be tuned separately.

## Who it's for

Competitive players who use a non-native resolution and are tired of changing it in Windows settings every time
they start and quit a game.

## What it does

- Detects the game by its executable name (`cs2.exe`, `bf6.exe`...) and applies that game's settings.
- Resolution, Hz and saturation **per game**; saturation only changes while the game is running.
- Reverts when the game closes, when you click **Restore now**, with the **Ctrl+Alt+R** hotkey (configurable), or when you quit the app.
- If the app or the PC shuts down unexpectedly, **it undoes what was left applied** the next time it starts.
- **Test resolution** button (reverts by itself after a few seconds), event history, notifications, start with Windows, settings backup (export/import) and the app in **Portuguese and English** (General → System → Language).

## What it guarantees (and what it doesn't)

**It guarantees:**
- **It never touches the game.** It doesn't open the game process, read or write its memory, inject DLLs, hook anything, install drivers, draw overlays or capture the screen.
- **It only uses public Windows and NVIDIA features** (the same API as the QRes tool for resolution; NVIDIA's public NvAPI for saturation).
- **No internet, no account, no telemetry.** Nothing leaves your computer. Data stays in `%AppData%\GBooster`.
- **Open source**: anyone can check everything it does.
- **Fully reversible**: it only undoes what it applied itself.

**It does not guarantee:**
- **There is no "100% ban-proof" guarantee.** No game publisher releases a list of allowed programs. GBooster does
  nothing that the published rules forbid, but the final call always belongs to the anti-cheat. **Saturation** is the
  most sensitive part (it could be seen as a "visual advantage"); read the [report](RELATORIO-ANTICHEAT.md) (Portuguese) and use it at your own risk.
  At 50% saturation (default) the app doesn't touch the driver at all.
- No warranty of any kind (see the MIT license).

## Installation

1. Under **Releases**, download `GBooster-win-x64.zip`.
2. Extract it to a folder (for example `C:\GBooster`).
3. Double-click `GBooster.exe`. **No .NET installation needed.**

> **Windows SmartScreen warning:** since the app has no paid code-signing certificate, Windows may show
> "Windows protected your PC". Click **More info → Run anyway**.
> You can verify the download with the release's `SHA256.txt`.

## First run (in this order)

1. Open the app, **Jogos** (Games) tab, pick the game and the resolution (the same one the game uses).
2. Keep saturation off and click **Test resolution**. The screen switches and comes back by itself.
3. Check that **Restaurar agora** (Restore now) or Ctrl+Alt+R brings the screen back.
4. Only then try it with the game running, and saturation last.

If anything locks your screen: **Ctrl+Alt+R**.

> The app interface is currently in Portuguese (Brazil).

## GPU setup (the app doesn't do this for you)

For a stretched resolution to fill the screen, in the NVIDIA Control Panel: *Adjust desktop size and position* →
Scaling: **Full-screen**, perform scaling on **GPU**, and tick *Override the scaling mode set by games and programs*.

## Known limitations

- Saturation works on **NVIDIA GPUs only**. Resolution and Hz work on any GPU.
- Game executable names can change with game updates; you can fix them in the app (magnifier button).
- With multiple monitors, the app changes the monitor the game opens on.

## Build it yourself

Install the **.NET 8 SDK** and double-click `COMPILAR.bat` (or run
`dotnet publish -c Release -r win-x64 --self-contained true -o publicar`). The app ends up in `publicar\GBooster.exe`.

## Security, license, credits

- Found a security issue? See [SECURITY.md](SECURITY.md).
- Code under the **MIT** license (`LICENSE`), © 2026 [Matheus Gheno](https://github.com/matheusgheno).
- Uses **NvAPIWrapper.Net** (LGPL-3.0), unmodified. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
- Not affiliated with NVIDIA, Valve, Tencent/Level Infinite/Garena or EA. Trademarks belong to their owners.
