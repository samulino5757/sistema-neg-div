@echo off
cd /d "%~dp0"
echo Abra http://localhost:5072 no navegador.
echo Pressione Ctrl+C para encerrar o Negocia.
Negocia.exe --urls http://localhost:5072
