# Guida all'uso di AeroSpot

Benvenuto in **AeroSpot**, l'applicazione moderna per Windows 11 (e Windows 10 x64) progettata per unire la rapidità di ricerca di un launcher in stile *Spotlight* con l'immediatezza delle anteprime di *QuickLook*, mantenendo il consumo di memoria residente dell'Host a riposo rigorosamente inferiore a 10 MB di Private Bytes.

---

## 1. Avvio Rapido ed Edizioni

AeroSpot è disponibile in due edizioni costruite a partire dagli stessi binari compilati:

| Edizione | Per chi è pensata | Cartella Programma | Cartella Dati e Database |
|---|---|---|---|
| **Installer (`Setup.exe`)** | Utenti comuni, uso quotidiano | `{localappdata}\Programs\AeroSpot\` | `%LOCALAPPDATA%\AeroSpot\Data\` |
| **Portable (`.zip`)** | Sviluppatori, chiavette USB, uso nomade | Cartella estratta scelta dall'utente | Cartella locale `Data\` accanto al programma |

### Avvio dell'Applicazione
- **Edizione con Installer:** Avvia AeroSpot dal menu Start o dal collegamento sul Desktop.
- **Edizione Portable:** Estrai l'archivio ZIP ed esegui il launcher nativo `AeroSpot.exe`.
- L'applicazione si avvia silenziosamente in background e posiziona l'icona Fluent scura nell'Area di Notifica (Tray accanto all'orologio di Windows).

### Chiusura e Disinstallazione
- **Chiusura completa:** Clic destro sull'icona di AeroSpot nella Tray -> **Esci da AeroSpot**. Tutti i processi (Host, Worker, Tools) vengono terminati in modo pulito e immediato.
- **Disinstallazione Edizione Installata:** Apri *Impostazioni di Windows* -> *App* -> *App installate* -> cerca **AeroSpot** -> *Disinstalla*. Il programma di disinstallazione ti chiederà se desideri conservare o rimuovere l'indice di ricerca e le impostazioni.
- **Disinstallazione Edizione Portable:** Esegui `Uninstall.exe` presente nella radice della cartella estratta. Il programma chiude i processi attivi, rimuove l'avvio automatico e le voci contestuali dal registro di Windows, pulisce la cache e cancella la cartella dell'applicazione. In alternativa, puoi eseguire dal terminale:
  ```cmd
  AeroSpot.Host.exe --cleanup
  ```

---

## 2. Come si usa il Launcher (Ricerca rapida)

### Scorciatoia da Tastiera
- **Scorciatoia predefinita:** `Alt + Spazio` (personalizzabile nelle Impostazioni con `Ctrl + Spazio`, `Win + Spazio` o una combinazione personalizzata).
- Puoi richiamare AeroSpot in qualsiasi momento da qualsiasi applicazione o schermata.

### Come Cercare
1. Premi la combinazione di tasti: apparirà la barra di ricerca fluida con effetto acrilico scuro centrata nella parte superiore dello schermo.
2. Digita il nome di ciò che cerchi:
   - **Applicazioni installate e di sistema** (es. *Chrome*, *Blocco note*, *Calcolatrice*, *Terminale*, *VS Code*, *Steam*, app UWP/Store come *WhatsApp*)
   - **Documenti e file** (es. *relazione.docx*, *tabella.xlsx*, *progetto.py*, *foto.png*, *video.mp4*)
   - **Cartelle** (es. *Download*, *Documenti*, *Immagini*)
3. **Calcolatrice integrata con percentuali:** Digita direttamente un'operazione matematica (es. `2+2`, `15 * 4`, `sqrt(144)`, `sin(pi/2)`) o formule con percentuali in stile Windows/Excel:
   - `1190-22%` = `928,2`
   - `1190+22%` = `1451,8`
   - `1190*22%` = `261,8`
   - `1190/22%` = `5409,09...`
   - `22%` = `0,22`
   - Percentuali concatenate: `200+10%+10%` = `242`
   - Con parentesi: `(100+20)-10%` = `108`
4. **Convertitore di unità:** Converti al volo misure di lunghezza, massa/peso (grammi, ettogrammi, chilogrammi, libbre, once), volume (litri, millilitri, centilitri, galloni) e temperature (°C, °F, K).
5. **Elementi aperti di recente (Home):** Quando la barra di ricerca è vuota, AeroSpot mostra gli elementi aperti di recente tramite l'applicazione ordinati per recenza decrescente con badge ambra "Recente".
6. **Ricerca nelle Impostazioni di Windows e AeroSpot:** Cerca direttamente le pagine di configurazione del sistema operativo (es. *"tastiera"*, *"keyboard"*, *"wifi"*, *"schermo"*, *"display"*, *"suono"*, *"sound"*, *"aggiornamenti"*, *"bluetooth"*, *"stampanti"*, *"batteria"*, *"mouse"*, *"privacy"*, *"notifiche"*, *"sfondo"*, *"spazio"*, *"accessibilità"*) e le schede di AeroSpot (*"scorciatoie"*, *"indicizzazione"*, *"aggiorna aerospot"*, *"istruzioni"*). Un filtro dedicato *"Impostazioni"* nella barra dei filtri permette di isolare i risultati di configurazione.
7. **Digitazione fluida a zero lag:** Un debouncer asincrono a 50 ms disaccoppia la digitazione dalle query del database SQLite, consentendo una digitazione immediata e fluida a 60/144 fps.
8. **Lo spazio funziona normalmente:** se cerchi parole composte (es. *"Google Chrome"* o *"Note spesa"*), premendo la barra spaziatrice inserisci normalmente uno spazio senza attivare l'anteprima.

### Tasti Rapidi nella Finestra di Ricerca
| Tasto | Azione |
|---|---|
| **Alt + Spazio** | Apre o chiude la barra di ricerca Spotlight (toggle). Se QuickLook è aperta, chiude solo Spotlight. |
| **Digitazione testo** | La ricerca inizia senza preselezionare forzatamente il primo elemento, permettendo di digitare liberamente. |
| **Freccia Giù (↓)** | Seleziona il primo risultato della lista, pronto per l'anteprima o l'esecuzione. |
| **Frecce Su (↑) e Giù (↓)** | Scorri comodamente l'intera lista dei risultati. **Se l'anteprima QuickLook è già aperta, cambia all'istante il file visualizzato senza chiudersi!** |
| **Invio (↵)** | Esegue immediatamente l'elemento selezionato (o il primo risultato della ricerca se nessun elemento è evidenziato). |
| **Spazio** (con elemento selezionato) | Apre l'anteprima rapida (*QuickLook*). **Lo Spotlight rimane aperto sotto!** |
| **Spazio** (dentro l'anteprima) | Chiude *soltanto* l'anteprima e restituisce il focus allo Spotlight per continuare a scorrere i risultati. |
| **Esc** (dentro l'anteprima o nello Spotlight) | Tasto generale e non modificabile: chiude istantaneamente sia l'anteprima QuickLook che la barra di ricerca Spotlight contemporaneamente. |
| **Clic Destro su un risultato** | Apre il menu contestuale dinamico: **📁 Vai al percorso file**, **🚀 Apri**, **👁 Anteprima**, **📋 Copia percorso**, e azioni rapide di conversione. |
| **Doppio Clic** | Apre il file o l'applicazione direttamente con il programma predefinito. |
| **Pulsanti 🔄 e ⚙** | Nella barra inferiore: icona 🔄 per avviare una reindicizzazione rapida in background e rotella ⚙ per le Impostazioni. |
| **Clic fuori dalla finestra** | Chiude automaticamente il launcher. |

### Azioni Rapide, Menu Contestuale Windows e Conversioni Aggregate
Facendo clic destro su qualsiasi risultato in Spotlight, dentro QuickLook o tramite il **menu a cascata "AeroSpot" in Esplora Risorse** (Menu classico / `Shift + Tasto destro`), AeroSpot offre conversioni immediate sia su file singoli che su **selezioni multiple**:
- **📁 Vai al percorso file:** Apre la cartella in Esplora risorse ed evidenzia direttamente il file selezionato.
- **🖼 Converti in JPG / PNG:** Converte immagini (singole o in batch se dello stesso formato) in JPG ad alta qualità (con compositing pixel-level bianco puro per le trasparenze, immune ai ritagli High-DPI) o PNG trasparente.
- **📄 Converti in PDF / Unisci foto in unico PDF:** Converte immagini o documenti (`.md`, `.txt`, `.docx`, `.rtf`, `.odt`) in PDF nativi; selezionando più foto, consente di unirle tutte in un **unico documento PDF multi-pagina** ordinato.
- **📑 Unisci PDF selezionati:** Selezionando due o più file `.pdf` in Esplora Risorse, li fonde istantaneamente in un unico documento preservando qualità vettoriale e testi originali.
- **🎬 Converti in MP4 / 🎵 Estrai MP3 e WAV:** Converte video non MP4 (MKV, AVI, MOV, WMV, WEBM) in MP4 compatibile tramite FFmpeg o estrae tracce audio in MP3/WAV.
- **✨ Rimuovi sfondo (AI):** Rimuove lo sfondo da immagini fotografiche (singole o batch multiplo) tramite modello locale Onnx/DirectML con accelerazione GPU DirectX 12 e fallback CPU. Il modello (~44 MB) viene scaricato on-demand al primo utilizzo.
- **Pillola di notifica fluttuante intelligente:** Ogni operazione mostra una notifica Dark Acrylic ancorata nell'angolo in basso a destra del **monitor attivo corrente** (con rilevamento multi-monitor e per-monitor DPI) con avanzamento in tempo reale e pulsante "Mostra nella cartella" al termine.

---

## 3. Anteprima Rapida dei File (QuickLook)

Quando ti trovi in **Esplora risorse**, sul **Desktop** o all'interno delle finestre di dialogo **Apri / Salva / Allega file** di qualsiasi applicazione (es. WhatsApp, Chrome, Edge, Blocco Note, Office):
1. Fai clic su un file o selezionalo con le frecce della tastiera.
2. Premi la barra **Spazio**: si aprirà istantaneamente la finestra con l'anteprima del contenuto.
3. Premi nuovamente **Spazio** (o **Esc**) per chiudere l'anteprima.

### Funzioni Interattive nell'Anteprima:
- **Menu Contestuale con Tasto Destro:** Facendo clic destro in qualsiasi punto della finestra di QuickLook appare un menu contestuale Dark Fluent che consente di accedere a tutti i tool di conversione, azioni file e controlli finestra.
- **Rotazione Manuale (Foto e Video):** Pulsante dedicato **⟳** nell'intestazione di QuickLook per ruotare di 90° in senso orario ad ogni clic (0° → 90° → 180° → 270°). L'anteprima adatta istantaneamente le dimensioni e le proporzioni della finestra intera, eliminando bande nere laterali o barre verticali. La rotazione è puramente visiva in tempo reale: il file originale non viene modificato né sovrascritto sul disco.
- **Zoom con Rotella del Mouse (Immagini):** Ingrandisci o rimpicciolisci immagini ruotando la rotella del mouse ancorata esattamente sul puntatore.
- **Pan e Trascinamento:** Quando un'immagine è ingrandita, trascinala liberamente tenendo premuto il tasto sinistro del mouse. Pulsante **1:1** per ripristinare la scala originale.
- **Navigazione Documenti PDF:** Barra laterale sinistra con galleria miniature ad alta velocità (caricate in 1–2 ms per pagina); fai clic su qualsiasi miniatura per saltare immediatamente a quella pagina con evidenziazione dinamica del bordo. Scorri verticalmente in modo continuo tra le pagine, esegui il pan a manina con il tasto sinistro del mouse, usa i tasti `PageUp`/`PageDown`/`Home`/`End` oppure clicca sulle frecce verticali Su (`▲`) e Giù (`▼`) della capsula di pagina posta in basso a sinistra.
- **A Tutto Schermo (Fullscreen):** Premi **F11**, fai **doppio clic** sulla foto/video oppure clicca sul pulsante **⛶** in alto a destra per espandere l'anteprima a schermo intero.
- **Memoria di Posizione e Dimensione:** La finestra dell'anteprima memorizza posizione e dimensione durante la sessione, con opzione di memorizzazione persistente per categoria tramite l'icona spilla 📌 nell'header.

### Tabella Canonica dei Formati Supportati:

| Categoria | Formati ed Estensioni Supportate | Funzionalità del Visualizzatore / Note |
|---|---|---|
| **Documenti PDF** | `.pdf` | Visualizzazione ultra-rapida nativa tramite Google PDFium x64 (`Docnet.Core`, standard QuickLook Store), apertura sub-50ms su autentico foglio bianco candido con ombra elegante, **scorrimento continuo verticale** con tutte le pagine impilate, **barra laterale sinistra con galleria miniature delle pagine** (`PdfSidebar`, 144px) per navigazione rapida e selezione al clic con evidenziazione sincronizzata, **capsula flottante di pagina posizionata in basso a sinistra** sotto le miniature con frecce verticali Su (▲) e Giù (▼), caricamento asincrono con cache LRU (max 16 pagine in RAM per gestire file giganti), pan fluido a manina con tasto sinistro (`Cursors.SizeAll`), zoom `Ctrl + Rotellina` a **60 FPS ancorato al puntatore del mouse con tasto reset 1:1**, navigazione da tastiera `PageUp`/`PageDown`/`Home`/`End` ed esecuzione 100% in-process senza processi browser esterni. |
| **Immagini** | `.png`, `.jpg`, `.jpeg`, `.webp`, `.bmp`, `.gif`, `.ico` | Rendering ad alta risoluzione con DWM Cloaking anti-sfarfallio, decodifica DCT downsampled a 2560px per apertura rapida (< 150 ms), zoom e pan, supporto a GIF animate. |
| **Foto RAW** | `.arw`, `.dng`, `.cr2`, `.cr3`, `.nef`, `.orf`, `.rw2`, `.pef`, `.raf` | Strategia ad altissima velocità: 1) Estrazione istantanea (~20–30 ms) dell'anteprima JPEG incorporata nel file RAW a piena risoluzione senza decodifica lenta; 2) Codec WIC di sistema come ripiego con downsampling DCT e rotazione EXIF; 3) Scheda metadati elegante. |
| **Grafica Vettoriale** | `.svg` | Rendering vettoriale nativo ad altissima definizione con SharpVectors (WPF DrawingImage), zoom continuo senza sgranature, isolamento di sicurezza senza esecuzione script (`IncludeRuntime = false`) e fallback su codice sorgente XML. |
| **File Grafica Vettoriale AI** | `.ai` (Adobe Illustrator) | Scheda informativa pulita con metadati tecnici e pulsante di apertura nell'applicazione predefinita, escludendo automaticamente server COM di terze parti instabili per prevenire schermate nere. |
| **Fogli di Calcolo** | `.xlsx`, `.xlsm`, `.xltx` | Lettore nativo C# OpenXML a zero dipendenze (apre senza Office installato), navigazione multi-foglio a schede tab, griglia Dark Fluent con lettere colonna e numeri riga. |
| **Audio** | `.mp3`, `.wav`, `.m4a`, `.aac`, `.flac`, `.wma`, `.ogg`, `.mid`, `.midi`, `.aiff`, `.opus` | Giradischi d'epoca a 33 giri con foro mandrino centrale, rotazione animata continua, stanghetta cinematica che traccia i solchi e rientra a riposo su pausa/stop congelando l'angolo di rotazione. |
| **Video** | `.mp4`, `.mov`, `.mkv`, `.avi`, `.wmv`, `.webm`, `.h264`, `.h265`, `.m4v`, `.flv`, `.3gp`, `.ts`, `.mpg`, `.mpeg` | Riproduzione fluida hardware, rotazione automatica video verticali da smartphone, pipeline remux FFmpeg integrata per tracce multicanale non supportate da Windows (AC-3, DTS, TrueHD) con audio AAC stereo trasparente (parità VLC), timeline scrubber a clic istantaneo. |
| **Modelli 3D** | `.stl` | Motore di rendering 3D nativo a 60 FPS per modelli STL binari e ASCII con rotazione orbitale mouse drag, zoom con rotella, conteggio triangoli e dimensioni ingombro. |
| **Pagine Web** | `.html`, `.htm` | Visualizzatore passivo in modalità scura, protetto da esecuzione script e conforme ai vincoli di airspace WPF. |
| **Testo e Codice** | `.txt`, `.md`, `.py`, `.json`, `.log`, `.csv`, `.ini`, `.cs`, `.xml`, `.yaml`, `.toml`, `.sql`, `.bat`, `.cmd`, `.cpp`, `.c`, `.h`, `.rs`, `.go`, `.ts`, `.js` | Font monospaziato (*Cascadia Mono* / *Consolas*), rilevamento automatico codifica (UTF-8, UTF-16, ANSI), conteggio righe, troncamento di sicurezza a 100 KB per prevenire picchi di RAM. |
| **Documenti Office & RTF/ODT** | `.docx`, `.docm`, `.dotx`, `.rtf`, `.odt` | Lettori nativi in-process zero-Office (~5–15 ms) per `.docx`/`.docm`/`.dotx`, `.rtf` e `.odt` con tipografia Dark Fluent ed esportazione PDF 1-clic (nessun processo `WINWORD.EXE` in background). |
| **Cartelle** | Cartelle e directory del file system | Visualizzazione immediata a 0 ms con calcolo asincrono in background di dimensione totale su disco, numero di file e sottocartelle. |
| **Collegamenti & App** | `.lnk`, app UWP / Windows Store | Scheda con metadati, icona vettoriale ad alta definizione ed estrazione del target reale. |
| **File Generici** | Tutte le altre estensioni | Scheda informativa pulita con icona di sistema Shell, dimensione, data di modifica e percorso completo. |

> **Protezione della digitazione:** Quando stai rinominando un file o digitando nella barra degli indirizzi o di ricerca di Esplora risorse, la barra Spazio inserisce normalmente uno spazio senza aprire l'anteprima.

---

## 4. Pannello delle Impostazioni

Puoi accedere alle impostazioni in due modi:
1. **Dalla barra di sistema:** Clic destro sull'icona di AeroSpot accanto all'orologio -> **Impostazioni...**
2. **Dalla barra di ricerca:** Clic sul pulsante **⚙ Impostazioni** in basso a destra nello Spotlight.

### Schede e Sezioni Configurabili:

Il pannello impostazioni è suddiviso in tre sezioni tematiche chiare e immediatamente navigabili:

1. **Generali:**
   - **Lingua interfaccia:** selezione tra `Automatica`, `Italiano` ed `English` con commutazione istantanea in memoria a caldo.
   - **Tasti di scelta rapida:** configurazione personalizzata delle hotkey globali per Spotlight (predefinito `Alt + Spazio`) e QuickLook (predefinito `Spazio`).
   - **Avvio e Prestazioni:** opzioni per *Avvia con Windows*, motore sempre attivo (*Keep-Warm*) e *Pre-avvio con Windows*.
   - **Integrazione Esplora Risorse:** comandi rapidi nel menu contestuale di Windows e apertura automatica cartella dopo le conversioni multimediali.
   - **Aggiornamenti & Info:** frequenza di controllo su GitHub, verifica manuale immediata, visualizzazione versione, cartella dati `Data\` e pulizia cache icone.

2. **QuickLook:**
   - **Guida e Scorciatoie Anteprime:** panoramica dei comandi rapidi da tastiera (`Spazio` / `Esc` per chiudere, `F11` o doppio clic per fullscreen, zoom con rotella, pan con mouse, `PageUp`/`PageDown` per PDF, pulsante `1:1`).
   - **Rotazione e Proporzioni:** pulsante **⟳** per rotazione a 90° orari in tempo reale per foto e video con auto-adattamento della sagoma finestra (zero barre nere).
   - **Comportamento Finestra:** gestione della spilla 📌 per memorizzare le dimensioni delle finestre per ciascuna categoria di file, con pulsante per ripristinare le dimensioni predefinite originali.
   - **Blocco Finestra Sempre in Primo Piano (Always on top):** opzione *"Mantieni la finestra sempre in primo piano"* per scegliere se mantenere l'anteprima QuickLook permanentemente bloccata in primo piano anche facendo clic su altre finestre, oppure sfruttare il comportamento contestuale predefinito dove la finestra esterna attiva sale fluidamente sopra QuickLook alla deattivazione.

3. **Spot (Ricerca e Indicizzazione):**
   - **Schermata Iniziale:** modalità di visualizzazione a campo vuoto (*Elementi aperti di recente*, *Elementi aperti più spesso*, *Ricerche più frequenti*, *Nessuno/Privacy*).
   - **Dischi Inclusi:** selezione rapida delle unità da indicizzare con scheda informativa su RAM, CPU e velocità.
   - **Esclusioni:** elenco cartelle escluse dall'indice di ricerca con aggiunta/rimozione immediata.
   - **Frequenza di Aggiornamento:** intervallo di scansione periodica dei file.
   - **Cronologia e Privacy:** pulsante di cancellazione irreversibile della cronologia ricerche e aperture recenti dal database SQLite locale.
   - **Gerarchia Categorie:** ordinamento visivo delle categorie di risultati tramite Drag & Drop fluido con elemento fantasma. Opzione gerarchia mista e pulsante di ripristino ordine predefinito.

---

## 5. Icona e Menu della Barra di Sistema (Tray)

In basso a destra, accanto all'orologio di Windows:
- **Clic sinistro (o doppio clic):** Mostra la barra di ricerca rapida Spotlight.
- **Clic destro:** Menu contestuale nativo Win32 con:
  - *Apri AeroSpot (`Alt + Spazio`)*
  - *Impostazioni...*
  - *Verifica aggiornamenti...*
  - *Stato del motore* (indica se il worker grafico è a riposo o attivo in memoria)
  - *Esci da AeroSpot* (chiusura totale e pulita di tutti i processi).

---

## 6. Procedura Dettagliata di Installazione, Aggiornamento e Disinstallazione

### Modalità Installata (Consigliata)
1. **Installazione:** Scarica ed esegui `AeroSpot-Setup-v<ver>.exe`. L'installer non richiede privilegi di amministratore e si installa in `{localappdata}\Programs\AeroSpot`. Crea automaticamente le icone nel menu Start e sul Desktop.
2. **Aggiornamento:** Quando una nuova versione è disponibile, puoi cliccare su "Aggiorna" nella notifica o nelle Impostazioni. L'aggiornamento chiude i processi attivi, sostituisce i binari conservando integralmente l'indice di ricerca e le impostazioni personali in `%LOCALAPPDATA%\AeroSpot\Data\`.
3. **Disinstallazione:** Da *App e funzionalità* di Windows, seleziona AeroSpot e clicca *Disinstalla*. L'uninstaller rimuove i binari, le icone, le voci di avvio automatico e ti chiede se cancellare anche il database dell'indice e le preferenze.

### Modalità Portable
1. **Installazione:** Scarica ed estrai `AeroSpot-v<ver>-win-x64-portable.zip` in qualsiasi cartella locale o unità USB. Avvia `AeroSpot.exe`.
2. **Aggiornamento:** Scarica il nuovo archivio ZIP, chiudi AeroSpot dalla Tray icon, e sovrascrivi i file nella cartella esistente. La cartella `Data\` contenente le tue preferenze e il database non viene toccata.
3. **Disinstallazione:** Esegui `Uninstall.exe` situato accanto ad `AeroSpot.exe` per una rimozione completa e sicura a zero residui.

---

## 7. Risoluzione dei Problemi Frequenti

- **La combinazione `Alt + Spazio` non risponde:**  
  Verifica se un'altra utility (es. PowerToys Run o Listary) occupa già la combinazione. AeroSpot rileva il conflitto, notifica l'utente e commuta sui fallback sicuri `Ctrl + Spazio` o `Alt + F12`. Puoi modificare la combinazione in qualsiasi momento dalle Impostazioni.
- **Sblocco rapido di emergenza:**  
  In caso di blocco dell'interfaccia o perdita di focus, la pressione del tasto `F12` forza il rilascio immediato degli hook di tastiera e ripristina il controllo a Windows.
- **Consultazione dei file di log:**  
  I log diagnostici delle ultime 24 ore per Host, Worker e satellite Tools sono conservati nella cartella `logs\` all'interno della directory dati dell'applicazione.
