# Guía en español: ejecutar el proyecto e integrarlo en Unity

`script-to-intent-utt` recibe una frase en español y responde con una de las
122 grabaciones en náhuatl. Esta guía explica cómo instalarlo y probarlo en
Windows, Linux y macOS, y cómo llevarlo al proyecto de Unity.

Contenido:

1. [Qué contiene el proyecto](#1-qué-contiene-el-proyecto)
2. [Instalación por sistema operativo](#2-instalación-por-sistema-operativo)
3. [Descargar el proyecto](#3-descargar-el-proyecto)
4. [Ejecutar las pruebas](#4-ejecutar-las-pruebas)
5. [Ejecutar la demo](#5-ejecutar-la-demo)
6. [Frases de prueba](#6-frases-de-prueba)
7. [Regenerar el audio y la base de datos](#7-regenerar-el-audio-y-la-base-de-datos)
8. [Integrar en Unity](#8-integrar-en-unity)
9. [Problemas frecuentes](#9-problemas-frecuentes)

---

## 1. Qué contiene el proyecto

| Carpeta / archivo | Qué es |
|---|---|
| `src/Intent/` | La biblioteca C# que reconoce la intención. Son archivos `.cs` sin dependencias; se copian tal cual a Unity. |
| `data/phrases.json` | La base de datos de frases: una fila por grabación, con palabras clave en español, texto náhuatl, traducción y nombre del clip. Se copia a Unity. |
| `audio/raw/` | Los 122 MP3 originales de la carpeta de Drive, sin tocar. |
| `audio/clean/` | Las mismas grabaciones en WAV (44.1 kHz, mono), recortadas y niveladas. Se copian a Unity. |
| `src/Demo/` | Programa de terminal para probar: escucha el micrófono con Whisper, reconoce la intención y reproduce el clip. |
| `tools/` | Scripts de Python que preparan el audio y construyen la base de datos. Solo hacen falta si cambian las grabaciones o el vocabulario. |
| `tests/Intent.Tests/` | Pruebas automáticas rápidas (sin audio). |
| `tests/Integration/` | Prueba lenta con Whisper sobre grabaciones propias. |
| `review.csv`, `missing.csv`, `cleanup.log`, `inventory.csv` | Hojas de revisión para el equipo de lengua. |
| `docs/` y `unity/` | Esta guía y un componente de ejemplo para Unity. |

---

## 2. Instalación por sistema operativo

Se necesita:

- **.NET SDK** (8 o más reciente) para la biblioteca, las pruebas y la demo.
- **Git** para descargar el proyecto.
- **sox** solo para grabar con el micrófono en la demo.
- **Python 3** y **ffmpeg** solo para regenerar el audio o la base de datos (sección 7).

### Windows 10 / 11

Abre **PowerShell** (no hace falta ser administrador para `winget`).

```powershell
winget install Microsoft.DotNet.SDK.8
winget install Git.Git
winget install Gyan.FFmpeg            # solo para la sección 7
winget install Python.Python.3.12     # solo para la sección 7
```

`sox` no está en winget. Opciones:

- Con Chocolatey (https://chocolatey.org): `choco install sox.portable`
- O descarga el instalador de https://sourceforge.net/projects/sox/ y añade su
  carpeta (por ejemplo `C:\Program Files (x86)\sox-14-4-2`) al `PATH`.

Cierra y vuelve a abrir PowerShell para que el `PATH` se actualice. Comprueba:

```powershell
dotnet --version
git --version
sox --version
ffmpeg -version
```

Para que Python muestre bien las tildes y la ñ en la consola:

```powershell
$env:PYTHONUTF8 = "1"
```

### Linux (Ubuntu 22.04 / 24.04, Debian)

```sh
sudo apt update
sudo apt install -y dotnet-sdk-8.0 git sox libsox-fmt-all ffmpeg python3 python3-pip
sudo apt install -y pulseaudio-utils alsa-utils      # reproductores paplay / aplay para la demo
```

En otras distribuciones instala los mismos paquetes con su gestor (`dnf`,
`pacman`). Si tu distribución no tiene `dotnet-sdk-8.0`, sigue
https://learn.microsoft.com/dotnet/core/install/linux.

### macOS (Intel o Apple Silicon)

Con Homebrew (https://brew.sh):

```sh
brew install --cask dotnet-sdk        # cualquier .NET SDK 8 o más reciente
brew install git sox ffmpeg python
```

El reproductor `afplay` viene con macOS. La primera vez que uses el micrófono,
macOS pedirá permiso para la Terminal; acéptalo.

---

## 3. Descargar el proyecto

```sh
git clone https://github.com/modullar/script-to-intent-utt.git
cd script-to-intent-utt
```

En Windows, los mismos comandos funcionan en PowerShell. Todo lo que sigue se
ejecuta **desde la carpeta raíz del proyecto** (donde está
`NahuatlIntent.sln`). Si ejecutas `dotnet test` en otra carpeta verás el
error `MSB1003: Specify a project or solution file`.

---

## 4. Ejecutar las pruebas

```sh
dotnet test
```

La primera vez descarga unos paquetes de NuGet. Debe terminar con
`Passed! ... Passed: 62` en pocos segundos. Si tienes Python y ffmpeg:

```sh
pip install -r tools/requirements.txt      # en Linux puede hacer falta: pip install --user ...
python -m pytest tools -q                  # en Linux/macOS: python3
```

Las pruebas de C# usan la base de datos real y el archivo
`tests/Intent.Tests/cases.csv`, donde cada línea es `frase ; clip esperado`.
Puedes añadir líneas y volver a ejecutar `dotnet test`.

---

## 5. Ejecutar la demo

### Sin micrófono (la forma más rápida de probar)

```sh
dotnet run --project src/Demo -- --text "cuántos perros hay"
```

La demo carga la base de datos, saluda con el clip 001 y después imprime:

```
transcript  (typed): cuántos perros hay
normalised  : cuantos perros hay
keywords hit: perros
intent      : perro  score=1
clip        : Audio-095-2026.wav  [095]
nahuatl     : Itskuintli
spanish     : Perro
```

y reproduce el clip. Con `--no-audio` no reproduce nada (útil sin altavoces).

### Con un archivo de audio

Cualquier WAV de 16 kHz, mono, 16 bits. Si tienes una grabación en otro
formato, conviértela con ffmpeg:

```sh
ffmpeg -i grabacion.m4a -ar 16000 -ac 1 -sample_fmt s16 frase.wav
dotnet run --project src/Demo -- --wav frase.wav
```

### Con el micrófono

```sh
dotnet run --project src/Demo
```

- **Primera vez:** descarga el modelo de Whisper `ggml-small` (unos 488 MB)
  a la caché del usuario (`~/.cache/script-to-intent-utt/` en Linux y macOS,
  `C:\Users\<tú>\.cache\script-to-intent-utt\` en Windows). Imprime el
  avance cada 50 MB. **No interrumpas la descarga.** Si se interrumpe, la
  siguiente ejecución lo detecta y vuelve a descargar.
- **macOS y Linux:** pulsa Enter, habla, pulsa Enter otra vez. La grabación
  termina cuando pulsas Enter.
- **Windows:** pulsa Enter y habla; la grabación dura 5 segundos (Windows no
  permite detener `sox` limpiamente). Cambia la duración con `--seconds 8`.
- Escribe `q` y Enter para salir.

Opciones:

| Opción | Efecto |
|---|---|
| `--text "frase"` | No usa Whisper; reconoce la frase escrita. |
| `--wav archivo.wav` | Transcribe ese archivo en lugar del micrófono. |
| `--seconds N` | Graba N segundos fijos (en Windows siempre; por defecto 5). |
| `--no-audio` | No reproduce clips. |
| `--model ruta` | Usa un modelo ggml ya descargado. |
| `--threshold 0` | Desactiva la tolerancia a erratas (Levenshtein). |

Reproducción de audio: `afplay` en macOS, `paplay`/`aplay`/`ffplay` en Linux,
PowerShell en Windows. Si ninguno funciona, la demo lo dice una vez y sigue
sin sonido.

---

## 6. Frases de prueba

Frases sencillas que se pueden escribir con `--text` o decir al micrófono, y lo
que responde el personaje. El significado del náhuatl es el que da el
documento de vocabulario.

| Dices (español) | Clip | Náhuatl | El clip dice |
|---|---|---|---|
| buenas noches | 018 | Kuali yowal | buenas noches |
| cuántos perros hay | 095 | Itskuintli | perro |
| mi abuela | 089 | Sijtli | abuela |
| quiero una tortilla | 061 | Tlaxcali | tortilla |
| buenos días | 055 | Quenon tomixtonaltitzino | buenos días, ¿cómo está usted? |
| gracias | 068 | Tlasojkamati miek | muchas gracias |
| ven aquí | 026 | Xiuala nikan | ven aquí |
| cómo se dice te amo | 111 | Nimitstlasojtla | te amo |
| casa | 034 | Kali | casa |
| agua | 029 | Atl | agua |
| me duele la cabeza | 093 (respuesta de reserva) | Okzepa | otra vez |

La última cae en la respuesta de reserva a propósito: todavía no existen
frases de plantas ni de malestares en el vocabulario. Cuando el equipo las
escriba y grabe, se añaden al documento y se regenera la base de datos
(sección 7).

Cómo decide el programa: normaliza la frase (minúsculas, sin tildes ni
signos), busca las palabras clave de cada fila, suma puntos (una palabra clave
de varias palabras vale un punto por palabra) y responde con la fila de mayor
puntuación. Si nada coincide o hay empate entre dos intenciones, responde con
el clip 093 ("otra vez"). Los detalles están en el `README.md` en inglés.

---

## 7. Regenerar el audio y la base de datos

Solo hace falta cuando cambian las grabaciones o el documento de vocabulario.
Requiere Python 3, ffmpeg y `pip install -r tools/requirements.txt`. En
Windows usa `python` en lugar de `python3` y `tools\inventory.py` o
`tools/inventory.py` indistintamente.

```sh
python3 tools/inventory.py    # lee audio/raw, escribe inventory.csv y missing.csv
python3 tools/cleanup.py      # recorta y nivela: audio/raw -> audio/clean, añade a cleanup.log
python3 tools/build_db.py     # documento + inventario -> data/phrases.json y review.csv
dotnet test                   # comprueba que las frases de prueba siguen funcionando
```

- Para nuevas grabaciones: cópialas a `audio/raw/` con el nombre
  `Audio-NNN-2026.mp3` y añade la fila `NNN` al documento
  `data/source/Vocabulario_nahuatl.docx` (o a un TSV con columnas id,
  nahuatl, spanish).
- Para cambiar palabras clave, marcar una fila como no aprobada o elegir qué
  grabación es la principal entre duplicadas: edita `tools/overrides.json` y
  ejecuta `build_db.py`. Todo lo que hay en ese archivo aparece también en
  `review.csv`.
- `cleanup.py` imprime `LISTEN` junto a los clips que recortó a menos de un
  tercio de su duración original; conviene escucharlos.

---

## 8. Integrar en Unity

Lo que se lleva a Unity son archivos; no hay paquetes que instalar para el
reconocimiento de intención. Probado con la lógica en .NET; la biblioteca es
netstandard2.1 y no usa LINQ, reflexión ni `dynamic`, así que IL2CPP para
Android (Quest 3) la compila sin cambios.

### Paso 1. Copiar la biblioteca

Copia los siete archivos de `src/Intent/` a `Assets/Scripts/Phase3/`:

```
PhraseRow.cs  PhraseFile.cs  PhraseTable.cs  Normalizer.cs
IntentMatcher.cs  MatchResult.cs  MiniJson.cs
```

Todos están en el espacio de nombres `NahuatlIntent`. No copies
`Intent.csproj`.

### Paso 2. Copiar los datos

```
Assets/StreamingAssets/phrases.json          <- data/phrases.json
Assets/StreamingAssets/clips/*.wav           <- audio/clean/*.wav   (122 archivos, 16 MB)
```

Si prefieres que Unity importe los WAV como `AudioClip` normales, cópialos a
`Assets/Resources/clips/` en lugar de StreamingAssets y cárgalos con
`Resources.Load<AudioClip>("clips/Audio-095-2026")`. El ejemplo de abajo usa
StreamingAssets, que conserva los nombres de archivo que aparecen en
`phrases.json`.

### Paso 3. Cargar la tabla y reconocer

`unity/NahuatlNpc.cs` es un `MonoBehaviour` de ejemplo listo para copiar a
`Assets/Scripts/Phase3/`. Hace tres cosas:

1. Lee `phrases.json` con `UnityWebRequest` y construye la tabla:

   ```csharp
   var file    = JsonUtility.FromJson<PhraseFile>(jsonText);
   var table   = new PhraseTable(file);
   var matcher = new IntentMatcher(table);
   ```

   Se lee con `UnityWebRequest` y no con `File.ReadAllText` porque en
   Android (Quest 3) la carpeta StreamingAssets está dentro del APK y no es
   un directorio normal. En el editor y en escritorio funciona igual.

2. Recibe el texto del reconocedor de voz y elige el clip:

   ```csharp
   MatchResult r = matcher.Match(textoEnEspanol);
   // r.Row.clip   -> "Audio-095-2026.wav"
   // r.Row.nahuatl, r.Row.spanish -> subtítulos
   // r.IsFallback -> true cuando no entendió (clip 093, "otra vez")
   ```

3. Carga el WAV con `UnityWebRequestMultimedia.GetAudioClip` y lo reproduce
   en un `AudioSource`.

Añade el componente a Luis, asigna el `AudioSource` y llama a
`OnTranscript(texto)` cada vez que el reconocedor devuelva una frase.

### Paso 4. Reconocimiento de voz con whisper.unity

En la demo de escritorio la transcripción la hace `whisper.net`
(`src/Demo/Transcriber.cs`). En Unity la hace **whisper.unity**
(https://github.com/Macoron/whisper.unity), que corre dentro de la app sin
red:

1. Window > Package Manager > `+` > *Add package from git URL*:
   `https://github.com/Macoron/whisper.unity.git?path=/Packages/com.whisper.unity`
2. Copia un modelo ggml a `Assets/StreamingAssets/Whisper/`. Para la Quest 3
   empieza con `ggml-tiny.bin` o `ggml-base.bin` (decenas de MB); `ggml-small`
   es más preciso pero más lento en el visor. Los modelos están en
   https://huggingface.co/ggerganov/whisper.cpp/tree/main.
3. Añade un `WhisperManager` a la escena, indica la ruta del modelo relativa a
   StreamingAssets y el idioma `es`.
4. Graba con el micrófono (el paquete trae `MicrophoneRecord`) y, al terminar,
   pide el texto y pásalo al NPC:

   ```csharp
   var result = await whisper.GetTextAsync(clipGrabado);
   npc.OnTranscript(result.Result);
   ```

   Comprueba los nombres exactos en el README de la versión de whisper.unity
   que instales; la API ha cambiado entre versiones.

Lo único que cambia respecto a la demo es de dónde viene el texto. La tabla,
el reconocedor de intención y los clips son los mismos archivos.

### Notas para Android / Quest 3

- IL2CPP y *managed code stripping*: no hace falta `link.xml`; la biblioteca
  no usa reflexión y `JsonUtility` conserva los campos públicos de
  `PhraseRow` y `PhraseFile` porque el código los referencia.
- Permiso de micrófono: `android.permission.RECORD_AUDIO` se añade solo al
  usar `Microphone`, pero conviene pedirlo con
  `Permission.RequestUserPermission(Permission.Microphone)` al arrancar.
- El modelo de Whisper y los 122 WAV viajan dentro del APK. Si el tamaño es
  un problema, importa los WAV como `AudioClip` (Unity los comprime en
  Vorbis) y usa un modelo `tiny` o `base`.

---

## 9. Problemas frecuentes

**`MSB1003: Specify a project or solution file`**
Estás en otra carpeta. Ejecuta los comandos desde la raíz del proyecto, donde
está `NahuatlIntent.sln`.

**`Failed to load the whisper model` o `Model file looks incomplete`**
Una descarga anterior se interrumpió. Borra el archivo
`ggml-small.bin` de la caché (ruta en la sección 5) y vuelve a ejecutar; o
descárgalo con el navegador desde
https://huggingface.co/ggerganov/whisper.cpp/tree/main y pásalo con
`--model`.

**`sox not found`**
Instala sox (sección 2) y abre una terminal nueva. Mientras tanto puedes
probar con `--text` o `--wav`.

**`Recording failed or too short`**
No hay micrófono por defecto o el sistema no dio permiso a la terminal. En
macOS: Ajustes > Privacidad y seguridad > Micrófono. En Linux prueba
`sox -d -r 16000 -c 1 prueba.wav` a mano para ver el error de ALSA/Pulse.

**No se oye nada**
La demo avisa si ningún reproductor funcionó. En Linux instala
`pulseaudio-utils` o `alsa-utils`. Con `--no-audio` se prueba sin sonido.

**Whisper entiende otra cosa**
La demo imprime siempre la transcripción (`transcript`). Si la frase llega
mal, el problema está en el reconocimiento de voz, no en la tabla; prueba la
misma frase con `--text` para confirmarlo. Un modelo más grande o un
micrófono mejor suelen resolverlo.

**Tildes y ñ se ven mal en Windows**
Ejecuta `$env:PYTHONUTF8 = "1"` (PowerShell) o `chcp 65001` antes de los
scripts de Python. El reconocimiento no depende de ello: las tildes se quitan
al normalizar.
