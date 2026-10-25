# Building & Packaging AeroSpot

Questo documento descrive i requisiti, i comandi di compilazione, la procedura di packaging e l'architettura dei prerequisiti per **AeroSpot**.

---

## 1. Requisiti di Sistema per lo Sviluppo

- **Sistema Operativo:** Windows 10 (versione 19041 o successiva) o Windows 11 (consigliato 23H2+).
- **Architettura:** `win-x64`.
- **SDK Richiesto:** [.NET 10.0 SDK](https://dotnet.microsoft.com/download) (verificato con `10.0.402` o superiore).
- **Shell:** PowerShell 7 o Windows PowerShell 5.1.
- **Strumenti opzionali:** Visual Studio 2026 / Visual Studio Code con estensione C# Dev Kit.

---

## 2. Compilazione Rapida e Test

Dalla directory radice del repository:

### Ripristino e Compilazione Debug / Release
```powershell
# Ripristino pacchetti e dipendenze
dotnet restore AeroSpot.sln

# Compilazione in modalità Release
dotnet build -c Release AeroSpot.sln
```

### Esecuzione della Suite di Test Unitari e di Integrazione
```powershell
dotnet test -c Release AeroSpot.sln --no-restore
```
*Tutti i test devono passare con 0 errori e 0 avvisi.*

---

## 3. Pubblicazione Automatica (Release Packaging)

AeroSpot viene distribuito in modalità **Self-Contained (Modalità A)**:
- Il runtime .NET 10 completo è incluso all'interno della cartella applicativa.
- **Nessun prerequisito** da scaricare o installare sul sistema dell'utente finale.
- Nessuna richiesta di privilegi di amministratore.
- **Runtime Condiviso:** sia `AeroSpot.Host.exe` che `AeroSpot.Worker.exe` risiedono nella medesima directory, condividendo gli assembly del CoreCLR e di WPF una sola volta senza duplicazioni di spazio su disco.

### Esecuzione dello Script di Pubblicazione Ufficiale

È disponibile lo script automatico:
```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1
```

### Parametri Supportati dallo Script:
| Parametro | Predefinito | Descrizione |
|---|---|---|
| `-Configuration` | `Release` | Configurazione di compilazione (`Release` o `Debug`). |
| `-Runtime` | `win-x64` | Runtime Identifier target per l'architettura Windows a 64 bit. |
| `-ReadyToRun` | `$true` | Precompilazione AOT (ReadyToRun) per eliminare i picchi di latenza JIT al primo avvio. |
| `-OutDir` | `bin\Release\publish` | Cartella di output dei binari finali. |
| `-SkipTests` | `$false` | Ignora l'esecuzione preventiva dei test unitari (sconsigliato). |
| `-CreateZip` | `$true` | Comprime automaticamente l'output nello zip pronto per GitHub Releases. |

---

## 4. Benchmark e Scelta ReadyToRun

Sono stati misurati rigorosamente i due approcci di pubblicazione self-contained su Windows 11 Build 26300 con 10 esecuzioni indipendenti a freddo dell'applicazione completa (inizializzazione CoreCLR + parsing XAML di MainWindow e PreviewWindow):

| Metrica | Senza ReadyToRun (JIT puro) | Con ReadyToRun (AOT + JIT) | Differenza |
|---|---|---|---|
| **Spazio su disco scompattato** | 166.64 MB | 197.20 MB | +30.56 MB |
| **Dimensione Archivio ZIP** | 67.66 MB | 76.33 MB | +8.67 MB |
| **Avvio a freddo (Min)** | 1015 ms | 1013 ms | -2 ms |
| **Avvio a freddo (Mediana)** | 1019 ms | 1015 ms | -4 ms |
| **Avvio a freddo (Max / 1° avvio)** | **4080 ms** | **1041 ms** | **-3039 ms (-74%)** |

**Conclusioni e Scelta Operativa:**
Senza ReadyToRun, il primissimo avvio da disco sconta un picco JIT di oltre 4 secondi. Con ReadyToRun, la latenza di avvio iniziale scende a 1.04 secondi con una deviazione standard quasi nulla. L'aumento della dimensione dell'archivio zip di appena ~8.6 MB ripaga ampiamente l'esperienza utente istantanea tipica di AeroSpot. Lo script di compilazione predefinisce pertanto `ReadyToRun = $true`.

---

## 5. Modalità Portable e File Chiave

- **Modalità Portable:** per utilizzare AeroSpot in modalità 100% isolata (es. su chiavetta USB o cartella sincronizzata), è sufficiente creare un file vuoto denominato `portable.txt` nella stessa cartella degli eseguibili. AeroSpot memorizzerà database SQLite, impostazioni e log esclusivamente nella sottocartella `Data\`.
- **Pulizia del Registro:** eseguendo `AeroSpot.Host.exe --cleanup`, qualsiasi voce eventualmente registrata all'avvio di Windows viene rimossa istantaneamente prima di cancellare la cartella.
