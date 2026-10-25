# Guida all'uso di AeroSpot

Benvenuto in **AeroSpot**, l'applicazione moderna per Windows 11 progettata per unire la rapidità di ricerca di un launcher in stile *Spotlight* con l'immediatezza delle anteprime di *QuickLook*, con un consumo di memoria residente dell'Host a riposo inferiore a 10 MB di Private Bytes.

---

## 1. Avvio Rapido

AeroSpot è un'applicazione portable nativa per Windows 11 (x64) a zero installazione e zero dipendenze esterne:
- **Avvio dell'applicazione:** Fai doppio clic su `AeroSpot.Host.exe` (oppure sul collegamento `AeroSpot`). L'applicazione si avvia silenziosamente in background e posiziona l'icona Fluent scura nella barra delle applicazioni in basso a destra (Tray accanto all'orologio di Windows).
- **Chiusura completa:** Clic destro sull'icona di AeroSpot nella Tray -> **Esci da AeroSpot**. Tutti i processi (Host e Worker) vengono terminati in modo pulito e immediato.
- **Disinstallazione / Pulizia registri:** Se hai attivato l'avvio automatico con Windows e desideri rimuovere l'applicazione, esegui da prompt o terminale:
  ```cmd
  AeroSpot.Host.exe --cleanup
  ```
  Questo comando rimuove istantaneamente ogni riferimento dal Registro di Windows prima che tu proceda a eliminare la cartella.

---

## 2. Come si usa il Launcher (Ricerca rapida)

### Scorciatoia da tastiera
- **Scorciatoia predefinita:** `Alt + Spazio` (personalizzabile nelle Impostazioni con `Ctrl + Spazio`, `Win + Spazio` o combinazione personalizzata).
- Puoi richiamare AeroSpot in qualsiasi momento da qualsiasi applicazione o schermata.

### Come cercare
1. Premi la combinazione di tasti: apparirà la barra di ricerca fluida con effetto acrilico scuro al centro dello schermo.
2. Digita il nome di ciò che cerchi:
   - **Applicazioni installate e di sistema** (es. *Chrome*, *Blocco note*, *Calcolatrice*, *Terminale*, *VS Code*, *Steam*, app UWP/Store come *WhatsApp*)
   - **Documenti e file** (es. *relazione.docx*, *tabella.xlsx*, *progetto.py*, *foto.png*, *video.mp4*)
   - **Cartelle** (es. *Download*, *Documenti*, *Immagini*)
3. **Calcolatrice integrata:** Digita direttamente un'operazione matematica (es. `2+2`, `15 * 4`, `sqrt(144)`, `sin(pi/2)`) per visualizzare istantaneamente il risultato.
4. **Convertitore di unità:** Converti al volo misure di lunghezza, massa/peso (grammi, ettogrammi, chilogrammi, libbre, once), volume (litri, millilitri, centilitri, galloni), temperature (°C, °F, K) e valute.
5. **Lo spazio funziona normalmente:** se cerchi parole composte (es. *"Google Chrome"* o *"Note spesa"*), premendo la barra spaziatrice inserisci normalmente uno spazio senza attivare l'anteprima.

