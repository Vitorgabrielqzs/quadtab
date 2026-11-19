@echo off
setlocal
rem Este script fica em Scripts\; o projeto e a pasta de cima.
cd /d "%~dp0.."
set "RAIZ=%CD%"
set "SAIDA=%RAIZ%\Executavel"
echo === Quadtab: publicar ===

:espera
tasklist /FI "IMAGENAME eq Quadtab.exe" 2>NUL | find /I "Quadtab.exe" >NUL
if not errorlevel 1 (
    echo O Quadtab esta aberto. Feche o app para atualizar o executavel.
    timeout /t 3 >NUL
    goto espera
)

dotnet publish Quadtab -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
if errorlevel 1 (
    echo.
    echo ERRO na compilacao. Nada foi atualizado.
    pause
    exit /b 1
)

if not exist "%SAIDA%" mkdir "%SAIDA%"
copy /Y "Quadtab\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\Quadtab.exe" "%SAIDA%\Quadtab.exe"
if errorlevel 1 (
    echo ERRO ao copiar o executavel.
    pause
    exit /b 1
)

powershell -NoProfile -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut([Environment]::GetFolderPath('Programs')+'\Quadtab.lnk'); $s.TargetPath='%SAIDA%\Quadtab.exe'; $s.WorkingDirectory='%SAIDA%'; $s.IconLocation='%SAIDA%\Quadtab.exe'; $s.Save()"

echo.
echo Pronto: Executavel\Quadtab.exe atualizado e atalho no menu Iniciar.
timeout /t 5
