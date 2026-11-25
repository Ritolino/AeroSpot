<p align="center">
  <img src="assets/app_icon.png" width="128" height="128" alt="AeroSpot Icon">
</p>

# AeroSpot

[![Piattaforma](https://img.shields.io/badge/Piattaforma-Windows%2011%20x64-blue?logo=windows&logoColor=white)](https://github.com/Ritolino/AeroSpot)
[![Runtime](https://img.shields.io/badge/.NET-10.0%20ReadyToRun-purple?logo=dotnet)](https://dotnet.microsoft.com/)
[![Memoria](https://img.shields.io/badge/Private%20Bytes-%3C%2010%20MB-brightgreen)](#requisiti-di-sistema-e-prestazioni-verificate)
[![CPU Idle](https://img.shields.io/badge/CPU%20Idle-0.0%25-brightgreen)](#requisiti-di-sistema-e-prestazioni-verificate)
[![Release](https://img.shields.io/github/v/release/Ritolino/AeroSpot?label=Release&color=orange)](https://github.com/Ritolino/AeroSpot/releases/latest)
[![Licenza](https://img.shields.io/badge/Licenza-MIT-brightgreen.svg)](LICENSE)

Una suite di produttività desktop per **Windows 11 / Windows 10 x64**, **ultra-leggera, a zero sprechi e rispettosa della privacy**, che unisce la rapidità di un launcher in stile **Spotlight (`Alt + Spazio`)** alle anteprime immediate **QuickLook (`Spazio`)**. Progettata come alternativa nativa e compatta ai launcher tradizionali e ai visualizzatori pesanti multi-processo, mantenendo il consumo residente dell'Host a riposo rigorosamente sotto i **10 MB di Private Bytes**.

[**🇬🇧 Read the English README (README.md)**](README.md)

---

## ⬇️ Download

AeroSpot è distribuito come pacchetto Self-Contained ReadyToRun `.NET 10` senza alcun runtime esterno da installare:

| Edizione | Per chi è consigliata | Caratteristiche Principali | Download |
| :--- | :--- | :--- | :--- |
| **🪟 Setup Unificato (.exe)** *(In arrivo)* | Utenti quotidiani e Portable | Wizard guidato con scelta tra Installazione Standard (`{localappdata}\Programs\AeroSpot`) e Modalità Portable, scelta libera della cartella, zero UAC, aggiornamenti silenziosi 1-clic | *In arrivo* |
| **📦 Pacchetto Portable (.zip)** | Versione attuale v1.6.2 | Estrai ed esegui `AeroSpot.exe`. Dati salvati esclusivamente nella sottocartella locale `Data\` | [**Scarica AeroSpot v1.6.2 Portable (.zip)**](https://github.com/Ritolino/AeroSpot/releases/download/v1.6.2/AeroSpot-v1.6.2-win-x64-portable.zip) |

---

## ✨ Caratteristiche Principali

- **🔒 100% Privata, Zero Telemetria e Zero Privilegi Admin (No UAC):** Tutte le ricerche, l'indicizzazione SQLite e le anteprime file avvengono rigorosamente offline in locale senza socket in ascolto né telemetria. Tutte le scritture restano confinate al profilo utente (`HKCU`, `%LOCALAPPDATA%\AeroSpot` o cartella locale `Data\` in modalità portable). Connessioni di rete solo su richiesta esplicita (controllo update GitHub Releases o download modello AI al primo utilizzo).
- **⚡ Ultra-Leggera (< 10 MB Private Bytes) e Zero CPU Idle (0.0%):**
  - **AeroSpot Host:** Coordinatore Win32 puro residente (~7–8 MB Private Bytes a riposo, 0.00%–0.05% CPU, zero dipendenze WPF/WinForms) per hook tastiera globali e tray icon.
  - **AeroSpot Worker:** Motore grafico WPF avviato su richiesta; si chiude dopo 30s di inattività (o rilascia la RAM a ~3.8–9.5 MB Working Set con `EmptyWorkingSet` in modalità *Keep-Warm*).
  - **AeroSpot Tools:** Processo satellite effimero per inferenza AI DirectML DirectX 12 GPU e strumenti batch Explorer (0 MB a riposo).
- **🚀 Ricerca Istantanea Spotlight (`Alt + Spazio`):**
  - Scorciatoia globale richiamabile all'istante da qualsiasi app o finestra a schermo intero.
  - **Digitazione a Zero Lag & Ricerca SQLite a Due Fasi:** Caricamento asincrono icone e query SQLite a due fasi (`Title > FullPath` con gerarchia categorie drag-and-drop personalizzabile) per digitazione fluida senza blocchi.
  - **Filtri Rapidi per Categoria & Tab Cycling:** Pillole centrate (`Tutti`, `App`, `Foto`, `Documenti`, `Video`, `Audio`, `Cartelle`, `Impostazioni`) navigabili con `Tab` / `Shift+Tab` senza perdere il focus dal campo di ricerca.
  - **Drag & Drop con Miniatura Fantasma:** Trascina i risultati verso Desktop, Esplora Risorse o chat esterne (WhatsApp, Telegram, browser) con modalità configurabile (*Segui Windows*, *Copia sempre*, *Sposta sempre*).
  - **Eliminazione Rapida (`Canc`), Calcolatrice & Convertitore:** Conferma invio nel Cestino con aggiornamento differenziale dell'indice, calcolatrice commerciale (`600 - 60% = 240`, *"Sconto 60% (-360)"*) e conversioni istantanee.
- **👁 Anteprime Immediate QuickLook (`Spazio`):**
  - Funziona nativamente in **Esplora Risorse** (con risoluzione della scheda attiva su Windows 11 multi-tab), sul **Desktop**, nei risultati di Spotlight e nei **dialoghi Apri / Salva / Allega (`#32770`)** di app terze tramite UI Automation senza DLL injection.
  - **DWM Cloaking Anti-Flicker & Navigazione Frecce:** Zero flash nero o frame fantasma, frecce direzionali sincronizzate con l'ordine di Explorer, **rotazione manuale 90° (`⟳`)** con adattamento finestra, scorciatoia universale **`R`** per reset vista/zoom e opzione *Sempre in primo piano*.
  - **Motore PDF Nativo Google PDFium (`Docnet.Core`):** Apertura sub-50ms con scorrimento verticale continuo, barra laterale miniature, zoom a 60 FPS ancorato al puntatore, pan a manina solo su documento zoommato e **0 MB** di processi browser esterni (niente `msedgewebview2.exe`).
  - **Lettori Documenti & Fogli di Calcolo Zero-Office:** Lettori nativi ~5–15 ms per `.docx`/`.docm`/`.dotx`/`.odt`, `.rtf`, `.xlsx`/`.xlsm`/`.xltx` e `.html`/`.htm` con esportazione PDF 1-clic e zero processi zombie `WINWORD.EXE`.
  - **Player Video Fluido, Giradischi Audio 33 Giri & Viewer 3D:** Riproduzione video universale con remux trasparente FFmpeg a 60x in AAC stereo per tracce AC-3/DTS/TrueHD, giradischi animato per audio, viewer 3D `.stl`/`.3mf` a 60 FPS, estrazione JPEG da foto RAW e SVG vettoriali.
- **🛠 Strumenti Batch Explorer (`Shift + Tasto destro`) & AI Locale:** Unione più PDF (`PDFsharp`), immagini in PDF multi-pagina, conversioni in blocco JPG/PNG/MP4/MP3/WAV e rimozione sfondo AI offline su GPU DirectX 12 (`RMBG-1.4` / fallback `u2netp`).
- **🔄 Auto-Updater 1-Clic & Interfaccia Bilingue (IT / EN):** Verifica HTTPS su GitHub Releases con controllo crittografico SHA-256 contro `SHA256SUMS.txt` e cambio lingua istantaneo senza riavvio.

---

## 📊 AeroSpot a Confronto con le Alternative

| Funzionalità / Metrica | 🪟 **AeroSpot** *(Suite Unificata)* | **Everything + QuickLook** | **Flow Launcher + QuickLook** | **PowerToys Run + QuickLook** |
| :--- | :--- | :--- | :--- | :--- |
| **RAM Residente a Riposo** | **~7–8 MB Private Bytes** | ~35 – 65 MB *(combinati)* | ~80 – 170 MB *(combinati)* | ~130 – 250+ MB *(combinati)* |
| **Overhead Relativo Memoria** | **Baseline (0%)** | **+350% a +700%** *(4× a 8×)* | **+900% a +2.000%** *(10× a 21×)* | **+1.500% a +3.000%+** *(16× a 30×+)* |
| **Utilizzo CPU a Riposo** | **0.00% – 0.05%** | ~0.0% – 0.2% | ~0.1% – 0.5% | ~0.2% – 1.0% |
| **Sottoprocessi PDF / Word / HTML** | **0 (Nativi In-Process)** | WebView / Avvio Office COM | QuickLook separato + COM | QuickLook separato + COM |
| **Launcher + Anteprima + Tool** | **Singola app leggera** | Richiede 2+ app separate | Richiede 2+ app separate | Richiede 2+ app separate |
| **Privilegi Admin Richiesti** | **No** *(Utente Standard)* | Spesso Sì (Servizio USN) | No | Opzionale |

---

## ⌨️ Scorciatoie da Tastiera Principali

| Scorciatoia | Contesto | Azione |
|---|---|---|
| **`Alt + Spazio`** | Ovunque | Apre o chiude la barra di ricerca Spotlight (toggle). |
| **`Spazio`** | Esplora Risorse / Desktop / Dialoghi | Apre l'anteprima QuickLook del file o cartella selezionato. |
| **`Spazio`** | Nei Risultati di Ricerca | Apre QuickLook mantenendo visibile Spotlight sotto. |
| **`Spazio`** | Dentro QuickLook | Chiude solo l'anteprima e restituisce il focus a Spotlight o Explorer. |
| **`Esc`** | Qualsiasi finestra AeroSpot | Tasto universale: chiude simultaneamente sia Spotlight che QuickLook. |
| **`Tab` / `Shift + Tab`** | Dentro Spotlight | Scorre ciclicamente le pillole filtro senza perdere il focus dalla casella di digitazione. |
| **`Canc`** | Dentro Spotlight | Invia l'elemento selezionato nel Cestino di Windows previa conferma. |
| **`Sinistra` / `Destra` / `Su` / `Giù`** | QuickLook / Spotlight | Naviga tra i file di Explorer/Desktop (nell'esatto ordine corrente) o tra i risultati aggiornando QuickLook all'istante. |
| **`R`** | Dentro QuickLook | Reset universale: ripristina vista camera o zoom 1:1 su modelli 3D, foto, PDF e PreviewHandler. |
| **`F11` / Doppio Clic** | Dentro QuickLook | Attiva o disattiva la modalità a tutto schermo. |
| **`PageUp` / `PageDown` / `Home` / `End`** | Nell'Anteprima PDF | Scorre le pagine o salta all'inizio/fine del documento. |
| **`Ctrl + Rotella`** | Nell'Anteprima PDF / Immagine | Zoom avanti / indietro ancorato al puntatore del mouse (tasto reset 1:1). |
| **`Click + Trascina`** | Nell'Anteprima PDF / Immagine | Sposta e naviga liberamente la vista ingrandita (solo quando è zoommata). |
| **`Clic su Miniatura`** | Nell'Anteprima PDF | Salta istantaneamente alla pagina selezionata nella barra laterale sinistra. |

*Per la lista completa delle combinazioni e la guida dettagliata, consulta le [Istruzioni d'uso (Docs/ISTRUZIONI.md)](Docs/ISTRUZIONI.md).*

---

## 📁 Formati Supportati (Sintesi)

- **Documenti:** PDF vettoriali multi-pagina (`.pdf`), fogli Excel OpenXML (`.xlsx`, `.xlsm`, `.xltx`), documenti nativi zero-Office (`.docx`, `.docm`, `.dotx`, `.rtf`, `.odt`; i formati legacy `.doc`, `.dot`, `.xls`, `.ppt`, `.pptx` mostrano una scheda metadati sicura senza avviare Office COM).
- **Media:** Video (`.mp4`, `.mkv`, `.avi`, `.mov`, `.wmv`, `.webm`, ecc.), Audio (`.mp3`, `.wav`, `.flac`, `.m4a`, `.aac`, `.ogg`, `.wma`), Immagini raster e vettoriali (`.png`, `.jpg`, `.jpeg`, `.webp`, `.bmp`, `.gif`, `.ico`, `.svg`), Foto RAW (`.arw`, `.dng`, `.cr2`, `.cr3`, `.nef`, `.orf`, `.rw2`, `.pef`, `.raf`), Adobe Illustrator (`.ai`, scheda tecnica sicura).
- **3D & Web:** Modelli stereolitografia e 3D Manufacturing (`.stl`, `.3mf`), pagine web (`.html`, `.htm`, `.xhtml`).
- **Codice & Testo:** Sorgenti e configurazioni (`.txt`, `.md`, `.py`, `.json`, `.csv`, `.ini`, `.cs`, `.xml`, `.yaml`, `.sql`, `.bat`, `.cpp`, `.ts`, `.js`, ecc.).
- **Sistema:** Cartelle (calcolo asincrono dimensioni ed elementi), eseguibili (`.exe`), collegamenti `.lnk` (in QuickLook) e app Windows Store/UWP.

*Per la tabella dettagliata e canonica di ciascun formato, consulta le [Istruzioni d'uso (Docs/ISTRUZIONI.md)](Docs/ISTRUZIONI.md).*

---

## ❓ Domande Frequenti (FAQ)

- **Cosa differenzia AeroSpot da PowerToys Run, Everything o QuickLook standalone?**  
  AeroSpot riunisce launcher Spotlight, anteprima QuickLook multi-formato e strumenti batch/AI di Esplora Risorse in un'unica suite costruita attorno a un Host Win32 puro (< 10 MB di RAM) con UI WPF su richiesta, evitando runtime pesanti sempre attivi e sottoprocessi esterni di browser o Office.
- **Come riesce AeroSpot a mantenere la RAM a riposo sotto i 10 MB?**  
  I framework grafici pesanti (`WPF`), Google PDFium e i modelli AI DirectML non vengono mai caricati nel processo residente `AeroSpot.Host.exe`. Girano solo in `AeroSpot.Worker.exe` e `AeroSpot.Tools.exe`, che rilasciano la memoria con `EmptyWorkingSet` o terminano automaticamente dopo l'inattività.
- **Perché AeroSpot non avvia `msedgewebview2.exe` né processi fantasma `WINWORD.EXE`?**  
  I PDF sono renderizzati in-process tramite Google PDFium (`Docnet.Core`), i file `.html` usano `FlowDocument` accelerato GPU, mentre `.docx`/`.rtf`/`.odt`/`.xlsx` usano parser C# nativi in memoria con i COM PreviewHandler di Office bloccati in modo permanente per CLSID.
- **AeroSpot richiede privilegi di amministratore o modifica file di sistema?**  
  No. Funziona interamente con permessi utente standard senza popup UAC. Tutte le integrazioni opzionali (avvio automatico e menu contestuale Explorer) restano dentro `HKCU` e i dati utente vivono in `%LOCALAPPDATA%\AeroSpot` (o nella cartella locale `Data\` in modalità portable).

---

## 🔒 Privacy, SmartScreen e "Cosa Tocca AeroSpot"

- **Edizione Installata:** Binari in `{localappdata}\Programs\AeroSpot\` (o cartella scelta dall'utente); indice SQLite, preferenze, modelli AI e log con rotazione a 24 ore in `%LOCALAPPDATA%\AeroSpot\`; chiavi di Registro opzionali solo in `HKCU` per avvio automatico e menu contestuale.
- **Edizione Portable:** Tutti i binari e i dati (`Data\`) restano esclusivamente nella cartella scelta dall'utente (`portable.txt`).
- **Trasparenza SmartScreen e Antivirus:** Poiché AeroSpot è un progetto open source indipendente privo di un certificato commerciale a pagamento EV, SmartScreen o gli antivirus euristici possono mostrare un avviso generico al primo avvio (legato all'hook `WH_KEYBOARD_LL` indispensabile per le anteprime con `Spazio`). Ogni rilascio include il checksum ufficiale `SHA256SUMS.txt` e codice sorgente 100% ispezionabile.

---

## 🛠 Requisiti di Sistema e Prestazioni Verificate

| Requisito | Specifica |
|---|---|
| **Sistema Operativo** | Windows 11 (64-bit) 22H2+ (compatibile con Windows 10 x64 build 19041+) |
| **Architettura** | x64 (Intel / AMD a 64 bit) |
| **Runtime .NET** | **Nessuno richiesto** (Runtime .NET 10 integrato ReadyToRun) |
| **Consumo RAM (Misurato)** | Host a riposo: **~7–8 MB Private Bytes** (~28–35 MB WS); Worker a riposo (post-bonifica): **3.8 – 9.5 MB Working Set**; Worker attivo: **150–255 MB**; Tools a riposo: **0 MB** |
| **Utilizzo CPU (Misurato)** | A riposo: **0.00% – 0.05%**; Picco (Remux audio 60x tempo reale): **0.4% – 1.6%** |
| **Tempo di Reazione** | Ripresa a caldo Worker: **< 1 ms**; Query SQLite: **2–4 ms**; Reset Spotlight: **0 ms**; Rendering Google PDFium: **~12–15 ms/pagina** (apertura **< 50 ms**) |

---

## 🚀 Compilazione dai Sorgenti

```powershell
git clone https://github.com/Ritolino/AeroSpot.git
cd AeroSpot
dotnet restore AeroSpot.sln
dotnet build -c Release AeroSpot.sln
dotnet test -c Release AeroSpot.sln
powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1
```

Per ulteriori dettagli su opzioni di build e benchmark ReadyToRun, consulta [Docs/BUILDING.it.md](Docs/BUILDING.it.md).

---

## 💡 Informazioni sul Progetto & Disclaimer

Questo progetto è stato ideato, progettato e collaudato da [@Ritolino](https://github.com/Ritolino).

> **Trasparenza:** Non sono un ingegnere del software o un designer UI/UX professionista — sono un appassionato di tecnologia! Questa applicazione è stata architettata, scritta e rifinita attraverso pair programming uomo-AI utilizzando Google Antigravity potenziato da Gemini.
>
> Il mio contributo principale è stato l'ideazione del concetto iniziale, il testing iterativo, il design UI/UX, la definizione delle funzionalità e la validazione sul campo su hardware Windows (inclusa l'architettura a tre processi Win32/WPF/Tools, gli hook a basso livello, i visualizzatori nativi PDFium/OpenXML/RTF e la localizzazione bilingue completa).
>
> Per questo motivo, suggerimenti, code review e Pull Request per migliorare il codice, ottimizzare la memoria o perfezionare l'architettura software sono calorosamente benvenuti e apprezzati!

---

## 📄 Licenza

Questo progetto è distribuito sotto licenza **MIT** — consulta il file [LICENSE](LICENSE) per tutti i dettagli.

Copyright (c) 2026 Ritolino
