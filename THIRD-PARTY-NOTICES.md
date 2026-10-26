# Avisos de terceiros

## NvAPIWrapper.Net
Usado, sem modificações, para controlar a "Vibrância digital" da NVIDIA.
- Autor: Soroush Falahati
- Projeto: https://github.com/falahati/NvAPIWrapper
- Licença: GNU LGPL v3.0 (https://www.gnu.org/licenses/lgpl-3.0.html)

O app usa a biblioteca como pacote NuGet separado (ligação dinâmica), sem alterar o código dela.
Você pode trocá-la por outra versão da mesma biblioteca.

## NVIDIA NvAPI
A biblioteca acima acessa a API pública NvAPI, que faz parte do driver da NVIDIA instalado no seu computador.
NVIDIA é marca registrada da NVIDIA Corporation.

## NVIDIA Profile Inspector
Os identificadores das opções de perfil da NVIDIA (DRS) usados pela página Desempenho foram portados do projeto NVIDIA Profile Inspector, de Orbmu2k.
- Projeto: https://github.com/Orbmu2k/nvidiaProfileInspector
- Licença: MIT

O GBooster não inclui o programa; usa só os números dessas opções, chamando a API do driver (nvapi64.dll) por conta própria.
