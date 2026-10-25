@echo off
cd /d "%~dp0"
echo ==================================================
echo   GBooster - criando o programa (GBooster.exe)
echo ==================================================
echo.

where dotnet >nul 2>&1
if errorlevel 1 goto SEM_DOTNET

dotnet --list-sdks | findstr /b "8. 9. 10." >nul 2>&1
if errorlevel 1 goto SEM_DOTNET

echo Compilando... isso leva de 1 a 3 minutos na primeira vez.
echo.
dotnet publish GBooster.csproj -c Release -r win-x64 --self-contained true -o publicar > compilar-log.txt 2>&1
if errorlevel 1 goto ERRO

echo.
echo ==================================================
echo   PRONTO!
echo   O programa esta na pasta "publicar".
echo   Abra o arquivo GBooster.exe.
echo   (Ja leva o .NET junto: funciona em qualquer Windows 10/11 x64.)
echo ==================================================
start "" "%~dp0publicar"
timeout /t 3 /nobreak >nul
exit /b 0

:SEM_DOTNET
echo Falta instalar o .NET 8 SDK (e gratuito, da Microsoft).
echo.
echo Vou tentar instalar agora pelo Windows (winget).
echo Se aparecer uma janela pedindo permissao, clique em Sim.
echo.
winget install --id Microsoft.DotNet.SDK.8 -e --accept-package-agreements --accept-source-agreements
echo.
echo ==================================================
echo   Se a instalacao terminou sem erro:
echo   FECHE esta janela e de dois cliques em COMPILAR.bat de novo.
echo   (Se deu erro, instale manualmente em:
echo    https://dotnet.microsoft.com/download/dotnet/8.0  -> ".NET SDK")
echo ==================================================
pause
exit /b 1

:ERRO
echo.
echo ==================================================
echo   DEU ERRO NA COMPILACAO.
echo   Nao se preocupe: isso e esperado no primeiro teste.
echo   Me envie o arquivo "compilar-log.txt" desta pasta
echo   (ou um print desta janela) que eu corrijo.
echo ==================================================
echo.
type compilar-log.txt | findstr /i "error"
echo.
pause
exit /b 1
