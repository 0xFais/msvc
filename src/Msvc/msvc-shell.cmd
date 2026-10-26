@echo off
call "%~dp0msvc.cmd" shell %*
exit /b %errorlevel%

