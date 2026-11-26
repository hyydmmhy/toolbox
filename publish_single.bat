@echo off
echo Building single-file...

dotnet publish ToolboxApp\ToolboxApp.csproj -c Release -o publish\SingleFile -p:SingleFile=true

if %errorlevel% equ 0 (
    if exist "publish\SingleFile\Toolbox.exe.config" del "publish\SingleFile\Toolbox.exe.config"
    rem Single-file publish: Costura embeds managed assemblies; native onnxruntime is gzip-embedded and extracted at runtime.
    rem So the publish dir should contain only Toolbox.exe; strip any leftover loose files.
    for %%f in (publish\SingleFile\*.dll publish\SingleFile\*.lib publish\SingleFile\*.pdb publish\SingleFile\*.xml publish\SingleFile\*.config) do (
        if exist "%%f" del /q "%%f"
    )
    echo.
    echo Done! Output: publish\SingleFile\
    explorer publish\SingleFile
) else (
    echo.
    echo Build Failed!
)
pause