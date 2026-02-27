@echo off
setlocal

powershell -ExecutionPolicy Bypass -File "%~dp0install-transcriber.ps1" %*
exit /b %errorlevel%
