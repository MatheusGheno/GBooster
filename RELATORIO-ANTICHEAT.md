# GBooster — relatório de risco de anti-cheat (pesquisa em 25/10/2026, atualizado na v1.0)

**Conclusão curta:** não existe "100% garantido". Nenhuma das três empresas publica uma lista
de programas permitidos, então ninguém consegue prometer isso. O que dá para afirmar, com base
nas regras publicadas: o app **não faz nada do que essas regras proíbem** (não toca no processo do
jogo, não injeta código, não lê memória, não automatiza entrada, não desenha overlay). O único
ponto que merece decisão sua é a **saturação**, por ser uma zona cinzenta de "vantagem visual".

## O que as regras publicadas dizem

**CS2 (VAC, Valve).** A wiki da Valve descreve o VAC como detecção de cheats conhecidos, incluindo
verificar "se algum processo estranho está ligado (hooked) aos binários do jogo", com banimento
atrasado. A mesma página admite que o VAC já baniu por engano software legítimo (gravadores,
overlays, drivers). O caso mais próximo do nosso: em 2023 a Valve baniu quem ativou o Anti-Lag+ da AMD,
dizendo "qualquer alteração no código do CS será banida com VAC" — o problema era o driver mexer
**dentro do código do jogo**, e os bans foram revertidos. O Nvidia Reflex foi citado sem risco.
Não achei nenhuma declaração oficial da Valve sobre vibrância/gamma/Painel NVIDIA; nos fóruns a
opinião é "permitido", mas isso é opinião de jogadores, não política.

**Delta Force (ACE, Tencent/Garena).** Os comunicados oficiais descrevem: detecção de programas de
cheat conhecidos, análise de comportamento (reações inumanas, movimento impossível), Secure Boot +
TPM 2.0 contra bootkits e "DMA Shield" contra hardware externo. Não há lista de programas proibidos,
nem texto sobre overlays, ferramentas de vídeo ou falsos positivos.

**Battlefield 6 (EA Javelin).** É a mais detalhada. O Javelin "fecha o jogo" se encontra cheat,
debugger ou software conflitante, e a EA mantém uma lista (não exaustiva) de **drivers** e
utilitários bloqueados; cita Daemon Tools e reWASD. A atualização da 4ª temporada (28/08/2026) lista
como proibidos: remapeadores que emulam dispositivos, automação (anti-recuo, turbo), "sound radars",
equalizadores automáticos, mira adaptativa, overlays com zoom/destaque de alvo e monitores que
removem/alteram elementos visuais (flashbang, mira). O critério declarado: se a ferramenta dá
**vantagem competitiva ou burla a detecção**. Vibrância/saturação não é citada. Num fórum oficial, um
jogador perguntou se filtros/vibrância da NVIDIA são permitidos e **a EA não respondeu**; no mesmo
tópico há opiniões de que "dá ban", todas sem fonte oficial.

## Risco por funcionalidade

| Funcionalidade | CS2 | Delta Force | BF6 | Observação |
|---|---|---|---|---|
| Ler lista de processos (Toolhelp) | Muito baixo | Muito baixo | Muito baixo | Mesma leitura do Gerenciador de Tarefas/Steam. Não abre o processo. |
| Achar o monitor pela janela | Muito baixo | Muito baixo | Muito baixo | Só pergunta posição da janela ao Windows. |
| Trocar resolução (ChangeDisplaySettingsEx) | Muito baixo | Muito baixo | Muito baixo | API padrão do Windows (QRes, os próprios jogos). Risco real é **crash/instabilidade**, não ban. |
| Iniciar com o Windows, bandeja | Nenhum | Nenhum | Nenhum | |
| **Saturação NVIDIA (NvAPI)** | Baixo (sem política oficial; precedente de uso comum) | **Indefinido** (sem política publicada) | **Indefinido/médio** (zona cinzenta de "vantagem visual"; EA não respondeu) | Não injeta nada; é o mesmo ajuste do Painel NVIDIA. O risco é de **interpretação da regra**, não técnico. |

## O que pode ir contra as diretrizes, como resolver

1. **Saturação em BF6 (e, por falta de política, Delta Force).** Pode ser interpretada como
   vantagem visual (destacar inimigos). *Correção:* deixe a saturação em **50% (padrão)** nesses dois
   jogos. O app já só mexe na saturação quando o valor é diferente de 50%, então 50% = o app nem toca
   no driver. Se quiser mesmo usar, pergunte por escrito ao suporte da EA/Level Infinite e guarde a
   resposta. Em CS2, uso moderado é o cenário de menor risco, sem garantia oficial.
