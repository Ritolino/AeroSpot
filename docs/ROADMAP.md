# AeroSpot — Roadmap Futura

Questo documento elenca le funzionalità pianificate e le aree di ricerca e sviluppo future per AeroSpot.
Tutte le voci rappresentano **linee guida ed esplorazioni future**, senza scadenze temporali vincolanti né promesse contrattuali.

---

## 🚀 Funzionalità e Integrazioni Future

### 1. Miglioramenti ai Visualizzatori QuickLook
- **Modelli 3D avanzati:** estensione del supporto 3D oltre ai file `.stl` per includere `.obj`, `.gltf` e `.glb` con controlli interattivi di rotazione, illuminazione e wireframe.
- **Supporto Markdown avanzato:** rendering ricco con sintassi GitHub Flavored Markdown (GFM), diagrammi Mermaid integrati e tabelle formattate.
- **Anteprima file di archivio (.zip, .7z, .tar.gz):** ispezione ad albero del contenuto degli archivi compressi senza necessità di estrazione preventiva.

### 2. Ricerca e Indicizzazione
- **Supporto query avanzate e operatori logici:** estensione della sintassi SQLite FTS5 per supportare filtri di dimensione (`size:>100MB`), estensione combinata (`ext:pdf,docx`) e data di modifica (`modified:today`).
- **Vacuuming e compattazione incrementale:** ottimizzazione automatica in background delle pagine del database SQLite per ridurre ulteriormente l'occupazione su disco.
- **Filtri di ricerca personalizzati:** possibilità per l'utente di creare schede di filtro personalizzate oltre alle categorie predefinite (Applicazioni, Documenti, Immagini, Audio, Video).

### 3. Strumenti di Produttività e AI Locale
- **OCR Locale (Riconoscimento Ottico dei Caratteri):** estrazione istantanea di testo da immagini e schermate tramite Windows.Media.Ocr o motore ONNX ultraleggero, con copia rapida negli appunti.
- **Estensione Modelli Neurali:** supporto a esecuzione su NPU dedicate (Windows Copilot+ PC) tramite DirectML per ridurre ulteriormente il tempo di inferenza per la rimozione dello sfondo.

### 4. Esperienza Utente e Sistema
- **Materiali Avanzati Windows 11:** esplorazione di supporto nativo per Windows 11 Mica / Acrylic tramite API DWM di composizione per coerenza estetica col sistema operativo.
- **Pacchetti e Distribuzione:** submission del pacchetto installer su Microsoft Community Package Manager (`winget`).
- **Localizzazioni aggiuntive:** aggiunta graduale di nuove lingue della community (spagnolo, francese, tedesco).

### 5. Stato delle Voci di Briefing (10.11.2026)
- **Rotazione Manuale QuickLook (Foto e Video):** [COMPLETATO 10.11.2026] Tasto dedicato ⟳ nell'header per rotazione oraria di 90° a scatto (0°→90°→180°→270°→0°) con adattamento dinamico dell'aspect ratio e delle dimensioni della finestra intera per azzerare le bande nere laterali/superiori. Nessuna riscrittura del file su disco (view-only), parità bilingue IT/EN e reset a ogni nuovo file.
- **Riorganizzazione Impostazioni in tre sezioni (Generali / QuickLook / Spot):** [COMPLETATO 10.11.2026] Riorganizzazione completa della UI in 3 schede tematiche chiare (Generali, QuickLook, Spot). Conservazione totale delle chiavi di configurazione in settings.json e SchemaVersion: 4 (nessun breaking change né migrazione necessaria), catalogo di ricerca impostazioni (SettingsSearchCatalog) aggiornato con collegamenti diretti a ciascuna scheda, parità 100% IT/EN nei dizionari di localizzazione.
- **QuickLook nei selettori Apri/Allega di altre applicazioni:** [COMPLETATO 10.11.2026] Implementato `FileDialogResolver` basato su Microsoft UI Automation nativo. Intercetta i dialoghi `#32770` e FileDialog in processi terzi (WhatsApp, Chrome, Blocco Note, Office, Discord), risolve il percorso cartella dalla barra strumenti dell'indirizzo e l'elemento selezionato nella vista file, senza iniezione di DLL e senza rischio di falsi allarmi antivirus.
- **QuickLook sempre in primo piano (Topmost):** [COMPLETATO 10.11.2026] Rimosso `Topmost="True"` statico da XAML. Attivato comportamento contestuale intelligente in `PreviewWindow.xaml.cs`: `Topmost = true` all'apertura (`ShowPreviewForFileAsync`) e su `OnActivated`; rilascio immediato `Topmost = false` su `OnDeactivated`. Le finestre terze su cui l'utente fa clic salgono in primo piano in modo fluido e naturale senza dover chiudere l'anteprima. Aggiunta inoltre un'opzione esplicita nel tab QuickLook delle Impostazioni ("Mantieni la finestra sempre in primo piano" / `QuickLookAlwaysOnTop`) per consentire all'utente di forzare il blocco permanente in primo piano se preferito.