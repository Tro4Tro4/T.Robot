# CLAUDE.md - T.Robot

Guida per Claude Code. Le regole di progetto vincolanti sono in `AGENTS.md`, importato qui sotto: questo file non le ripete, aggiunge solo orientamento rapido e note specifiche per Claude Code.

@AGENTS.md

## Lettura prima di lavorare

1. `PROJECT_CONTEXT.md` (root): analisi verificata sul codice al 31 ottobre 2026, con aggiornamenti in testa. Le sezioni sotto gli aggiornamenti sono in parte uno snapshot storico.
2. Il documento della milestone o del rilascio pertinente in `docs/` (`MILESTONE_NN.md`, `MENU_PROPOSAL_STAY.md`, `MEMBER_ASSOCIATIONS_RELEASE.md`, `AZURE_CLOUD.md`).
3. `docs/DECISIONS.md`: registro cronologico, leggere anche le voci successive perche conserva stati intermedi.

`README.md`, `docs/PROJECT_CONTEXT.md` e `gateway/TRobot.WhatsApp/README.md` contengono limitazioni storiche superate: non usarli come descrizione dello stato corrente.

## Lingua e stile

- Rispondere e documentare in italiano. Mai il carattere U+2014: usare sempre `-`.
- Nomi: `T.Robot` (pubblico), `TRobot.*` (namespace/progetti), `TRobot.sln`, `trobot.db`.
- C# `net10.0`, nullable e implicit usings attivi, namespace per file, servizi async con `CancellationToken`, DI. Importi `decimal`, timestamp UTC, `TimeProvider` nei test dove previsto.

## Architettura in breve

```
src/TRobot.Domain          entita e invarianti, nessuna dipendenza da MVC/EF/Baileys
src/TRobot.Application     servizi, regole, autorizzazioni, contratti store (per modulo: WhatsApp, Menu, Lodging, Attendance, ...)
src/TRobot.Infrastructure  EF Core SQLite + Identity, store, client HTTP gateway, Persistence/Migrations
src/TRobot.Web             MVC/Razor per soli organizzatori, Program.cs, callback interno WhatsApp, worker scheduler locale
gateway/TRobot.WhatsApp    processo Node ESM separato (Baileys non ufficiale), test con trasporti finti
tests/TRobot.Tests         xUnit: dominio, servizi, SQLite, migrazioni, pipeline HTTP
tools/                     strumenti monouso (seeder, transfer, cleanup) con README: non passaggi ordinari
deploy/oracle, deploy/azure  servizi, backup, recovery; molti file "oracle" sono riusati su Azure
```

Dipendenze: Web -> Application + Infrastructure; Infrastructure -> Application + Domain; Application -> Domain.

Flusso WhatsApp: `transport.js` -> filtri `gateway.js` -> `http.js` -> `POST /internal/whatsapp/incoming` (`WhatsAppIncomingController`, loopback + bearer) -> `WhatsAppService` (Application) -> store Infrastructure. I controller non scrivono mai tabelle direttamente: tutto passa dai servizi Application con l'identita autenticata.

Moduli portale Tasks/Menu/Polls/Games disattivati per default (`PortalModules` in `appsettings.json`): dati e test esistono, ma non riattivarli senza richiesta.

## Comandi

```bash
dotnet restore TRobot.sln
dotnet tool restore                      # dotnet-ef 10.0.6
dotnet build TRobot.sln --no-restore
dotnet test TRobot.sln --no-build --no-restore
dotnet ef migrations has-pending-model-changes --project src/TRobot.Infrastructure --startup-project src/TRobot.Infrastructure --no-build

# gateway (Node 24, engines ">=24 <25")
cd gateway/TRobot.WhatsApp && npm ci --no-audit --no-fund && npm test
```

Nuova migrazione: `dotnet ef migrations add <Nome> --project src/TRobot.Infrastructure`, versionando migrazione e snapshot. Mai `EnsureCreated`, mai migrazioni automatiche all'avvio. Per modifiche allo schema aggiungere test di upgrade sul modello di `tests/TRobot.Tests/*MigrationTests.cs`.

Avvio locale (solo Development, loopback 5238): `dotnet ef database update --project src/TRobot.Infrastructure`, poi `dotnet run --project src/TRobot.Web --launch-profile http -- --initialize-demo` una sola volta su DB nuovo. Non cancellare il DB per ripetere il seed; `--initialize-identity` serve solo a demo legacy.

## Ambiente cloud di Claude Code

Verificato il 7 novembre 2026 in questo container: `dotnet` **non installato**, Node disponibile e la 22 (fuori da `engines` del gateway). Prima di dichiarare build o test riusciti:

- controllare `dotnet --version` / `node --version`; se mancano, dirlo esplicitamente e non riportare risultati non eseguiti;
- non installare runtime globali senza necessita; gli script `scripts/*.ps1` sono pensati per Windows/PowerShell.

Le verifiche numeriche dei documenti (es. 663 test .NET al 5 novembre 2026) sono storiche: riportarle come tali.

## Cosa non fare mai senza richiesta esplicita

- Collegare, avviare o inviare messaggi con il gateway WhatsApp reale; nessun messaggio di prova automatico.
- Toccare Azure (VM `rg-trobot-host`), DB operativo, sessione WhatsApp o riattivare copie Oracle/PC: Azure e l'unica fonte operativa.
- Reinserire lo scenario sintetico rimosso nel DB operativo.
- Versionare `.local/`, credenziali, token, numeri reali, QR, sessioni, journal o file `*.db`.
- Pubblicare saldi o dati personali individuali nel gruppo; aggiungere DM oltre `!proponi` e `!preferenze`.
- Riattivare log di payload Baileys/libsignal per diagnosi.

## Flusso di lavoro con Claude Code

- La skill `brainstorming` citata in `AGENTS.md` non e installata in Claude Code: sostituirla con una breve fase di disegno (obiettivo, vincoli, opzioni, scelta) prima di implementare, senza attendere approvazioni intermedie (modalita autonoma). Esempio di spec: `docs/superpowers/specs/2026-10-31-meal-preferences-design.md`.
- Default non ancora approvati vanno in `docs/DECISIONS.md`.
- Codice significativo: implementatore -> build/test -> revisore distinto in sola lettura (subagente) -> APPROVED. Modifiche banali: intervento diretto e verifica automatica.
- Lavorare sul branch indicato dalla sessione, commit piccoli con messaggi descrittivi in italiano.
- Riepilogo finale: file toccati, comandi eseguiti, esito test (o motivo per cui non sono stati eseguiti), prossimo passo.
