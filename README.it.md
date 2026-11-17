# AeroSpot

[![Piattaforma](https://img.shields.io/badge/Piattaforma-Windows%2011%20x64-blue?logo=windows&logoColor=white)](https://github.com/Ritolino/AeroSpot)
[![Runtime](https://img.shields.io/badge/.NET-10.0%20Self--Contained%20(ReadyToRun)-purple?logo=dotnet)](https://dotnet.microsoft.com/)
[![UI](https://img.shields.io/badge/UI-WPF%20Fluent%20Dark-black)](https://github.com/Ritolino/AeroSpot)
[![Release](https://img.shields.io/github/v/release/Ritolino/AeroSpot?label=Release&color=orange)](https://github.com/Ritolino/AeroSpot/releases/latest)
[![Licenza](https://img.shields.io/badge/Licenza-MIT-brightgreen.svg)](LICENSE)

AeroSpot è un'applicazione desktop nativa per **Windows 11** (compatibile con Windows 10 x64), ultra-leggera e rispettosa della privacy, progettata per unire la velocità di ricerca di un launcher in stile **Spotlight** con l'immediatezza delle anteprime file di **QuickLook**, mantenendo il consumo di memoria residente dell'Host a riposo rigorosamente inferiore a **10 MB di Private Bytes**.

[**🇬🇧 Read the English README (README.md)**](README.md)

---

## ⬇️ Download

AeroSpot è distribuito in due edizioni costruite a partire dagli stessi binari compilati (.NET 10 ReadyToRun self-contained, senza alcun prerequisito runtime da installare):

| Edizione | Per chi è consigliata | Caratteristiche | Download |
| :--- | :--- | :--- | :--- |
| **🪟 Setup Installer** *(In arrivo)* | Utenti quotidiani | Installazione per-utente senza UAC in `{localappdata}\Programs\AeroSpot`, collegamenti Start e Desktop, aggiornamento automatico | *In arrivo* |
| **📦 Pacchetto Portable (.zip)** | Sviluppatori, chiavette USB | Nessuna installazione richiesta: estrai ed esegui `AeroSpot.exe`. Dati salvati nella cartella locale `Data\` | [**Scarica AeroSpot v1.6.0 Portable (.zip)**](https://github.com/Ritolino/AeroSpot/releases/download/v1.6.0/AeroSpot-v1.6.0-win-x64-portable.zip) |

---

## ✨ Caratteristiche Principali

- **⚡ Architettura a Due Processi a Bassissimo Consumo**:
  - **AeroSpot Host**: Processo Win32 puro residente (< 10 MB Private Bytes a riposo) che registra l'hook tastiera globale e coordina i processi.
  - **AeroSpot Worker**: Processo grafico WPF istanziato su richiesta per ricerca e anteprime; si arresta automaticamente dopo 30 secondi di inattività restituendo tutta la memoria a Windows (oppure rimane in modalità *Keep-Warm* a scelta dell'utente per una reattività a 0 ms).
  - **AeroSpot Tools**: Processo satellite effimero per inferenza neurale DirectML DirectX 12 GPU (rimozione sfondo AI), con autoshutdown garantito dopo 15 secondi.
- **🚀 Ricerca Rapida Spotlight (`Alt + Spazio`)**:
  - Scorciatoia globale richiamabile all'istante da qualsiasi schermata o applicazione.
  - **Digitazione a Latenza Zero**: Il thread UI non si blocca mai durante la digitazione; icone ed elementi grafici vengono composti asincronamente in background mentre le query SQLite FTS5 vengono eseguite in tempo reale.
  - **Filtri Rapidi per Categoria & Navigazione Tab**: Barra pillole centrata sotto la casella di ricerca (`Tutti`, `App`, `Foto`, `Documenti`, `Video`, `Audio`, `Cartelle`, `Impostazioni`). Premendo `Tab` o `Shift+Tab` si scorrono ciclicamente i filtri all'istante senza perdere il focus dalla casella di digitazione. I filtri si resettano automaticamente su `Tutti` alla chiusura.
  - **Drag & Drop con Miniatura Fantasma**: Trascina direttamente i file trovati verso il Desktop, Esplora Risorse o chat esterne (WhatsApp, Telegram, browser), con una miniatura elegante semitrasparente che segue fedelmente il cursore del mouse.
  - **Eliminazione Rapida nel Cestino (`Canc`)**: Tasto `Canc` o voce del menu contestuale per inviare i file eliminati nel Cestino di sistema di Windows con dialogo di conferma e rimozione istantanea dall'indice.
  - **Calcolatrice e Convertitore di Unità**: Calcoli al volo (`2+2`, `sqrt(144)`), scomposizione sconti e maggiorazioni percentuali con sottotitoli contestuali (es. `600 - 60%` = `240`, *"Sconto 60% (-360)"*) e conversioni istantanee di volume, massa, lunghezza e temperatura.
  - **Menu Contestuale su Ogni Risultato**: Clic destro per "Vai al percorso file" in Esplora risorse, copia percorso, anteprima, cestino e conversioni rapide (JPG, PNG, PDF per immagini e documenti `.md`/`.txt`/`.docx`, MP4, MP3, WAV, rimozione sfondo AI).
  - **Impostazioni in 3 Sezioni & Gerarchia Personalizzabile**: Schede pulite *Generali*, *QuickLook* e *Spot* con collegamento diretto da Spotlight, riordino categorie drag-and-drop e opzione per privilegiare la corrispondenza esatta del nome.
- **👁 Anteprime Immediate QuickLook (`Spazio`)**:
  - Funziona nativamente in **Esplora Risorse**, sul **Desktop**, nei risultati di Spotlight e all'interno delle **finestre di dialogo Apri / Salva / Allega (`#32770`)** di app terze (WhatsApp, Chrome, Blocco Note, Discord) tramite UI Automation senza injection di DLL.
  - **Architettura DWM Cloaking & Layering Contestuale**: Zero frame fantasma, zero flash nero, attivazione automatica in primo piano, pulsante **rotazione manuale 90° (`⟳`)** con adattamento dinamico delle proporzioni finestra (zero bande nere) e opzione *Sempre in primo piano*.
  - **Visualizzatore Word Nativo Zero-Office (`.docx`, `.docm`, `.dotx`)**: Lettore nativo OpenXML ultra-rapido (~5–15 ms) con tipografia Fluent, tabelle e zero processi zombie `WINWORD.EXE` in background.
  - **Esportazione Diretta in PDF**: Pulsante **📄 PDF** integrato nella barra strumenti per convertire ed esportare all'istante file Markdown (`.md`), testo (`.txt`) e documenti Word (`.docx`).
  - **Visualizzatore Web & HTML Nativo**: Rendering reale con layout, CSS ed immagini tramite motore browser nativo isolato e pulsante per esaminare il codice sorgente.
  - **Documenti PDF ad Alta Velocità (Google PDFium x64)**: Visualizzazione ultra-rapida sub-50ms tramite motore nativo C++ Google PDFium (`Docnet.Core`, standard QuickLook Store) su autentico foglio bianco candido con ombra elegante, **scorrimento continuo verticale** con pagine impilate, **barra laterale sinistra con miniature delle pagine** per navigazione rapida e salto istantaneo al clic con evidenziazione dinamica, caricamento asincrono con cache LRU (max 16 pagine in RAM), pan a manina con trascinamento tasto sinistro (`Cursors.SizeAll`), zoom millimetrico continuo `Ctrl + Rotellina` a **60 FPS ancorato al puntatore con tasto reset 1:1**, capsula flottante di pagina posizionata in basso a sinistra (`▲ 1 / 40 ▼`), navigazione da tastiera `PageUp`/`PageDown`/`Home`/`End` e zero processi browser esterni avviati.
  - **Fogli di Calcolo Excel**: Lettore nativo OpenXML (`.xlsx`, `.xlsm`, `.xltx`) a zero dipendenze; apre e visualizza fogli multipli con schede tab senza richiedere Microsoft Office installato.
  - **Riproduzione Video Fluida con Remux Trasparente**: Supporto universale MP4, MKV, AVI, MOV, WMV, WEBM con timeline ad alta precisione, seek istantaneo senza rimbalzi, rotazione automatica per video verticali da smartphone e pipeline di remux automatico trasparente FFmpeg a 60x (audio AAC stereo, video intatto) per tracce bitstream multicanale (AC-3, DTS, TrueHD) a parità VLC.
  - **Giradischi Audio Vintage a 33 Giri**: Piatto rotante animato con foro mandrino centrale ed etichetta retrò, braccio cinematico che segue i solchi e rientra a lato su pausa/stop congelando l'angolo di rotazione.
  - **Modelli 3D STL e 3MF**: Motore 3D nativo a 60 FPS con rotazione orbitale mouse drag e zoom con rotella.
  - **Pagine Web & HTML**: Visualizzazione rapida passiva scura per `.html` e `.htm`.
  - **Testo e Codice Sorgente**: Oltre 20 estensioni supportate con font monospaziato, conteggio righe e limite di sicurezza a 100 KB.
  - **Menu Contestuale QuickLook**: Clic destro su qualsiasi anteprima per accedere a conversioni, azioni file e gestione finestra (schermo intero F11, fissa dimensioni 📌).
- **🛠 Conversioni Rapide, Menu Contestuale Explorer e Strumenti Batch**:
  - **Menu a Cascata Esplora Risorse Multi-File (`Shift + Tasto destro`)**: Unisci più PDF selezionati in un unico documento (`PDFsharp`), combina più foto in un PDF multi-pagina o avvia conversioni intelligenti in blocco (JPG/PNG) con pillola di progresso multi-monitor.
  - **Converti in JPG / PNG**: JPEG qualità 95 con compositing pixel-level su sfondo bianco per trasparenze PNG/BMP/WebP (zero ritagli High-DPI).
  - **Converti in PDF**: Documento PDF 1.4 ISO singolo o multi-pagina generato in C# nativo.
  - **Converti in MP4 / Estrai MP3 e WAV**: Converte video non MP4 (MKV, AVI, MOV, WMV, WEBM) in MP4 compatibile tramite FFmpeg o estrae tracce audio con Windows Media Foundation.
  - **Rimuovi sfondo (AI)**: Segmentazione neurale su GPU DirectX 12 via DirectML. Il modello compatto (~44 MB) viene scaricato in streaming solo al primo clic e salvato localmente.
  - **Apertura Automatica Cartella**: Al termine della conversione, Esplora risorse si apre automaticamente evidenziando il nuovo file generato.
- **🔄 Aggiornamenti Automatici & Gestione Release**:
  - Controllo automatico o manuale di nuove versioni ufficiali su GitHub Releases API via HTTPS.
  - Frequenze configurabili: *Manuale*, *Ad ogni avvio*, *Giornaliero*, *Settimanale*, *Mensile*.
  - Verifica crittografica preventiva SHA-256 contro `SHA256SUMS.txt` prima dell'esecuzione.
- **🌐 Interfaccia Bilingue (Italiano & English)**:
  - Rilevamento automatico della lingua di Windows al primo avvio e cambio istantaneo senza riavviare l'applicazione.

---

## ⌨️ Scorciatoie da Tastiera Principali

| Scorciatoia | Contesto | Azione |
|---|---|---|
| **`Alt + Spazio`** | Ovunque | Apre o chiude la barra di ricerca Spotlight (toggle). |
| **`Spazio`** | Esplora Risorse / Desktop | Apre l'anteprima QuickLook del file o cartella selezionato. |
| **`Spazio`** | Nei Risultati di Ricerca | Apre QuickLook mantenendo visibile Spotlight sotto. |
| **`Spazio`** | Dentro QuickLook | Chiude solo l'anteprima e restituisce il focus a Spotlight o Explorer. |
| **`Esc`** | Qualsiasi finestra AeroSpot | Tasto universale: chiude simultaneamente sia Spotlight che QuickLook. |
| **`F11` / Doppio Clic** | Dentro QuickLook | Attiva o disattiva la modalità a tutto schermo. |
| **`Ctrl + Rotella`** | Nell'Anteprima PDF | Zoom avanti / indietro ancorato al puntatore del mouse (tasto reset 1:1). |
| **`Click + Trascina`** | Nell'Anteprima PDF | Sposta e naviga liberamente il foglio ingrandito (pan a manina). |
| **`Clic su Miniatura`** | Nell'Anteprima PDF | Salta istantaneamente alla pagina selezionata nella barra laterale sinistra. |
| **`F12`** | Emergenza | Disattiva immediatamente gli hook di tastiera e ripristina il focus. |

*Per la lista completa delle combinazioni e la guida dettagliata, consulta le [Istruzioni d'uso](Docs/ISTRUZIONI.md).*

---

## 📁 Formati Supportati (Sintesi)

AeroSpot supporta l'anteprima istantanea per decine di formati raggruppati nelle seguenti categorie:
- **Documenti:** PDF vettoriali multi-pagina (`.pdf`), fogli Excel OpenXML (`.xlsx`, `.xlsm`, `.xltx`), documenti Office (`.docx`, `.pptx`, `.rtf`).
- **Media:** Video (`.mp4`, `.mkv`, `.avi`, `.mov`, `.wmv`, `.webm`, ecc.), Audio (`.mp3`, `.wav`, `.flac`, `.m4a`, `.aac`, `.ogg`, `.wma`), Immagini raster e vettoriali (`.png`, `.jpg`, `.jpeg`, `.webp`, `.bmp`, `.gif`, `.ico`, `.svg`), Foto RAW (`.arw`, `.dng`, `.cr2`, `.cr3`, `.nef`, `.orf`, `.rw2`, `.pef`, `.raf` con estrazione rapida dell'anteprima JPEG incorporata), Grafica vettoriale Adobe Illustrator (`.ai`, scheda tecnica sicura con metadati).
- **3D & Web:** Modelli stereolitografia (`.stl`, `.3mf`), pagine web (`.html`, `.htm`).
- **Codice & Testo:** Sorgenti e configurazioni (`.txt`, `.md`, `.py`, `.json`, `.csv`, `.ini`, `.cs`, `.xml`, `.yaml`, `.sql`, `.bat`, `.cpp`, `.ts`, `.js`, ecc.).
- **Sistema:** Cartelle (con calcolo asincrono dimensioni e conteggio elementi), collegamenti (`.lnk`) e applicazioni di sistema/Store.

*Per la tabella dettagliata e canonica di ciascun formato, consulta la sezione dedicata nelle [Istruzioni d'uso (Docs/ISTRUZIONI.md)](Docs/ISTRUZIONI.md).*

---

## 🔒 Privacy, Rete e "Cosa Tocca AeroSpot" (What AeroSpot Touches)

### Privacy e Connessioni di Rete
- **Funzionamento 100% locale:** Tutte le ricerche, l'indicizzazione e le anteprime avvengono rigorosamente sul tuo dispositivo. Nessun socket aperto e nessuna telemetria.
- **Cronologia e Ricerche protette:** Tutti i percorsi aperti e i testi cercati risiedono unicamente nel database locale SQLite (`index.db`) sul tuo computer. Puoi configurare liberamente la schermata iniziale dello Spotlight tra 4 modalità (inclusa la modalità *Non mostrare nulla* che disattiva ogni registrazione) o cancellare definitivamente tutti i dati registrati in qualsiasi momento con il pulsante "Cancella cronologia". Nessun dato di ricerca lascia mai la macchina.
- **Rete (solo opzionale e trasparente):**
  1. *Controllo aggiornamenti:* Se abilitato nelle Impostazioni, interroga l'endpoint pubblico HTTPS `https://api.github.com/repos/Ritolino/AeroSpot/releases/latest`. Nessun dato utente viene trasmesso.
  2. *Download modello AI:* Se l'utente clicca su "Rimuovi sfondo (AI)", scarica on-demand il modello compatto da repository sicuro salvandolo in locale.

### File System e Registro di Windows
- **Edizione Installata:**
  - Binari programma: `{localappdata}\Programs\AeroSpot\`
  - Database indice e preferenze: `%LOCALAPPDATA%\AeroSpot\Data\`
  - Log diagnostici (ritenzione automatica 24 ore): `%LOCALAPPDATA%\AeroSpot\logs\`
  - Registro Windows: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (avvio con Windows) e opzionalmente `HKCU\Software\Classes\SystemFileAssociations` (menu contestuale Shell).
- **Edizione Portable:**
  - Binari e dati: interamente contenuti nella cartella scelta dall'utente (con cartella `Data\` locale creata via marcatore `portable.txt`).
  - Nessun residuo su `%LOCALAPPDATA%` salvo modelli AI o log se `portable.txt` non è scrivibile.
- **Pulizia Completa:**
  L'uninstaller dell'edizione installata o il programma `Uninstall.exe` dell'edizione portable rimuovono completamente ogni file e ogni chiave di registro, lasciando il sistema pulito.

---

## 🛡 Nota di Trasparenza su SmartScreen e Antivirus

AeroSpot è un progetto software open source indipendente. Poiché i file eseguibili non sono firmati con un certificato commerciale a pagamento EV (Extended Validation), Windows SmartScreen o alcuni antivirus potrebbero visualizzare un avviso generico al primo avvio (es. *"Autore sconosciuto"* o segnalazioni euristiche dovute all'uso dell'hook globale di tastiera `WH_KEYBOARD_LL`).

- **Perché serve l'hook di tastiera?** Per consentire la pressione del tasto `Spazio` in Esplora risorse o sul Desktop senza interferire con la normale digitazione o con la rinomina dei file.
- **Verifica Integrità Checksum:** Puoi verificare in qualsiasi momento l'integrità del download confrontando l'impronta crittografica SHA-256 contenuta nel file ufficiale `SHA256SUMS.txt` pubblicato su GitHub Releases.
- **Sorgenti Verificabili:** Il codice sorgente completo è pubblico e verificabile in questo repository.

---

## 🛠 Requisiti di Sistema e Prestazioni

| Requisito | Specifica |
|---|---|
| **Sistema Operativo** | Windows 11 (64-bit) versione 22H2 o superiore (compatibile con Windows 10 x64 build 19041+) |
| **Architettura** | x64 (Intel / AMD a 64 bit) |
| **Runtime .NET** | **Nessuno richiesto** (Runtime .NET 10 integrato ReadyToRun) |
| **Consumo RAM (Misurato)** | Host a riposo: **~7–23 MB Private Bytes**; Worker a riposo (post-bonifica): **3.8 – 9.5 MB Working Set**; Worker attivo/picco: **180–257 MB** (PDF Nativo PDFium: Worker ~158 MB Priv, **0 MB** sottoprocessi esterni); Post-timeout: **0 MB** |
| **Utilizzo CPU (Misurato)** | A riposo: **0.00% – 0.05%**; Picco (Remux audio 60x tempo reale): **0.4% – 1.6%** |
| **Tempo di Reazione** | Ripresa a caldo Worker: **< 1 ms**; Latenza query indice SQLite: **2–4 ms**; Reset Spotlight: **0 ms**; Estrazione geometria PDF WinRT: **5–50 ms**; Debounce UI: **50 ms** |

*Per il report tecnico completo di misurazione e configurazione hardware di test, consulta [STATUS.md](STATUS.md).*

---

## 🚀 Compilazione dai Sorgenti

Per compilare AeroSpot dai sorgenti utilizzando .NET 10 SDK:

```powershell
# Clona il repository
git clone https://github.com/Ritolino/AeroSpot.git
cd AeroSpot

# Ripristina pacchetti e compila in Release
dotnet restore AeroSpot.sln
dotnet build -c Release AeroSpot.sln

# Esegui la suite completa di test
dotnet test -c Release AeroSpot.sln

# Genera i pacchetti ufficiali di rilascio (Portable zip ed eventuale Setup)
powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1
```

Per maggiori dettagli su opzioni di build e benchmark ReadyToRun, consulta la guida [Docs/BUILDING.it.md](Docs/BUILDING.it.md).

---

## 💡 Informazioni sul Progetto

AeroSpot è ideato, progettato e collaudato da Gabriele Clemente ([@Ritolino](https://github.com/Ritolino)).

> **Nota di sviluppo:** Questa applicazione è stata sviluppata in modalità pair programming assistita da modelli avanzati di intelligenza artificiale. L'ideazione concettuale, il testing sul campo, l'architettura a due processi, la gestione degli hook Win32 a basso livello, le rifiniture grafiche e la validazione funzionale sono il risultato di iterazioni dirette su hardware Windows.
> Suggerimenti, segnalazioni e contributi sono sempre i benvenuti!

---

## 📄 Licenza

Questo progetto è distribuito sotto licenza **MIT** — consulta il file [LICENSE](LICENSE) per tutti i dettagli.

Copyright (c) 2026 Gabriele Clemente
