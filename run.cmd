@echo off
setlocal enabledelayedexpansion

rem Builds PhotoBoost (Debug) and starts it, to try the app by hand.
rem
rem   .\run.cmd                  a menu of manual test scenarios, each on a test image
rem   .\run.cmd [app options]    starts the app straight away with those options, e.g.
rem                              .\run.cmd --open photo.jpg --provider cpu
rem
rem Call it as .\run.cmd: with NoDefaultCurrentDirectoryInExePath set, a bare "run.cmd" finds
rem another run.cmd on the PATH (nvm ships one) before this one.
rem
rem The models export-and-quantize.py left in .cache\models are used unless --models is given;
rem without that folder the app falls back to %LOCALAPPDATA%\PhotoBoost\models. The real test
rem images come with scripts\download_models.py (.cache\weights\test-images); the fixtures are
rem committed (tests\fixtures\images). App options: docs\COMMAND-LINE.md.

pushd "%~dp0"

echo Building PhotoBoost (Debug)
dotnet build src\PhotoBoost.App\PhotoBoost.App.csproj --configuration Debug --nologo -v quiet -nodeReuse:false
if errorlevel 1 (
    echo.
    echo BUILD FAILED - the app was not started.
    popd
    exit /b 1
)

set "EXE=%CD%\src\PhotoBoost.App\bin\Debug\net10.0-windows10.0.19041.0\win-x64\PhotoBoost.exe"
set "REAL=.cache\weights\test-images"
set "FIX=tests\fixtures\images"
set "OWN_MODELS="
if exist ".cache\models" set "OWN_MODELS=.cache\models"

if not "%~1"=="" goto :direct

set "PROVIDER=auto"
set "QUALITY=compact"
set "EXPORT=off"

:menu
cls
echo  PhotoBoost - cenarios de teste manual
echo  =====================================
echo.
echo  Modelos reais (scripts\download_models.py e export-and-quantize.py)
echo    1  Upscale           0014.jpg, foto pequena e ruidosa (179 x 179)
echo    2  Restaurar rostos  Blake_Lively.jpg, dois rostos lado a lado
echo    3  Rosto alinhado    Adele_crop.png, recorte 512 ja alinhado
echo    4  Remover fundo     example_input.jpg, duas girafas sobre o ceu
echo    5  Remover ruido     building.png, foto real com ruido (SCUNet)
echo.
echo  Fixtures do repositorio
echo    6  Orientacao EXIF   rotated-exif6.jpg, deve abrir em pe (8 x 16)
echo    7  Transparencia     alpha-lossless.webp, aviso ao exportar JPEG
echo    8  Perfil de cor     p3-swatch.png, Display P3 convertido para sRGB
echo    9  Perfil quebrado   broken-profile.png, deve abrir sem converter
echo   10  Dados da camera   exif-camera.jpg, "Manter dados da camera"
echo.
echo  Outros
echo   11  Modelos minusculos (offline): rotated-exif6.jpg em cases\fixtures\models
echo   12  Sem modelos: o painel diz qual modelo falta
echo   13  Arquivo inexistente: a tela inicial diz que nao abriu
echo   14  Argumento invalido: o app recusa e sai com codigo 2
echo   15  Tela vazia, sem imagem
echo.
echo  Opcoes:  P provedor [!PROVIDER!]   Q qualidade [!QUALITY!]   E exportar ao terminar [!EXPORT!]
echo           S sair
echo.
set "PICK="
set /p "PICK=Escolha: "
if not defined PICK goto :menu

set "IMG="
set "MODELS=!OWN_MODELS!"
set "EXTRA="
if /i "!PICK!"=="S" goto :done
if /i "!PICK!"=="P" goto :provider
if /i "!PICK!"=="Q" goto :quality
if /i "!PICK!"=="E" goto :export
if "!PICK!"=="1" set "IMG=%REAL%\0014.jpg"
if "!PICK!"=="2" set "IMG=%REAL%\Blake_Lively.jpg"
if "!PICK!"=="3" set "IMG=%REAL%\Adele_crop.png"
if "!PICK!"=="4" set "IMG=%REAL%\example_input.jpg"
if "!PICK!"=="5" set "IMG=%REAL%\building.png"
if "!PICK!"=="6" set "IMG=%FIX%\rotated-exif6.jpg"
if "!PICK!"=="7" set "IMG=%FIX%\alpha-lossless.webp"
if "!PICK!"=="8" set "IMG=%FIX%\p3-swatch.png"
if "!PICK!"=="9" set "IMG=%FIX%\broken-profile.png"
if "!PICK!"=="10" set "IMG=%FIX%\exif-camera.jpg"
if "!PICK!"=="11" set "IMG=%FIX%\rotated-exif6.jpg"
if "!PICK!"=="11" set "MODELS=cases\fixtures\models"
if "!PICK!"=="12" set "IMG=%FIX%\rotated-exif6.jpg"
if "!PICK!"=="12" set "MODELS=%FIX%"
if "!PICK!"=="13" set "IMG=%FIX%\missing.png"
if "!PICK!"=="14" set "EXTRA=--provider gpu"
if "!PICK!"=="15" goto :launch

if defined EXTRA goto :launch
if not defined IMG goto :unknown
rem Scenario 13 opens a file that is not there on purpose; any other missing image needs the downloads.
if "!PICK!"=="13" goto :launch
if not exist "!IMG!" goto :absent
goto :launch

:unknown
echo  Opcao desconhecida: !PICK!
pause
goto :menu

:absent
echo.
echo  !IMG! nao existe. Rode antes:
echo    .venv\Scripts\python scripts\download_models.py
echo    .venv\Scripts\python scripts\export-and-quantize.py
pause
goto :menu

:launch
rem Scenario 14 passes its own bad --provider, so the toggles are left out for it.
set "ARGS=--provider !PROVIDER! --quality !QUALITY!"
if defined EXTRA set "ARGS=!EXTRA!"
if defined MODELS set ARGS=!ARGS! --models "!MODELS!"
if defined IMG set ARGS=!ARGS! --open "!IMG!"
rem --export never overwrites, so each run gets a fresh name under artifacts\manual (git-ignored).
if /i "!EXPORT!"=="on" if defined IMG if not exist "artifacts\manual" mkdir "artifacts\manual"
if /i "!EXPORT!"=="on" if defined IMG set ARGS=!ARGS! --export "artifacts\manual\result-!RANDOM!!RANDOM!.png"
echo.
echo  Abrindo: PhotoBoost.exe !ARGS!
start "PhotoBoost" "%EXE%" !ARGS!
timeout /t 2 >nul 2>&1
goto :menu

:provider
if /i "!PROVIDER!"=="auto" (set "PROVIDER=dml") else if /i "!PROVIDER!"=="dml" (set "PROVIDER=cpu") else (set "PROVIDER=auto")
goto :menu

:quality
if /i "!QUALITY!"=="compact" (set "QUALITY=high") else (set "QUALITY=compact")
goto :menu

:export
if /i "!EXPORT!"=="off" (set "EXPORT=on") else (set "EXPORT=off")
goto :menu

:direct
set "MODELS_ARG="
echo %* | findstr /i /c:"--models" >nul
if errorlevel 1 if defined OWN_MODELS set "MODELS_ARG=--models !OWN_MODELS!"
echo Starting %EXE% !MODELS_ARG! %*
start "PhotoBoost" "%EXE%" !MODELS_ARG! %*

:done
popd
endlocal
