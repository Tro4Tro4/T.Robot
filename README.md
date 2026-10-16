# T.Robot

Bot per organizzare Capodanno 2027 con sito riservato agli organizzatori. Milestone 1: soluzione .NET 10, modello iniziale ed EF Core SQLite. Il sito è solo un prototipo locale; WhatsApp, AI, autenticazione, pagamenti e dashboard non sono implementati.

## Struttura

- `TRobot.sln`: Web MVC → Application + Infrastructure; Application → Domain; Infrastructure → Application + Domain. Domain senza dipendenze esterne.
- `src/TRobot.Domain`: Event, Person, EventParticipant, EventOrganizer e validazioni iniziali.
- `src/TRobot.Infrastructure/Persistence`: DbContext, mapping, factory per CLI e migrazioni.
- `tests/TRobot.Tests`: xUnit, regole di dominio e integrazione SQLite con migrazioni reali.
- [Contesto](docs/PROJECT_CONTEXT.md) e [decisioni](docs/DECISIONS.md).

## Preparazione e database

Da PowerShell nella radice, con SDK 10.0.401 (o patch compatibile) installato:

```powershell
# Opzionale: usa cache scrivibili interne al progetto in ambienti limitati.
. ./scripts/Use-LocalDotnet.ps1
dotnet restore TRobot.sln
dotnet tool restore
dotnet build TRobot.sln --no-restore
dotnet ef database update --project src/TRobot.Infrastructure
dotnet test TRobot.sln --no-build --no-restore
```

Il comando EF crea `src/TRobot.Web/trobot.db` e applica la migrazione iniziale. Ripeterlo non duplica lo schema. La factory individua la radice della soluzione; il comando documentato usa Infrastructure anche come startup project, senza avviare Web. Non usare `EnsureCreated` in alternativa alle migrazioni.

La connection string Web è `ConnectionStrings:TRobot` in `appsettings.Development.json`; i percorsi relativi sono risolti rispetto alla cartella Web. La factory CLI usa il percorso predefinito; per un database diverso passare esplicitamente `--connection "Data Source=C:/percorso/trobot.db;Foreign Keys=True"` a `database update` e configurare la Web con lo stesso percorso. Non inserire credenziali nel repository.

Per future variazioni di modello:

```powershell
dotnet ef migrations add NomeModifica --project src/TRobot.Infrastructure --output-dir Persistence/Migrations
dotnet ef database update --project src/TRobot.Infrastructure
```

Versionare migrazioni e snapshot, mai database o contenuti personali. Il database iniziale è vuoto; i test usano dati sintetici in memoria e non modificano quello locale.

## Avvio locale

```powershell
dotnet run --project src/TRobot.Web --launch-profile http --no-build
```

Aprire <http://localhost:5238>. Arrestare con `Ctrl+C`. Il prototipo ascolta solo su loopback e rifiuta ambienti diversi da Development. La pagina iniziale non interroga il DB: l'integrazione dello schema viene verificata nei test. Prima di qualsiasi deploy occorrono autenticazione reale, autorizzazione e revisione della configurazione di hosting; non pubblicare questa milestone su IIS.

## Verifiche e prossimo passo

I test verificano validazioni, default di partecipazione, timestamp UTC, applicazione ripetuta della migrazione, round trip delle quattro entità, identità WhatsApp facoltative/univoche, chiavi composte, riferimenti obbligatori, cancellazioni protette e vincoli sugli enum.

Verificato il 16 ottobre 2026: build con 0 errori e 0 avvisi; 20 test passati; migrazione applicata al database locale e ripetuta senza modifiche; `dotnet ef migrations has-pending-model-changes --project src/TRobot.Infrastructure --no-build` senza differenze. Smoke test MVC con HTTP 200 e nome pubblico corretto; listener verificati solo su `127.0.0.1` e `::1`, processo poi arrestato. L'avvio è stato verificato nel contesto utente ordinario: la sandbox non può decifrare le chiavi DPAPI del profilo Windows. Nessuna verifica di deploy, autenticazione o integrazioni esterne, che non rientrano nella milestone.

Milestone 2 proposta: ingresso locale simulato `!presenza si` → servizio Application validato e autorizzato, con storico/idempotenza → SQLite → conteggio in dashboard locale. Il gateway WhatsApp resta separato e richiede una richiesta esplicita.
