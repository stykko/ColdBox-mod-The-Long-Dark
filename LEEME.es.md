# ColdBox — guía rápida (el detalle completo está en `README.md`, en inglés)

Nevera de nieve para The Long Dark 2.55 (MelonLoader 0.7.2). Se craftea, se coloca, se llena de nieve y la comida guardada se echa a perder más despacio.

## Requisitos
- MelonLoader 0.7.2 y **ModSettings** (`ModSettings.dll` en `Mods\`). No hace falta ningún otro mod.

## Instalar
Copiá `ColdBox.dll` a `<carpeta del juego>\Mods\`.

## Cómo se usa
- **Craftear** en el banco de trabajo: 10 leña de cedro, 5 trozos de metal, 5 lonas y herramientas de calidad (las herramientas no se gastan).
- **Colocar:** inventario > Snow Cooler > Place / Usar. Click izquierdo coloca, click derecho o Esc cancela, rueda o Q/E giran.
- **Abrir:** mirá la nevera y mantené apretado el botón de interacción. Menú: cargar nieve, comida, recoger agua (cuando se derritió toda la nieve), mover o retirar, cerrar tapa.
- **Retirar:** con la nevera vacía, "Remove" la devuelve al inventario.
- **Guardado:** `UserData\ColdBox\<slot>.json`.
- **Obtención:** solo se consigue crafteándola en el banco de trabajo.

## Compilar
Abrí `ColdBox.sln` en Visual Studio, ajustá `GameDir.props` si hace falta, abrí el juego una vez con MelonLoader y compilá (Ctrl+Shift+B). El DLL va solo a `Mods\`.
- **Idioma:** en Mod Settings hay una opción "Language / Язык" (inglés por defecto, ruso opcional). Los textos están en `ColdBoxLoc.cs`; para agregar otro idioma hay que sumar otro diccionario ahí.
