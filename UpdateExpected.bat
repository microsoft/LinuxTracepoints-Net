@echo off
setlocal
if "%~1" == "" (
    set SRC=%~dp0bin\Debug\actual
) else (
    set SRC=%~1
)
robocopy "%SRC%" "%~dp0DecodeTest\expected" /mir /njh /njs
