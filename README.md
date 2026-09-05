# Display Profile Switcher

Utilidad para Windows 10/11 creada en C#/.NET 8.

## Funciones

- Perfiles de **gamma**, **brillo**, **contraste** y **NVIDIA Digital Vibrance**.
- Atajos globales por perfil (`Ctrl+Alt+1`, `Ctrl+Alt+2`, etc.).
- Aplicación a monitor principal o a todos los monitores.
- Reaplicación periódica opcional del perfil, útil si un juego o el driver restaura la gamma.
- Icono en la bandeja del sistema.
- Aplicar el último perfil al iniciar.
- Inicio opcional con Windows.
- Configuración en `%APPDATA%\DisplayProfileSwitcher\profiles.json`.

## Seguridad respecto a juegos

La aplicación **no inyecta DLL, no engancha DirectX y no modifica el proceso del juego**.

- Gamma/brillo/contraste: `WindowsDisplayAPI` / gamma ramp de Windows.
- Digital Vibrance: `NvAPIWrapper` / NVAPI del driver NVIDIA.
- Atajos: `RegisterHotKey` de Windows.

## Compilar

Necesitas el **.NET 8 SDK** instalado.

1. Extrae la carpeta.
2. Ejecuta `build.bat`.
3. El ejecutable autocontenido aparecerá en:

   `publish\DisplayProfileSwitcher.exe`

También puedes abrir `DisplayProfileSwitcher.sln` en Visual Studio 2022.

## Perfiles iniciales

- Normal — `Ctrl+Alt+1`
- Claro — `Ctrl+Alt+2`
- Noche — `Ctrl+Alt+3`

Los valores son solo un punto de partida y se pueden editar desde la interfaz.

## Notas

- `Gamma = 1.00`, `Brillo = 50`, `Contraste = 50` y `Vibrance = 50` son los valores neutros.
- El control de Digital Vibrance requiere una GPU NVIDIA y un driver compatible.
- La gamma puede ser sobrescrita por algunos cambios de modo de pantalla, HDR, suspensión o por el propio panel de NVIDIA; para eso existe `Reaplicar`.
