@echo off
REM ==========================================================================
REM update-release.cmd - Define a versao do PhotoBoost e gera o release em um
REM passo. Depois do update-release.cmd do claude-tray.
REM   1) Atualiza a versao em Directory.Build.props e app.manifest.
REM   2) Roda os testes (run-tests.cmd): um release nao sai de um build vermelho.
REM   3) Chama build-installer.cmd (publish + avisos de terceiros + instalador).
REM   4) [opcional] Se o 2o parametro for "upload": commita a versao, envia,
REM      cria e envia a tag vX.Y.Z e cria a release no GitHub com o instalador
REM      anexado.
REM
REM Uso (de qualquer pasta):  build\update-release 0.1.0
REM                           build\update-release 0.1.0 upload
REM ==========================================================================
setlocal

REM Este script vive em build\; o commit, a tag e o instalador sao da pasta acima.
cd /d "%~dp0.."

if "%~1"=="" (
    echo *** ERRO: informe a versao. ***
    echo Uso: build\update-release 0.1.0 [upload]
    exit /b 1
)

REM --- 1) A versao, nos dois lugares que a carregam ---------------------------
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0update-release.ps1" -Version "%~1"
if errorlevel 1 (
    echo.
    echo *** ERRO: falha ao atualizar a versao. ***
    exit /b 1
)

REM --- 2) Os testes, inclusive o que mantem as duas versoes iguais ------------
call "%~dp0..\run-tests.cmd"
if errorlevel 1 (
    echo.
    echo *** ERRO: os testes falharam; nenhum instalador foi gerado. ***
    exit /b 1
)

REM --- 3) Publish + instalador ------------------------------------------------
call "%~dp0build-installer.cmd"
if errorlevel 1 (
    echo.
    echo *** ERRO: falha ao gerar o instalador. ***
    exit /b 1
)

set "SETUP=%CD%\artifacts\installer\PhotoBoost-Setup-%~1.exe"
if not exist "%SETUP%" (
    echo *** ERRO: o instalador esperado nao existe: %SETUP% ***
    exit /b 1
)

REM --- 4) Upload (opcional): commit + push + tag + release no GitHub -----------
if /i not "%~2"=="upload" goto :local

echo.
echo === Publicando release v%~1 no GitHub ===
echo.

REM Nada para commitar quando a versao ja era esta (o primeiro release, por exemplo):
REM o run-commit.cmd so roda se o git ve alguma mudanca.
git diff --quiet
if errorlevel 1 (
    call run-commit.cmd -m "chore: release v%~1"
    if errorlevel 1 (
        echo *** ERRO: falha ao commitar a versao. ***
        exit /b 1
    )
)

git push
if errorlevel 1 (
    echo *** ERRO: falha no git push. ***
    exit /b 1
)

git tag "v%~1"
if errorlevel 1 (
    echo *** ERRO: falha ao criar a tag v%~1 - talvez ja exista. ***
    exit /b 1
)
git push origin "v%~1"
if errorlevel 1 (
    echo *** ERRO: falha ao enviar a tag v%~1. ***
    exit /b 1
)

REM A release com o instalador anexado e as notas geradas pelo GitHub.
gh release create "v%~1" "%SETUP%" --title "PhotoBoost v%~1" --generate-notes
if errorlevel 1 (
    echo *** ERRO: falha ao criar a release no GitHub. ***
    exit /b 1
)

echo.
echo === Release v%~1 publicada ===
echo.
goto :fim

:local
echo.
echo === Release v%~1 gerado localmente ===
echo Instalador: %SETUP%
echo Nada foi commitado nem enviado. Para publicar:  build\update-release %~1 upload
echo.

:fim
endlocal
