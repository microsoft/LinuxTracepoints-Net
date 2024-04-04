@echo off
setlocal
if "%~1" == "" (
    set SRC=%~dp0bin\Debug\netcoreapp3.1
) else (
    set SRC=%~1
)
echo Copying from "%SRC%".
for %%a in ("%~dp0*.json") do (
    if exist "%SRC%\%%~nxa.actual" copy /y "%SRC%\%%~nxa.actual" "%~dp0%%~nxa"
)
