@echo off
echo Building...

dotnet publish ToolboxApp\ToolboxApp.csproj -c Release -o publish\MultiFile

if %errorlevel% equ 0 (
    if exist "publish\MultiFile\Toolbox.exe.config" del "publish\MultiFile\Toolbox.exe.config"
    rem Two C/C++ import .lib files from onnxruntime that a .NET project doesn't need; clean them up.
    if exist "publish\MultiFile\onnxruntime.lib" del /q "publish\MultiFile\onnxruntime.lib"
    if exist "publish\MultiFile\onnxruntime_providers_shared.lib" del /q "publish\MultiFile\onnxruntime_providers_shared.lib"
    echo.
    echo Done! Output: publish\MultiFile\
    explorer publish\MultiFile
) else (
    echo.
    echo Build Failed!
)
pause