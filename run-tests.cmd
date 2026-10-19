@echo off
setlocal enabledelayedexpansion

rem The one command whose exit code is the suite's verdict: it builds PhotoBoost.slnx, then runs
rem every test application under tests\ and fails on a red one, a missing one, or none at all.
rem UI cases are not part of it; they run separately through winwright.
rem
rem Usage:  run-tests.cmd [Configuration]        default Debug

set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Debug"

rem How long one test may run before it is taken as hung, ended and named.
if "%PHOTOBOOST_HANG%"=="" set "PHOTOBOOST_HANG=10m"

pushd "%~dp0"

set "REPORTS=%CD%\TestResults\reports"
if exist "%REPORTS%" rd /s /q "%REPORTS%"
mkdir "%REPORTS%" 2>nul

echo Building PhotoBoost.slnx (%CONFIG%)
dotnet build PhotoBoost.slnx --configuration %CONFIG% --nologo -v quiet -nodeReuse:false
if errorlevel 1 (
    echo.
    echo BUILD FAILED - no tests were run.
    popd
    exit /b 1
)

set /a RAN=0
set /a FAILED=0
set "BROKEN="

for /d %%P in (tests\*) do (
    set "APP=%%P\bin\%CONFIG%\net10.0-windows\%%~nxP.exe"
    echo.
    echo === %%~nxP ===
    if exist "!APP!" (
        "!APP!" --results-directory "%REPORTS%" --hangdump --hangdump-timeout %PHOTOBOOST_HANG% --hangdump-type Mini
        if errorlevel 1 (
            set /a FAILED+=1
            set "BROKEN=!BROKEN! %%~nxP"
        )
        set /a RAN+=1
    ) else (
        echo   no test application at !APP!
        set /a FAILED+=1
        set "BROKEN=!BROKEN! %%~nxP^(missing^)"
    )
)

echo.

rem Nothing found is not a pass: a suite that silently shrinks to zero is what this guards against.
if %RAN%==0 (
    echo NO TEST APPLICATIONS FOUND under tests\ for configuration %CONFIG%.
    popd
    exit /b 1
)

if %FAILED%==0 (
    echo All %RAN% test assemblies passed.
    popd
    exit /b 0
)

echo %FAILED% of %RAN% test assemblies failed:!BROKEN!
popd
exit /b 1
