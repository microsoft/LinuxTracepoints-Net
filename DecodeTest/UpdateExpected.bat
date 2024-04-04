@echo off
setlocal
if "%~1" == "" (
    set SRC=%~dp0..\bin\Debug\netcoreapp3.1\actual
) else (
    set SRC=%~1
)
robocopy "%SRC%" "%~dp0expected" /mir /njh /njs
