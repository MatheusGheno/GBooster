# Segurança

## Como relatar uma falha

Encontrou um problema de segurança no GBooster? **Não abra uma issue pública.**
Envie um e-mail para `contato@matheusgheno.com` com: o que aconteceu, como reproduzir e a versão do app.
Respondo em até 7 dias.

## O que o app faz e não faz

- Não usa internet, não envia dados e não tem telemetria.
- Não pede administrador, não instala driver, não injeta código e não lê a memória de nenhum programa.
- Grava apenas em `%AppData%\GBooster` e, se você marcar "Iniciar com o Windows", em `HKCU\...\Run`.

## Verifique o que baixou

Cada versão publicada traz o hash SHA-256 do `GBooster.exe`. No PowerShell:

    Get-FileHash .\GBooster.exe -Algorithm SHA256

O resultado deve ser igual ao da página da versão.

## Aviso

Programa independente, sem ligação com Valve, EA, Tencent/Level Infinite/Garena, FACEIT, ESEA, Gamers Club
ou NVIDIA. Use por sua conta e risco: nenhuma dessas empresas publica uma lista completa de programas
permitidos, e as regras de cada jogo e plataforma podem mudar. Os nomes dos jogos pertencem aos seus donos.
