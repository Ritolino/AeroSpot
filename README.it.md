# AeroSpot

[![Piattaforma](https://img.shields.io/badge/Piattaforma-Windows%2011%20x64-blue?logo=windows&logoColor=white)](https://github.com/Ritolino/AeroSpot)
[![Runtime](https://img.shields.io/badge/.NET-10.0%20Self--Contained%20(ReadyToRun)-purple?logo=dotnet)](https://dotnet.microsoft.com/)
[![UI](https://img.shields.io/badge/UI-WPF%20Fluent%20Dark-black)](https://github.com/Ritolino/AeroSpot)
[![Release](https://img.shields.io/badge/Release-v1.0.0-orange)](https://github.com/Ritolino/AeroSpot/releases/tag/v1.0.0)
[![Licenza](https://img.shields.io/badge/Licenza-MIT-brightgreen.svg)](LICENSE)

AeroSpot è un'applicazione nativa per **Windows 11**, ultra-leggera e rispettosa della privacy, progettata per unire la velocità di ricerca di un launcher in stile **Spotlight** con l'immediatezza delle anteprime file di **QuickLook**, mantenendo un consumo di memoria residente dell'Host a riposo inferiore a **10 MB di RAM**.

[**🇬🇧 Read the English README (README.md)**](README.md)

---

### ⬇️ Scarica l'Applicazione Portable Pre-compilata (Nessuna Installazione Richiesta)

| Piattaforma | Formato Pacchetto | Architettura | Link per il Download |
| :--- | :--- | :--- | :--- |
| **🪟 Windows 11** | Portable Standalone (.zip) | x64 (Intel / AMD) | [**Scarica AeroSpot v1.0.0 (.zip)**](https://github.com/Ritolino/AeroSpot/releases/download/v1.0.0/AeroSpot-v1.0.0-win-x64-portable.zip) |

*È sufficiente scaricare il file `.zip`, estrarlo in qualsiasi cartella del computer ed eseguire `AeroSpot.Host.exe`.*

---

## ✨ Caratteristiche Principali

- **🔒 100% Offline e Zero Telemetria**: Tutte le ricerche, l'indicizzazione e la generazione delle anteprime avvengono rigorosamente ed esclusivamente sul tuo computer locale. Zero connessioni di rete, zero telemetria o analytics, zero dipendenze dal cloud.
- **⚡ Architettura a Doppio Processo a Bassissimo Consumo**:
  - **AeroSpot Host**: Processo coordinatore residente Win32 nativo che consuma rigorosamente **< 10 MB di Private Bytes** a riposo.
  - **AeroSpot Worker**: Motore grafico WPF ad alte prestazioni istanziato solo al bisogno; restituisce interamente tutta la memoria a Windows dopo 30 secondi di inattività (con periodo di grazia di 10 secondi tra anteprime consecutive).
- **🚀 Ricerca Rapida Spotlight (`Alt + Spazio`)**:
  - Scorciatoia globale richiamabile all'istante da qualsiasi schermata o applicazione a tutto schermo.
  - Motore di ricerca SQLite (`index.db`) con latenze inferiori al millisecondo per applicazioni, strumenti di sistema, documenti e cartelle.
  - **Calcolatrice Integrata**: Digita formule matematiche al volo (`2+2`, `sqrt(144)`, `sin(pi/2)`) per visualizzare il risultato immediato.
  - **Convertitore di Unità Istantaneo**: Conversioni in tempo reale per volumi (litri, millilitri, galloni), massa/peso (grammi, ettogrammi, chilogrammi, libbre, once), lunghezze, temperature e valute.
- **👁 Anteprime Immediate QuickLook (`Spazio`)**:
  - Funziona nativamente all'interno di **Esplora Risorse**, sul **Desktop** o direttamente tra i **Risultati di Ricerca**.
  - **Visualizzatore PDF Vettoriale Multi-Pagina**: Rendering vettoriale WinRT nativo ad alta definizione (`Windows.Data.Pdf`), rilevamento automatico aspect ratio (panoramico/ritratto), scorrimento verticale, zoom fluido `Ctrl + Rotella` (0.4x - 4.0x), ripristino 1:1 e pan con trascinamento del mouse.
  - **Player Video Nativo**: Riproduzione fluida per MP4, MKV, AVI, MOV, WMV, WEBM, FLV e TS. Supporto a filmati oltre 1 ora con formato orario (`hh:mm:ss`), barra timeline scrubber interattiva con click istantaneo e pulsanti vettoriali frameless.
  - **Player Audio con Vinile Rotante a 33 Giri**: Riproduzione per MP3, WAV, M4A, FLAC, AAC, WMA e OGG con piatto vinile vintage animato, stanghetta del giradischi che avanza sul solco con la durata e rientra a lato in pausa/stop senza resettare la rotazione.
  - **Visualizzatore 3D STL Interattivo**: Motore 3D nativo a 60 FPS per formati STL binari e ASCII con rotazione orbitale al mouse, zoom e conteggio triangoli.
  - **Visualizzatore Fogli di Calcolo Excel**: Lettore OpenXML nativo ad alte prestazioni per file `.xlsx`, `.xlsm` e `.xltx` con schede tab multi-foglio e scrollbar dark fluida.
  - **Visualizzatore Testo e Codice Sorgente**: Anteprima ultra-rapida per oltre 20 estensioni (.txt, .md, .py, .json, .csv, .cs, .xml, .yaml, .sql, ecc.) con font monospazio, rilevamento codifica automatico e conteggio righe.
  - **Shell Preview Handler COM**: Integrazione per documenti Word (.docx), PowerPoint (.pptx) e Rich Text (.rtf).
  - **Scheda Cartelle e Metadati**: Visualizzazione istantanea del percorso, data di modifica e calcolo asincrono delle dimensioni complessive su disco, numero di file e sottocartelle.
- **🌐 Interfaccia Bilingue (Italiano & English)**:
  - Rileva automaticamente la lingua del sistema operativo al primo avvio.
  - Cambio lingua istantaneo dal pannello Impostazioni senza necessità di riavviare l'applicazione.
- **🛡 Modalità Portable & Avvio Automatico Pulito**:
  - Supporta la portabilità totale su supporti USB tramite `portable.txt`, memorizzando tutti i dati in `<CartellaApp>\Data\`.
  - Configurazione avvio con Windows con autoriparazione del percorso e rimozione completa tramite `AeroSpot.Host.exe --cleanup`.

---

## ⌨️ Scorciatoie da Tastiera Principali

| Scorciatoia | Contesto | Azione |
|---|---|---|
| **`Alt + Spazio`** | Ovunque | Apre o chiude la barra di ricerca Spotlight (toggle). |
| **`Spazio`** | Esplora Risorse / Desktop | Apre l'anteprima QuickLook del file o cartella evidenziato. |
| **`Spazio`** | Nei Risultati di Ricerca | Apre QuickLook mantenendo Spotlight visibile sotto. |
| **`Spazio`** | Dentro QuickLook | Chiude solo l'anteprima e restituisce il focus a Spotlight o Explorer. |
| **`Esc`** | Qualsiasi finestra AeroSpot | Tasto universale: chiude simultaneamente sia Spotlight che QuickLook. |
| **`F11` / Doppio Clic** | Dentro QuickLook | Attiva/disattiva la modalità a tutto schermo. |
| **`Ctrl + Rotella`** | Nell'Anteprima PDF | Zoom fluido (da 0.4x a 4.0x) ancorato al puntatore del mouse. |
| **`F12`** | Emergenza | Sblocco immediato: disattiva gli hook di tastiera e ripristina il focus. |

*Tutte le scorciatoie possono essere personalizzate nel pannello delle Impostazioni (`⚙`).*

---

## 🎧 Formati di Anteprima Supportati

| Categoria | Estensioni Supportate | Tecnologia |
|---|---|---|
| **Documenti PDF** | `.pdf` | Motore vettoriale nativo Windows.Data.Pdf con scorrimento e zoom |
| **Fogli di Calcolo** | `.xlsx`, `.xlsm`, `.xltx` | Motore OpenXML nativo con navigazione schede multi-foglio |
| **Video** | `.mp4`, `.mkv`, `.avi`, `.mov`, `.wmv`, `.webm`, `.h264`, `.h265`, `.m4v`, `.flv`, `.3gp`, `.ts`, `.mpg` | Windows Media Engine nativo con scrubber temporale e formato >1h |
| **Audio** | `.mp3`, `.wav`, `.m4a`, `.aac`, `.flac`, `.wma`, `.ogg`, `.mid`, `.midi`, `.aiff`, `.opus` | Audio Engine nativo con visualizzatore a vinile 33 giri animato |
| **Modelli 3D** | `.stl` (binario e ASCII) | Viewport 3D nativo a 60 FPS con rotazione orbitale e zoom |
| **Immagini** | `.png`, `.jpg`, `.jpeg`, `.webp`, `.bmp`, `.gif`, `.ico`, `.svg` | Decoder raster e vettoriale ad alta definizione con pan e zoom al mouse |
| **Codice e Testo** | `.txt`, `.md`, `.py`, `.json`, `.csv`, `.ini`, `.cs`, `.xml`, `.html`, `.yaml`, `.toml`, `.sql`, `.bat`, ecc. | Visualizzatore monospazio con rilevamento codifica e conteggio righe |
| **Documenti Office** | `.docx`, `.pptx`, `.rtf` | Preview Handler COM di Windows con isolamento di processo |
| **Cartelle** | Directory | Calcolo asincrono dimensioni totali, conteggio file e cartelle |
| **Applicazioni** | `.exe`, `.lnk`, `shell:AppsFolder` (UWP/Store) | Estrazione icone Shell ad alta risoluzione e avvio applicazione |

---

## 🛠 Requisiti di Sistema

| Requisito | Specifica |
|---|---|
| **Sistema Operativo** | Windows 11 (64-bit) versione 22H2, 23H2, 24H2 o superiore (compatibile anche con Windows 10 19041+) |
| **Architettura** | x64 (Intel a 64 bit / AMD64) |
| **Runtime .NET** | **Nessuno richiesto** — Runtime .NET 10 integrato e precompilato ReadyToRun AOT |
| **Spazio su Disco** | ~75 MB download / ~195 MB scompattato |
| **Memoria RAM** | Host: ~8 MB a riposo; Worker: ~150 MB attivo |

---

## 🛡 Nota di Trasparenza su SmartScreen e Antivirus

AeroSpot è un progetto open source indipendente. Poiché gli eseguibili non sono firmati con un certificato commerciale a pagamento EV (Extended Validation), Windows Defender SmartScreen o alcuni antivirus di terze parti potrebbero mostrare un avviso al primo avvio (es. *"Autore sconosciuto"* o segnalazioni euristiche dovute all'uso dell'hook globale di tastiera).

- **Perché AeroSpot usa un hook di tastiera?** Per rilevare la pressione della barra `Spazio` in Esplora Risorse o sul Desktop senza interferire con la normale scrittura o con la rinomina dei file.
- **Verifica dell'Integrità:** Puoi verificare in qualsiasi momento l'integrità del pacchetto zip scaricato confrontando il checksum SHA-256 pubblicato nel file `SHA256SUMS.txt`.
- **Trasparenza del Codice:** L'intero codice sorgente è pubblico, consultabile e verificabile al 100% in questo repository.

---

## 🚀 Compilazione dai Sorgenti

Per compilare AeroSpot dai sorgenti utilizzando .NET 10 SDK:

```powershell
# Clona il repository
git clone https://github.com/Ritolino/AeroSpot.git
cd AeroSpot

# Ripristina dipendenze e compila in Release
dotnet restore AeroSpot.sln
dotnet build -c Release AeroSpot.sln

# Esegui la suite completa di test (100 test)
dotnet test -c Release AeroSpot.sln

# Genera il pacchetto portable self-contained
powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1
```

Per l'architettura completa di compilazione e i benchmark, consulta [docs/BUILDING.md](docs/BUILDING.md).

---

💡 Informazioni sul Progetto & Disclaimer
Questo progetto è stato ideato, progettato e collaudato da Gabriele Clemente (@Ritolino).
> **Trasparenza:** Non sono un ingegnere del software o un designer UI/UX professionista — sono un appassionato di tecnologia! Questa applicazione è stata architettata, scritta e rifinita attraverso pair programming uomo-AI utilizzando Google Antigravity potenziato da Gemini 3.8.
>
> Il mio contributo principale è stato l'ideazione del concetto iniziale, il testing iterativo, il design UI/UX, la definizione delle funzionalità e la validazione sul campo su hardware Windows (inclusa l'architettura multi-processo Host/Worker, gli hook Win32 a basso livello, il playback audio/video DirectShow & MediaElement, il rendering 3D DirectX 11 con SharpDX, l'indicizzazione SQLite full-text search e la localizzazione bilingue completa).
>
> Per questo motivo, suggerimenti, code review e Pull Request per migliorare il codice, ottimizzare le dipendenze o perfezionare l'architettura software sono calorosamente benvenuti e apprezzati!

---

## 📄 Licenza

Questo progetto è distribuito sotto licenza **MIT** — consulta il file [LICENSE](LICENSE) per tutti i dettagli.

Copyright (c) 2026 Gabriele Clemente
