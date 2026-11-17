# AeroSpot — Stato dei Moduli e del Progetto

**Data istantanea:** 17.11.2026  
**Versione:** `1.6.0`  
**Stato compilazione:** `Release` 0 errori, 0 avvisi C# (`net10.0-windows10.0.19041.0`)  
**Stato test suite:** **177 superati**, 0 falliti, 2 ignorati (test diagnostici su file multimediali reali), totale **179 test** automatizzati  
**Ambiente di test:** Windows 11 Enterprise x64 (10.0.26300), .NET SDK 10.0.402, 32 GB RAM  

---

## 1. Tabella di Stato dei Moduli

| Modulo | Componente | Stato Oggettivo | Ultima Verifica | Note Tecniche |
|---|---|---|---|---|
| **Host Residente** | `AeroSpot.Host` | **Verificato con test** | 17.11.2026 | Win32 puro, tray icon nativa con retry asincrono e gestione `TaskbarCreated`, hook `WH_KEYBOARD_LL` coordinato con stato QuickLook (`AllowSetForegroundWindow` su trigger Spazio/Spotlight + gestione smart Explorer quando QuickLook perde il focus), `StopDynamicTracking` race-safe con guardia `!_isPreviewActive`, chiusura unificata via Escape (`CloseAll`), auto-registrazione/riparazione all'avvio del menu contestuale Explorer (`ShellContextMenuIntegration` con `MultiSelectModel=Player`), timeout cold-start resiliente a 25s, pre-avvio worker, assenza totale WPF/WinForms |
| **Lifecycle & IPC** | Named Pipe Server/Client | **Verificato con test** | 04.11.2026 | Disaccoppiamento asincrono `Dispatcher.InvokeAsync` (eliminazione deadlock IPC / freeze UI), retry con delay su handshake di riconnessione client, timeout inattività, autoshutdown, gestione crash/kill del Worker |
| **Spotlight Launcher** | `MainWindow` | **Verificato con test** | 17.11.2026 | Ricerca a testo vuoto asincrona su thread worker (`ExecuteSearchAsync`), attivazione foreground garantita tramite `ForceForegroundWindow` + reset `Topmost`, ricerca FTS5 con ordinamento cronologico di parità (`LastModifiedUtcTicks` prioritario su lunghezza), esclusione calcoli da `SearchHistory`, Drag & Drop con ghost perfettamente ancorato su monitor High-DPI (125%-200%), isolamento thread STA per chiamate shell COM in `IconService`, calcolatrice con sottotitoli contestuali espliciti per sconti/percentuali (es. `600 - 60%` = `240` con `"Sconto 60% (-360)"`), menu conversioni aggiornato con `Converti in MP4` |
| **Indicizzazione & DB** | SQLite FTS5 / Crawler | **Verificato con test** | 04.11.2026 | Schema v4 con pulizia retroattiva espressioni da `SearchHistory`, RealtimeFolderWatcher attivo su cartelle utente, scheduler frequenza |
| **QuickLook Viewer** | `PreviewWindow` | **Verificato con test** | 17.11.2026 | Attivazione primo piano deterministica all'avvio senza necessità di aprire prima Spotlight (`Win32.ForceForegroundWindow` con `AttachThreadInput` + reset `Topmost` in `HidePreview` e `ShowPreviewForFileAsync`); ripresa in primo piano su `Spazio` se finita dietro Explorer (`!IsActive && !Topmost`); posizionamento prima apertura corretto in `PositionWindowRelative` (`ApplyModelToUI`/`PositionAndSizeWindow` prima di `Show()`); chiusura immediata Spazio resiliente via hook globale coordinato; supporto nativo finestre di dialogo Apri/Salva/Allega (`#32770`) in app terze via UI Automation senza injection; pulsante rotazione 90° con riadattamento proporzioni geometriche finestra; anteprima PDF nativa Google PDFium x64 (`Docnet.Core`); visualizzatore documenti Word (`.docx`) OpenXML nativo; pipeline remux FFmpeg on-demand audio AC-3/DTS; menu conversioni rapido comprensivo di `Converti in MP4` |
| **DWM Cloaking** | Mascheramento finestre | **Verificato** | 04.11.2026 | Zero frame fantasma, zero flash nero, `WaitRenderedAsync` 2-frame compositing con cancellazione rapida |
| **Strumenti AI & Media** | `AeroSpot.Tools` | **Verificato con test** | 11.11.2026 | Processo satellite on-demand, DirectML GPU D3D12 / CPU, telemetria ONNX reale (`STAGE: DOWNLOADING`/`INFERENCE` e traguardi progressivi su modello in cache senza salto al 100%), uscita immediata al termine (eliminato delay 15s post-task), aggregatore multi-selezione Explorer `FileBatchCollector` (`NamedMutex` + `NamedPipe` + ordinamento logico Windows), toast nativo `FloatingNotificationWindow` standalone per azioni avviate da Esplora File |
| **Conversioni File** | `MediaConversionService` | **Verificato con test** | 11.11.2026 | Fusione trasparenza per JPG/PDF a livello di pixel (`CompositeOverWhite` su `Pbgra32`, zero ritagli su PNG High-DPI es. 288 DPI), conversione JPG/PNG, PDF multipagina da selezione immagini (`ConvertImagesToMultiPagePdfAsync`), unione PDF multipli con `PDFsharp 6.2.4` MIT (`MergePdfsAsync`), conversione video MP4 FFmpeg (stream copy H.264 o `libx264`), estrazione audio MP3/WAV, PDF ISO 1.4 per documenti |
| **Aggiornamenti** | `UpdateService` | **Verificato con test** | 04.11.2026 | Verifica semantica release GitHub, download SHA-256, frequenze configurabili |
| **Impostazioni & UX** | `SettingsWindow` / `FloatingNotificationWindow` | **Verificato con test** | 11.11.2026 | Riorganizzazione in 3 sezioni (Generali, QuickLook, Spot) con deep-linking da Spotlight; toast flottante bloccato dinamicamente sul monitor del cursore (`MonitorFromPoint` + `GetDpiForMonitor` + `SetWindowPos`) con riposizionamento automatico al resize/comparsa pulsanti (zero debordi su configurazioni doppio schermo) |
| **Localizzazione** | `LocalizationService` | **Verificato con test** | 11.11.2026 | Parità dizionari 100% IT/EN, supporto completo chiavi menu contestuale Explorer e azione `Converti in MP4` |
| **Edizione Portable / Standalone (SSD)** | `AeroSpot.exe` / `app\` | **Verificato** | 09.11.2026 | Ambiente installato in `C:\Programmi_miei\AeroSpot` su unità SSD (C:), marcatore `portable.txt`, dati in `Data\`, launcher radice sub-2ms, `Uninstall.exe`, script di deploy rapido `deploy-local.ps1` (preserva Data\) |
| **Edizione Setup** | Inno Setup | **NON VERIFICATO** | 04.11.2026 | In fase di preparazione procedura di rilascio |

---

## 2. Metriche Prestazionali Verificate

I dati sottostanti includono sia i benchmark di laboratorio sia le misurazioni continue reali campionate tramite `Avvia_Monitor_Prestazioni.bat` (`performance_20261104_154734.csv`):

| Metrica | Valore Misurato | Ambiente / Riferimento |
|---|---|---|
| **Working Set Worker a riposo (post-bonifica)** | **3.82 – 9.55 MB** | Misurato su Windows 11 x64 dopo timer di grazia 10s (`EmptyWorkingSet`) |
| **Working Set Worker attivo / picco** | **182 – 257.6 MB** | Durante anteprima multimediale complessa e remux video |
| **Memoria Anteprima PDF Nativa (Google PDFium x64)** | **Worker: ~158 MB Priv (~216 MB WS)**<br>**Sottoprocessi esterni: 0 processi (0 MB)** | Misurato su dispensa universitaria da 40 pagine (`Dispensa_Completa_Diritto_Industriale.pdf`); apertura in **~15 ms** e render prima pagina in **~12–25 ms**; pan a manina con tasto sinistro e zoom `Ctrl + Rotellina` a 60 FPS; **zero processi esterni avviati** (risparmio di oltre 250 MB di RAM rispetto a WebView2) |
| **Private Bytes Worker (riposo / picco)** | **~248 MB / ~510 MB** | Memoria riservata gestita dal runtime .NET 10 |
| **Worker post-shutdown (inattività)** | **0 MB** (processo terminato) | Spegnimento pulito a timeout di inattività, zero zombie |
| **Working Set Host a riposo** | **~28 – 35 MB** (Private: ~7–23 MB) | Processo Win32 residente headless (nessun WPF/WinForms) |
| **AeroSpot.Tools a riposo** | **0 MB** (non caricato) | Processo effimero on-demand, autoshutdown a 15s |
| **Utilizzo CPU a riposo (Host + Worker)** | **0.00% – 0.05%** | Campionato a intervalli di 5s dal monitor diagnostico |
| **Utilizzo CPU di picco (Remux / Decodifica AV)** | **0.40% – 1.61%** | Picco massimo su CPU multi-core durante remux video 4K/FHD |
| **Velocità Remux Audio FFmpeg (AC-3/DTS)** | **~60x tempo reale** | Remux trasparente stereo AAC 192 kbps (`-c:v copy`), cache hit a 0 ms |
| **Latenza estrazione proporzioni PDF (PDFium)** | **10 – 15 ms** | Google PDFium nativo C++ in-memory (vs 50ms WinRT, vs 4-5s bitmap legacy) |
| **Cold start Worker** | **~320 – 560 ms** | Avvio iniziale processo WPF (.NET 10 R2R) |
| **Warm start Worker** | **< 1 ms** | Ripresa istantanea con Keep-Warm attivo |
| **Latenza decodifica JPG (24-48 MP)** | **~139 ms** (vs 325 ms prec.) | DCT downsampling max 2560px + probing EXIF |
| **Latenza anteprima RAW camera** | **~150 – 240 ms** | WIC codec / estrazione JPEG embedded |
| **Latenza rendering SVG vettoriale** | **~12 – 25 ms** | SharpVectors engine managed |
| **Latenza query SQLite FTS5** | **2 – 4 ms** | Query su indice pronto con tokenizzazione prefissa |
| **Debounce digitazione Spotlight** | **50 ms** | Disaccoppiamento query per digitazione fluida a 60/144 fps |
| **Reset ricerca Spotlight (`ResetSearchImmediate`)** | **0 ms (sincrono)** | Svuotamento e ripopolamento Home istantaneo su Esc / Alt+Spazio |

---

## 3. Limitazioni Note e Verifiche Aperte

- **Conflitto Hotkey con utility terze (Verificato):** Se `Alt + Spazio` è già impegnata sul sistema (es. PowerToys Run), AeroSpot intercetta l'errore 1409 Win32, notifica l'utente tramite balloon tray e imposta la combinazione alternativa `Alt + F12`.
- **Drag & Drop su Windows 11 Explorer CommandBar (Verificato e Accettabile):** Quando si trascina un file da Spotlight e il cursore attraversa la barra superiore delle cartelle di Esplora Risorse (barra degli strumenti con taglia/copia/incolla o barra del percorso), si può avvertire un lieve micro-freeze/esitazione causato dal meccanismo di hit-testing OLE nativo di Windows 11 su controlli moderni XAML Island. La resa è considerata accettabile e ampiamente migliorata dall'utente, lasciata a monitoraggio senza ulteriori modifiche immediate.
- **Visualizzatore PDF Nativo Google PDFium x64 ad Alte Prestazioni stile QuickLook Store (Verificato):** Anteprima PDF nativa ultra-rapida sub-50ms basata sulla libreria Google PDFium x64 (`Docnet.Core`), rimuovendo completamente la dipendenza `Microsoft.Web.WebView2` dal progetto. Le pagine vengono visualizzate su autentico foglio di carta bianco candido tramite alpha-blending veloce a 5ms su `0xFFFFFFFF` con raffinata ombreggiatura stile Apple/QuickLook. Include una **barra laterale sinistra con le miniature scorrevoli delle pagine (`PdfSidebar`)** da 144px per la navigazione rapida e la selezione diretta al clic con evidenziazione dinamica, contatore di pagina posizionato in basso a sinistra subito sotto le miniature con frecce verticali Su (▲) e Giù (▼), **scorrimento continuo verticale** con tutte le pagine impilate una sotto l'altra, caricamento asincrono su richiesta (lazy-loading) e bonifica LRU in-memory (max 16 pagine in RAM per preservare le prestazioni su documenti di centinaia di pagine), pan continuo con tasto sinistro (`Cursors.SizeAll`), zoom millimetrico continuo `Ctrl + Rotellina` a 60 FPS ancorato al cursore con reset `1:1` e tasti `PageUp`/`PageDown`/`Home`/`End`. Esecuzione interamente in-process con **zero processi browser esterni `msedgewebview2.exe` avviati** (risparmio di oltre 250 MB di RAM rispetto a WebView2) e rilascio immediato all'uscita.
- **Reset Immediato Ricerca Spotlight (Verificato):** Eliminato il glitch di persistenza del testo/risultati precedenti alla riapertura di Spotlight. Il reset avviene in modo sincrono e immediato (`ResetSearchImmediate`) sia alla pressione di Esc/Alt+Spazio sia prima della visualizzazione della finestra.
- **Elementi virtuali Shell di Windows (Verificato):** Elementi puramente logici privi di percorso fisico su file system (*Questo PC*, *Cestino*, librerie virtuali) non aprono QuickLook; la pressione dello Spazio viene passata al comportamento ordinario di Windows.
- **Auto-Update end-to-end con asset reali GitHub (NON VERIFICATO prima della pubblicazione):** L'API `releases/latest` richiede la pubblicazione effettiva di una release con i relativi asset binari (`Setup.exe`, `.zip`, `SHA256SUMS.txt`). La logica di confronto semantico, parsing JSON e verifica SHA-256 è coperta al 100% da test unitari mockati.
- **Avvisi Windows SmartScreen / Antivirus (NON VERIFICATO su PC esterni puliti):** In assenza di firma digitale commerciale (EV Code Signing), l'eseguibile installer e lo zip potrebbero generare avvisi euristici SmartScreen ("Autore sconosciuto") al primo download.