### Tasti Rapidi nella Finestra di Ricerca
| Tasto | Azione |
|---|---|
| **Alt + Spazio** | Apre o chiude la barra di ricerca Spotlight (interruttore / toggle). Se QuickLook è aperta, chiude solo Spotlight. |
| **Digitazione testo** | La ricerca inizia senza preselezionare forzatamente il primo elemento, permettendo di digitare liberamente. |
| **Freccia Giù (↓)** | Seleziona il primo risultato della lista, pronto per l'anteprima o l'esecuzione. |
| **Frecce Su (↑) e Giù (↓)** | Scorri comodamente l'intera lista dei risultati. **Se l'anteprima QuickLook è già aperta, cambia all'istante il file visualizzato senza chiudersi!** |
| **Invio (↵)** | Esegue immediatamente l'elemento selezionato (o il primo risultato della ricerca se nessun elemento è evidenziato). |
| **Spazio** (con elemento selezionato) | Apre l'anteprima rapida (*QuickLook*). **Lo Spotlight rimane aperto sotto!** |
| **Spazio** (dentro l'anteprima) | Chiude *soltanto* l'anteprima e restituisce il focus allo Spotlight per continuare a scorrere i risultati. |
| **Esc** (dentro l'anteprima o nello Spotlight) | Tasto generale e non modificabile: chiude istantaneamente sia l'anteprima QuickLook che la barra di ricerca Spotlight contemporaneamente. |
| **Clic Destro su un risultato** | Apre il menu contestuale: **📁 Vai al percorso file** (apre la cartella in Esplora risorse con il file selezionato), **🚀 Apri**, **👁 Anteprima**, **📋 Copia percorso**. |
| **Doppio Clic** | Apre il file o l'applicazione direttamente con il programma predefinito. |
| **Pulsanti 🔄 e ⚙** | Nella barra inferiore: icona 🔄 per avviare una reindicizzazione rapida in background e rotella ⚙ per le Impostazioni. |
| **Clic fuori dalla finestra** | Chiude automaticamente il launcher. |

---

## 3. Anteprima Rapida dei File (QuickLook)

Quando ti trovi in **Esplora risorse** (o sul **Desktop**):
1. Fai clic su un file o selezionalo con le frecce della tastiera.
2. Premi la barra **Spazio**: si aprirà istantaneamente la finestra con l'anteprima del contenuto.
3. Premi nuovamente **Spazio** (o **Esc**) per chiudere l'anteprima.

### Funzioni interattive nell'anteprima:
- **Zoom con Rotella del Mouse (Immagini e PDF):** Ingrandisci o rimpicciolisci immagini e documenti PDF ruotando la rotella del mouse ancorata esattamente sul puntatore.
- **Pan e Trascinamento:** Quando un'immagine o un documento PDF è ingrandito, trascinalo liberamente tenendo premuto il tasto sinistro del mouse. Pulsante **1:1** per ripristinare la scala originale.
- **A Tutto Schermo (Fullscreen):** Premi **F11**, fai **doppio clic** sulla foto/video oppure clicca sul pulsante **⛶** in alto a destra per espandere l'anteprima a schermo intero.
- **Memoria di Posizione e Dimensione:** La finestra dell'anteprima memorizza posizione e dimensione durante la sessione, con opzione di memorizzazione persistente per categoria tramite l'icona spilla 📌 nell'header.

### Formati supportati:
- **Documenti PDF:** Visualizzatore nativo multi-pagina vettoriale ad alta definizione (`Windows.Data.Pdf`). Rileva l'aspect ratio reale (panoramico vs ritratto), consente scorrimento verticale multi-pagina, zoom fluido `Ctrl + Rotella del mouse` (0.4x - 4.0x), pan con tasto sinistro del mouse e ripristino `1:1`.
- **Audio (.mp3, .wav, .m4a, .aac, .flac, .wma, .ogg, .mid, .midi, .aiff, .opus):** Riproduzione nativa con visualizzatore a disco in vinile rotante a 33 giri con foro ed etichetta centrale vintage, stanghetta del giradischi che avanza sul disco e rientra a lato in pausa/stop senza resettare la rotazione, scrubber interattivo con minutaggio e cursore volume.
- **Modelli 3D STL (.stl):** Motore di rendering 3D nativo a 60 FPS per formati STL binari e ASCII. Rotazione orbitale 3D tramite click e trascinamento mouse, zoom con rotella, indicazione di dimensioni e numero di triangoli.
- **Video (.mp4, .mov, .mkv, .avi, .wmv, .webm, .h264, .h265, .m4v, .flv, .3gp, .ts, .mpg, .mpeg):** Riproduzione fluida con volume predefinito al 50%, supporto a video oltre 1 ora con formato orario completo (`hh:mm:ss`), barra temporale scrubber interattiva a click istantaneo e pulsanti vettoriali frameless.
- **Fogli di Calcolo Excel (.xlsx, .xlsm, .xltx):** Lettore nativo OpenXML ad altissime prestazioni con visualizzazione a griglia Dark Fluent, schede tab per fogli multipli e scrollbar orizzontale/verticale bidirezionale corretta.
- **Testo e codice sorgente:** .txt, .md, .py, .json, .log, .csv, .ini, .cs, .xml, .yaml, .toml, .sql, .bat, .cmd, .cpp, .c, .h, .rs, .go, .ts, .js con font monospazio, codifica UTF-8/Unicode rilevata automaticamente, conteggio righe e scorrimento verticale.
- **Documenti Office e RTF:** Documenti Word (.docx), presentazioni PowerPoint (.pptx), Rich Text (.rtf) tramite i visualizzatori di sistema registrati (COM Preview Handlers).
- **Cartelle:** Visualizzazione a 0 ms con percorso chiaro in alto, data di modifica nel footer e calcolo asincrono delle dimensioni totali su disco, numero di file e cartelle (es. `1.45 GB (1.240 file, 48 cartelle)`).
- **Immagini:** PNG, JPG, JPEG, WebP, GIF animate, BMP, ICO ad alta risoluzione con zoom e pan.
- **Collegamenti (.lnk) e Applicazioni:** Scheda informativa con percorso, dimensione e data, priva di sovrapposizioni di testo e con icone vettoriali ad alta definizione per app Desktop e UWP/Store.
- **Altri file:** Scheda con metadati, formato, data di modifica e dimensione file.

> **Protezione scrittura:** Se stai rinominando un file o digitando nella barra degli indirizzi o di ricerca di Windows, la barra Spazio inserisce normalmente uno spazio senza aprire l'anteprima.

---

## 4. Pannello delle Impostazioni

Puoi accedere alle impostazioni in due modi:
1. **Dalla barra di sistema:** Clic destro sull'icona di AeroSpot accanto all'orologio -> **Impostazioni...**
2. **Dalla barra di ricerca:** Clic sul pulsante **⚙ Impostazioni** in basso a destra nello Spotlight.

### Opzioni configurabili:
1. **Lingua interfaccia:** Scegli tra `Automatica (lingua di sistema)`, `Italiano` o `English`. Il cambio è istantaneo su tutta l'interfaccia senza riavviare.
2. **Scorciatoia Spotlight:** Scegli tra `Alt + Spazio`, `Ctrl + Spazio`, `Win + Spazio` o registra una scorciatoia personalizzata. Il sistema verifica i conflitti in tempo reale.
3. **Scorciatoia QuickLook:** Scegli tra `Spazio`, `Shift + Spazio` o registra il tuo tasto preferito.
4. **Dischi indicizzati:** Seleziona con caselle di spunta quali dischi monitorare (di base l'unità di sistema e le unità fisse collegate, es. `C:\`, `E:\`, `D:\`).
5. **Esclusioni di cartelle:** Di base esclude librerie giochi (*SteamLibrary*, *Epic Games*), cartelle temporanee (*Temp*, *AppData\Local\Temp*) e cartelle di sviluppo (*.git*, *node_modules*, *bin*, *obj*). Puoi aggiungere o rimuovere qualsiasi cartella personalizzata.
6. **Sistema e Prestazioni:**
   - *Avvio con Windows:* Registra AeroSpot nell'avvio automatico senza privilegi di amministratore. Rispetta lo stato di Gestione Attività e ripara automaticamente il percorso se la cartella viene spostata.
   - *Pre-avvio del Worker:* Se sia "Avvia con Windows" sia "Mantieni motore sempre attivo" sono abilitati, AeroSpot pre-avvia direttamente il Worker in background all'avvio del sistema, garantendo una reattività di 0 ms fin dal primo tasto.
   - *Auto-spegnimento Worker:* Consumo base dell'Host pari a soli **~8 MB di RAM**. Quando il Worker si attiva per un'anteprima o una ricerca, si spegne dopo 30 secondi di inattività restituendo tutta la memoria a Windows. Tra un'anteprima e l'altra è attivo un periodo di grazia di 10 secondi per azzerare i micro-rallentamenti durante la consultazione rapida di più file.
7. **Cartella Dati e Archiviazione:**
   - Mostra il percorso reale della cartella dati: se è presente `portable.txt` i dati risiedono in `<CartellaApplicazione>\Data\`, altrimenti in `%LOCALAPPDATA%\AeroSpot\`.
   - Visualizza in tempo reale lo spazio occupato su disco dal database dell'indice (`index.db`).
8. **Istruzioni d'uso & Documentazione:**
   - Pulsanti per aprire la guida utente offline formattata nel browser di sistema o consultare il repository ufficiale su GitHub.

---

## 5. Icona e Menu della Barra di Sistema (Tray)

In basso a destra, accanto all'orologio di Windows:
- **Clic sinistro (o doppio clic):** Mostra la barra di ricerca rapida Spotlight.
- **Clic destro:** Menu contestuale con:
  - *Apri AeroSpot (Alt + Spazio)*
  - *Impostazioni...*
  - *Stato del Worker* (indica se il motore grafico è a riposo o attivo in memoria)
  - *Esci da AeroSpot* (chiusura totale e pulita di tutti i processi).

---

## 6. Identità Grafica e Logo

Gli asset grafici originali si trovano nella cartella `design/`:
- `design/logo.svg`: Vettoriale SVG ad alta precisione con le geometrie a triangoli contrapposti e taglio diagonale.
- `design/app_icon.svg` e `app_icon.png`: Icona in stile Windows 11 Fluent con squircle scuro e gradiente ciano/bianco.
- `design/app.ico`: Icona multi-risoluzione incorporata negli eseguibili e nella tray icon.

---

## 7. Risoluzione dei Problemi

- **La combinazione di tasti non risponde:**
  Verifica se un'altra utility (es. Listary o PowerToys) occupa `Alt + Spazio`. AeroSpot commuta automaticamente su `Ctrl + Spazio` o `Alt + F12` e ti avvisa con una notifica. Puoi anche selezionare una scorciatoia alternativa nelle Impostazioni.
- **Tasto di sblocco d'emergenza:**
  In caso di blocchi insoliti, premendo `F12` l'Host rilascia immediatamente gli hook di tastiera e ripristina il focus.
- **File di log:**
  I log dettagliati delle operazioni si trovano sempre nella cartella dati (`<CartellaDati>\logs\host.log` e `worker.log`).