2. **Trocar resolução com o jogo ainda carregando.** Não é banível, mas o jogo e o anti-cheat estão
   iniciando e podem travar. *Correção já aplicada:* espera de 5 s (padrão) antes de mexer na tela, que dá para mudar em
   Geral → Esperar antes de aplicar. Se travar em BF6/Delta Force, aumente para 10–15 s.
3. **Programa novo, sem assinatura digital.** Antivírus e anti-cheats desconfiam de executáveis
   desconhecidos (há relatos de antivírus acusando até o executável do Delta Force). Não é regra
   violada, mas pode gerar alerta. *Correção:* se for distribuir a mais gente, assinar o executável;
   para uso próprio, nada a fazer. Não instalamos driver, que é o que a lista da EA bloqueia.
4. **NvAPIWrapper.Net.** Biblioteca de terceiros carregada **no nosso processo**, nunca no jogo.
   Sem risco por si só; exige manter o aviso de licença LGPL.
5. **Ligas de terceiros (FACEIT, ESEA etc.).** Não pesquisei. Elas têm anti-cheat próprio e regras
   próprias; um comentário em fórum diz que o ESEA já baniu por vibranceGUI, sem fonte oficial.
   Se joga por elas, verifique as regras delas antes.

## Boost (Desempenho e Limpeza) e anti-cheat — adicionado na v1.0

Esta pesquisa de regras cobre a **troca de tela**. O **Boost** não mexe no jogo, mas muda o Windows
(registro, plano de energia, `bcdedit`, serviços, perfil NVIDIA e modo MSI), e por isso é um risco de natureza
diferente: **não é ban por "mexer no jogo", e sim possível incompatibilidade com anti-cheats que checam o estado do sistema.**

- O app **não** liga modo de teste, não desliga a verificação de assinatura de drivers, não instala driver,
  e **não** mexe em VBS/HVCI, Secure Boot, TPM, Spectre, ASLR ou CFG. São os itens que esses anti-cheats mais cobram.
- Os ajustes de boot são só `disabledynamictick`, `useplatformtick` e remoção de `useplatformclock` (timing).
- **Riot Vanguard (Valorant): não foi pesquisado nem testado.** O Vanguard roda como driver e é mais exigente com
  o estado do sistema. O risco mais provável não é banimento, e sim o Vanguard se recusar a abrir o jogo
  depois de ajustes de boot/driver. Se você joga Valorant: não cadastre o jogo no GBooster, otimize e reinicie o PC
  antes de abrir o Vanguard, e se der erro use **Desfazer otimizações** e reinicie.
- Nenhum ajuste do Boost roda com um jogo cadastrado aberto.

## O que não consegui verificar

- Lista interna de assinaturas/denylists (não é pública).
- Se algum desses anti-cheats registra "janela consultada por outro processo" (não há indício; o
  app não abre o processo).
- Nenhum teste real em anti-cheats: tudo acima vem das regras publicadas e da leitura do código, não de testes com contas de jogo.

## Fontes

- Valve Developer Wiki, Valve Anti-Cheat: https://developer.valvesoftware.com/wiki/Valve_Anti-Cheat
- Valve alerta sobre AMD Anti-Lag+ no CS2: https://tech.yahoo.com/general/articles/valve-warns-counter-strike-2-194750692.html
- Discussões CS2 (opinião de jogadores, sem fonte oficial): https://steamcommunity.com/app/730/discussions/0/2247803885918123102/ e https://steamcommunity.com/app/730/discussions/0/1637542668226926398/
- EA, atualização anti-cheat Temporada 4: https://www.ea.com/games/battlefield/battlefield-6/news/battlefield-6-anticheat-update-season-4
- EA Help, EA Javelin Anticheat: https://help.ea.com/en-ca/help/pc/ea-anticheat
- EA Help, regras do BF6: https://help.ea.com/en/articles/battlefield/battlefield-6/play-by-the-rules
- Fórum EA (pergunta sem resposta sobre filtros/vibrância NVIDIA): https://forums.ea.com/discussions/battlefield-6-general-discussion-en/clarification-on-software-and-peripherals/12826233
- Delta Force, Security Brief Vol. 1: https://deltaforce.garena.com/en/news/all/T735EZ
- Anti-Cheat Expert (ACE): https://intl.anticheatexpert.com/resource-center/content-102.html
- Nome do processo do Delta Force: https://steamcommunity.com/app/2507950/discussions/0/510695968495132110
- Nome do processo do BF6: https://forums.ea.com/discussions/battlefield-6-technical-issues-en/re-bf6-crashing-to-bsod-on-launch/13474083
