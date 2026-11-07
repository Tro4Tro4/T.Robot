# T.Robot - Contesto per un agente di coding

**Aggiornamento 5 novembre 2026:** Web `member-associations-20261105-arm64` rilasciato su Azure: nel pannello WhatsApp i membri gia associati sono raggruppati in fondo ed evidenziati. Solo Web, gateway M34 invariato, nessuna migrazione; dettagli in `docs/MEMBER_ASSOCIATIONS_RELEASE.md`.

**Aggiornamento 1 novembre 2026:** controllo `!proponi` rilasciato su Azure nel Web `proposal-stay-20261101-arm64`: giorno nel soggiorno personale confermato, verifica alla scelta e alla conferma. Gateway M34 invariato, nessuna migrazione; backup e conservazione dati/identita verificati. 662/662 test .NET e review indipendenti APPROVED. Dettagli in `docs/MENU_PROPOSAL_STAY.md`.

**Aggiornamento operativo M34, 31 ottobre 2026:** Web e gateway rilasciati su Azure in `m34-20261031-arm64`. Consultazione e preferenze multiple tramite `!preferenze` dal gruppo al dialogo privato, scelte distinte dei figli collegati, preparazione dell'elenco nel pannello e risultati adulti/bambini. Backup privato sulla VM, rehearsal e migrazione con righe precedenti conservate, stessa identita e gruppo ripristinati. 651/651 test .NET, 60/60 Node, 18 controlli HTTP/navigazione, review indipendenti APPROVED, 48 controlli browser pubblici e quattro senza JavaScript positivi. Raccolta rimasta aperta, nessun ciclo o voto dimostrativo creato. M33 dal telefono e salvataggio confermati dall'utente; voto M34 dal telefono ancora da collaudare dopo preparazione e apertura. Dettagli in `docs/MILESTONE_34.md`. Le sezioni seguenti conservano l'analisi precedente a M34 e vanno lette come snapshot storico.

Analisi del repository locale al 31 ottobre 2026, commit `4db2767`. Il codice applicativo non e stato modificato. Stato Git iniziale pulito. SDK rilevato: 10.0.401; Node nel PATH: 26.13.0, fuori dall'intervallo richiesto dal gateway. E presente anche la cartella del runtime portabile Node 24.22.0; eseguibilita da verificare.

Questo documento distingue codice presente, verifiche storiche documentate e verifiche ancora necessarie. Non sono stati avviati servizi, eseguite migrazioni, letti database operativi o inviati messaggi. Build e test non rieseguiti durante questa analisi.

## 1. Scopo e confini del prodotto

**T.Robot** organizza un evento di Capodanno attraverso un gruppo WhatsApp esistente: partecipazioni, soggiorni, nuclei con bambini, proposte pasti e comunicazioni operative. Sono presenti anche moduli per alloggi, attivita, menu, sondaggi, contabilita e giochi volontari. Il tono previsto e ironico senza essere offensivo; non deve commentare indiscriminatamente la chat.

I partecipanti usano WhatsApp. Il sito ASP.NET Core MVC e riservato agli organizzatori autenticati e assegnati all'evento: configurazione, anagrafiche, dashboard, storico, approvazioni e gestione. Non esistono account web da creare per ogni partecipante.

La regola originaria vietava i messaggi privati. **M33 introduce una sola eccezione**: `!proponi` nel gruppo autorizzato avvia il dialogo privato del mittente associato per raccogliere giorno, pasto, piatto e disponibilita facoltativa a cucinare. Solo `!conferma` salva la proposta; `!annulla` interrompe. Scadenza assoluta di cinque minuti, senza estensioni o messaggi automatici alla scadenza. Una proposta non crea un incarico o il menu definitivo.

Riferimenti: `AGENTS.md`, `docs/PROJECT_CONTEXT.md`, `docs/MILESTONE_33.md`. Il documento sotto `docs/` contiene anche disegni iniziali e stati storici: non leggerli come descrizione uniforme della versione corrente. Anche `README.md` e `gateway/TRobot.WhatsApp/README.md` contengono limitazioni storiche superate, fra cui assenza di Production e DM.

## 2. Stack, versioni e cartelle

| Componente | Versione o scelta verificata | Fonte |
| --- | --- | --- |
| SDK .NET | 10.0.401, roll-forward alle patch compatibili | `global.json` |
| Applicazione | `net10.0`, C#, ASP.NET Core MVC/Razor, Identity con cookie | `src/TRobot.Web/TRobot.Web.csproj`, `src/TRobot.Web/Program.cs` |
| Persistenza | EF Core SQLite, Identity EF e CLI EF 10.0.6 | `src/TRobot.Infrastructure/TRobot.Infrastructure.csproj`, `dotnet-tools.json` |
| SQLite nativo | SQLitePCLRaw.bundle_e_sqlite3 3.0.5 | `src/TRobot.Infrastructure/TRobot.Infrastructure.csproj` |
| Test .NET | xUnit 2.9.3, runner 3.1.4, Test SDK 17.14.1, MVC.Testing 10.0.6, coverlet 6.0.4 | `tests/TRobot.Tests/TRobot.Tests.csproj` |
| Gateway | JavaScript ESM, Node >=24 e <25; pacchetto interno 0.1.0 | `gateway/TRobot.WhatsApp/package.json` |
| Dipendenze gateway | Baileys 7.0.0-rc14, qrcode 1.5.4, pino 10.4.0 | `gateway/TRobot.WhatsApp/package.json`, `gateway/TRobot.WhatsApp/package-lock.json` |
| Interfaccia | MVC, Bootstrap 5.3.3, CSS e JavaScript locali | `src/TRobot.Web/Views/Shared/_Layout.cshtml`, `src/TRobot.Web/wwwroot/lib/bootstrap/dist/css/bootstrap.css` |
| Hosting documentato | Ubuntu ARM64, ASP.NET Core runtime 10.0.13, Node 24.22.0, Caddy 2.11.7 | `docs/AZURE_CLOUD.md`; versioni live da verificare |

Struttura:

- `TRobot.sln`: soluzione principale.
- `src/TRobot.Domain/`: entita, invarianti, revisioni, ricevute e sessioni. Nessuna dipendenza da MVC, EF o Baileys.
- `src/TRobot.Application/`: servizi applicativi, autorizzazioni, workflow e contratti degli store. Sottocartelle per Attendance, WhatsApp, Menu, Lodging, Messaging, Accounting e altri moduli.
- `src/TRobot.Infrastructure/`: store EF, Identity e client HTTP verso il gateway; migrazioni in `Persistence/Migrations/`.
- `src/TRobot.Web/`: composition root `Program.cs`, controller MVC/API, modelli, viste, sicurezza, asset e worker locale.
- `gateway/TRobot.WhatsApp/src/`: host HTTP, trasporto Baileys, filtri, configurazione e journal; test in `gateway/TRobot.WhatsApp/test/`.
- `tests/TRobot.Tests/`: test di dominio, servizi, SQLite, migrazioni e pipeline HTTP.
- `scripts/`: preparazione SDK/cache e gateway Windows.
- `tools/TRobot.ScenarioSeeder/`, `tools/TRobot.WhatsAppTransfer/`, `tools/TRobot.ScenarioCleanup/`: strumenti manutentivi con propri README, non passaggi ordinari di avvio.
- `deploy/oracle/`, `deploy/azure/`: provisioning, servizi, backup, trasferimenti e verifiche operative. Diversi file Oracle sono riutilizzati dall'hosting Azure; il nome della cartella non identifica da solo il server operativo.
- `docs/`: decisioni, milestone e runbook. `.local/`: materiale privato e cache esclusi da Git; non e distribuita con il clone e viene popolata dai passaggi di preparazione pertinenti.

Dipendenze: Web -> Application e Infrastructure; Infrastructure -> Application e Domain; Application -> Domain. Il gateway e un processo distinto.

## 3. Installazione, configurazione, avvio e test

### Preparazione locale

Da PowerShell nella root, installare SDK compatibile con `global.json`. Per il gateway usare Node 24, non il Node 26 rilevato nel PATH. Non e necessario collegare WhatsApp per sviluppare o eseguire i test.

```powershell
# Facoltativo: cache SDK/NuGet dentro il workspace
. ./scripts/Use-LocalDotnet.ps1
dotnet restore TRobot.sln
dotnet tool restore
dotnet build TRobot.sln --no-restore
dotnet test TRobot.sln --no-build --no-restore
```

Su un database locale nuovo applicare le migrazioni, poi inizializzare la demo una sola volta:

```powershell
dotnet ef database update --project src/TRobot.Infrastructure
dotnet run --project src/TRobot.Web --launch-profile http --no-build -- --initialize-demo
dotnet run --project src/TRobot.Web --launch-profile http --no-build
```

Il sito Development ascolta su loopback, porta 5238. Le credenziali casuali iniziali sono scritte in `.local/identity-admin.txt`, da custodire senza copiarle nei documenti. Il bootstrap non parte automaticamente. Su una demo legacy gia popolata esiste `--initialize-identity`, che richiede il precedente `.local/local-access.json`: non usarlo come inizializzazione generica. Non cancellare il database per ripetere il seed.

La factory EF `src/TRobot.Infrastructure/Persistence/TRobotDbContextFactory.cs` cerca `TRobot.sln` e punta al DB locale `src/TRobot.Web/trobot.db`; non carica automaticamente la configurazione Web. Per un database differente occorre un override esplicito della connessione CLI e una configurazione Web coerente. Il Web risolve i percorsi relativi rispetto alla propria content root. L'avvio normale non applica automaticamente le migrazioni. Non sostituire le migrazioni con `EnsureCreated`.

### Configurazione: solo nomi delle variabili

Fonti: `src/TRobot.Web/appsettings.json`, `src/TRobot.Web/appsettings.Development.json`, `src/TRobot.Web/Properties/launchSettings.json`, `src/TRobot.Web/Program.cs`, `src/TRobot.Web/Security/HostingProductionConfiguration.cs`, `src/TRobot.Web/Security/WhatsAppHostingConfiguration.cs`, `gateway/TRobot.WhatsApp/src/config.js` e `gateway/TRobot.WhatsApp/src/index.js`.

Nomi per ambiente e override della configurazione ASP.NET Core, senza valori:

```text
ASPNETCORE_ENVIRONMENT
DOTNET_ENVIRONMENT
AllowedHosts
ConnectionStrings__TRobot
Hosting__Enabled
Hosting__ListenAddress
Hosting__HttpsPort
Hosting__CertificatePath
Hosting__CertificatePassword
Hosting__DataProtectionKeysPath
WhatsAppGateway__Enabled
WhatsAppGateway__ProductionEnabled
WhatsAppGateway__BaseUrl
WhatsAppGateway__Token
LocalScheduler__Enabled
PortalModules__Enabled__Tasks
PortalModules__Enabled__Menu
PortalModules__Enabled__Polls
PortalModules__Enabled__Games
PortalModules__Enabled__Messages
PortalModules__Enabled__Simulator
Logging__LogLevel__Default
Logging__LogLevel__Microsoft.AspNetCore
```

Variabili lette direttamente dal processo Node:

```text
NODE_ENV
TROBOT_GATEWAY_CONFIG
```

Variabili predisposte o usate dagli script locali:

```text
DOTNET_CLI_HOME
NUGET_PACKAGES
DOTNET_CLI_TELEMETRY_OPTOUT
PATH
```

Il gateway Node legge un file JSON privato, non traduce automaticamente gli override ASP.NET Core in configurazione Node. Il Web Development carica anche `.local/whatsapp/gateway.json` dopo le fonti standard: controllare la precedenza prima di tentare un override. Il file, il materiale di autenticazione e il journal non vanno versionati. In Production i percorsi e i permessi del file, della CA e dello stato sono validati rigidamente; consultare `gateway/TRobot.WhatsApp/src/config.js`.

### Gateway locale, solo quando richiesto

`scripts/Prepare-WhatsApp.ps1` scarica e verifica Node portabile, prepara la configurazione privata e installa le dipendenze con `npm ci`. **Abilita la configurazione locale**: non e un passo necessario per il solo Web e non va eseguito sul PC operativo per questa analisi.

```powershell
./scripts/Prepare-WhatsApp.ps1
./gateway/TRobot.WhatsApp/start-gateway.ps1
```

Lo script di avvio sceglie un unico runtime portabile oppure richiede `-NodePath`. Il listener HTTP del gateway usa loopback, porta 5240; il processo non apre automaticamente la connessione WhatsApp. QR, collegamento, selezione gruppo e associazioni sono amministrati dal sito con conferme. Il gateway operativo e su Azure: non avviare una seconda sessione locale sulla stessa identita. Procedura autorizzata e limiti in `docs/WHATSAPP_LOCAL.md`, da leggere insieme a M30 e M33.

### Production

Non usare i comandi demo o di migrazione locale sul server. Il Web Production richiede attivazione esplicita, host ammessi, certificato valido, SQLite gia esistente in lettura/scrittura e chiavi Data Protection persistenti esterne agli asset pubblici. Kestrel espone HTTPS; Caddy gestisce il TLS pubblico. Lo scheduler locale non viene registrato in Production. Il gateway Production ha un consenso separato e recovery della sessione esistente.

Per installazione o rilascio usare `docs/AZURE_CLOUD.md`, `docs/MILESTONE_29.md`, `docs/MILESTONE_30.md`, `docs/MILESTONE_33.md` e gli script pertinenti sotto `deploy/`. Occorrono backup consistente, rehearsal su copia corrente e verifica di conservazione dei dati; un semplice `dotnet publish` non completa un rilascio sicuro. Gli script privati menzionati nei runbook sotto `.local/` potrebbero mancare in un clone: disponibilita da verificare.

## 4. Architettura e flussi

### Ingresso WhatsApp e callback

1. `gateway/TRobot.WhatsApp/src/transport.js` apre Baileys solo su richiesta esplicita di connessione. Riceve `messages.upsert` di tipo `notify`, senza sincronizzare lo storico completo.
2. `gateway/TRobot.WhatsApp/src/gateway.js` filtra gruppo attivo, mittente, canale, timestamp, duplicati e frequenza; serializza l'elaborazione. I DM sono ammessi soltanto per il percorso previsto da M33.
3. `gateway/TRobot.WhatsApp/src/http.js` invia il payload al callback interno `POST /internal/whatsapp/incoming`. Non e un webhook pubblico Meta/WhatsApp Cloud API. In locale usa HTTP loopback; sul server HTTPS con CA esplicita e verifica TLS.
4. `src/TRobot.Web/Controllers/WhatsAppIncomingController.cs` controlla abilitazione, IP loopback, bearer con confronto a tempo costante, dimensione e rate limit. `AllowAnonymous` evita l'obbligo di login Identity per questo endpoint; non disabilita il middleware di autenticazione e non sostituisce i controlli tecnici eseguiti nell'azione.
5. `src/TRobot.Application/WhatsApp/WhatsAppService.cs` verifica collegamento, attivatore, gruppo e alias del mittente, poi instrada alle parti Attendance, Information, Operational e MealProposal. I controlli vengono ripetuti nella transazione che modifica dati.
6. Gli store Infrastructure salvano mutazioni, revisioni, ricevute e audit. Il callback restituisce un esito e l'eventuale risposta testuale; prima dell'invio del testo il gateway scrive il journal. Le reazioni emoji seguono un percorso separato senza journal di invio. La dashboard legge gli stessi dati attraverso i servizi.

Contratti in `src/TRobot.Application/WhatsApp/Contracts.cs`; implementazione callback/client in `gateway/TRobot.WhatsApp/src/http.js` e `src/TRobot.Infrastructure/WhatsApp/WhatsAppGatewayClient.cs`.

### Uscite, pannello e persistenza

- Il pannello invoca servizi Application usando l'identita autenticata; non scrive direttamente le tabelle dai controller. `src/TRobot.Application/Local/LocalServices.cs` centralizza operazioni e controlli comuni.
- Nuovi invii operativi reali passano da bozza, anteprima del gruppo, conferma e API gateway `/send`. Il destinatario deve corrispondere al collegamento approvato. Un DM M33 deriva dal messaggio reale, non da un destinatario arbitrario inserito nel pannello.
- L'outbox simulata `OutboundMessage` e distinta dai messaggi WhatsApp reali. Non convertire vecchie code in invii reali. `src/TRobot.Application/Messaging/` e `src/TRobot.Web/Services/AutomaticSchedulerWorker.cs` gestiscono lo scheduler locale opt-in, legato ai permessi dell'attivatore.
- SQLite comprende dati applicativi e Identity. `src/TRobot.Infrastructure/Persistence/TRobotDbContext.cs` applica i mapping dell'assembly, anche per entita non elencate come proprieta `DbSet`.
- Migrazioni e snapshot sono in `src/TRobot.Infrastructure/Persistence/Migrations/`; l'ultima verificata nel repository (7 novembre 2026) e `20261031205555_AddMealPreferences.cs` (M34), che crea soltanto nuove tabelle per sessioni, cicli di voto, schede, modifiche, voci e fonti delle preferenze pasti (`MealPreferenceSessions`, `MealVotingCycles`, `MealPreferenceBallots`, `MealPreferenceChanges`, `MealVotingEntries`, `MealVotingSources`). La precedente `20261031133619_AddMealProposals.cs` (M33) aggiunge proposte confermate e sessioni provvisorie.
- Token, autenticazione Baileys e journal filesystem restano separati dal DB. Gli alias WhatsApp vengono associati esplicitamente a persone esistenti tramite hash; nomi visualizzati e identita tecniche non sono intercambiabili.

## 5. Convenzioni e verifiche

- Nome pubblico `T.Robot`, namespace/progetti `TRobot.*`, soluzione `TRobot.sln`, database `trobot.db`. Risposte e documentazione operative in italiano; usare il trattino ASCII, mai U+2014 nei nuovi testi.
- C# con nullable e implicit usings abilitati; namespace per file, servizi asincroni con `CancellationToken`, dipendenze via DI. Preservare l'indipendenza del Domain.
- Regole e autorizzazioni nei servizi, permessi per evento anche per l'amministratore, validazione server, conferme, revisioni contro pagine obsolete, ricevute idempotenti e audit atomico. Non bypassare i servizi per modificare dati.
- Importi `decimal`, timestamp UTC, fuso configurabile per evento; date locali e cambio ora richiedono attenzione. Test con `TimeProvider` dove previsto. Razor codifica il testo utente.
- Versionare migrazioni e snapshot. `.gitignore` esclude `.local/`, database SQLite, WAL/journal, bin/obj e node_modules. Non versionare configurazioni private, credenziali, QR o sessioni.
- Prima di sviluppare: leggere `AGENTS.md` e `docs/PROJECT_CONTEXT.md`; usare brainstorming e piccoli incrementi. I default nuovi vanno in `docs/DECISIONS.md`. Per codice significativo le istruzioni richiedono implementatore, build/test e revisore distinto; non applicato a questa analisi documentale.

Comandi di verifica, dalla root salvo dove indicato:

```powershell
dotnet build TRobot.sln --no-restore
dotnet test TRobot.sln --no-build --no-restore
dotnet ef migrations has-pending-model-changes --project src/TRobot.Infrastructure --startup-project src/TRobot.Infrastructure --no-build
```

Dalla cartella `gateway/TRobot.WhatsApp/`, con Node 24 selezionato:

```powershell
npm ci --no-audit --no-fund
npm test
```

`npm test` esegue `node --test`, con trasporti finti e senza sessione o invii WhatsApp. I test .NET usano dati sintetici e SQLite, incluse prove di upgrade e pipeline HTTP. Esempi: `tests/TRobot.Tests/MealProposalTests.cs`, `tests/TRobot.Tests/MealProposalMigrationTests.cs`, `tests/TRobot.Tests/MealProposalWebTests.cs`, `tests/TRobot.Tests/WhatsAppProductionConfigurationTests.cs`.

Test operativi Python disponibili in `deploy/oracle/test_backup.py` e `deploy/oracle/test_gateway_ops.py`; requisiti Linux e modalita di esecuzione da verificare prima dell'uso su Windows. Non e stata individuata una configurazione dedicata ESLint/Prettier o un comando lint nel package gateway. Non risultano file `.editorconfig` o workflow CI nell'inventario consultato; build, test e controllo EF sono i controlli documentati. Eventuali controlli esterni: da verificare.

## 6. Decisioni, motivazioni e alternative

| Decisione | Motivazione | Alternativa esclusa o rinviata | Fonte |
| --- | --- | --- | --- |
| Monolite modulare .NET con gateway Node separato | Mantenere il dominio indipendente dal trasporto e condividere servizi fra MVC e callback | Accoppiamento del Domain a MVC, EF o Baileys vietato; confronto con microservizi da verificare | `AGENTS.md`, `docs/PROJECT_CONTEXT.md`, riferimenti nei `.csproj` |
| SQLite iniziale con EF e migrazioni versionate | Persistenza contenuta e upgrade verificabili sul DB esistente | Database gestito separato per questo hosting, ricreazione dello schema al boot | `docs/DECISIONS.md`, `docs/AZURE_CLOUD.md` |
| Identity/cookie per organizzatori | Accessi per evento, revoche e invalidazione sessioni | Precedente codice locale condiviso sostituito; provider esterno ed email obbligatoria non adottati | `docs/MILESTONE_4.md`, `docs/MILESTONE_5.md`, `docs/DECISIONS.md` |
| Baileys sperimentale con numero dedicato | Supportare il gruppo WhatsApp esistente con identita esplicite | API ufficiale come garanzia di compatibilita con questo scenario, non dimostrata | `docs/MILESTONE_23.md` |
| Conferme, fingerprint e ricevute persistenti | Evitare salvataggi involontari, duplicati e approvazioni obsolete | Invii immediati, replay indiscriminato e promessa di exactly-once | `docs/MILESTONE_24.md`, `docs/MILESTONE_25.md`, `gateway/TRobot.WhatsApp/src/journal.js` |
| Scheduler solo locale e simulato | Non trasformare automazioni di prototipo in invii reali | Scheduler Production e conversione automatica delle vecchie code | `docs/MILESTONE_22.md`, `src/TRobot.Web/Program.cs` |
| Azure come unica fonte operativa | Trasferimento coerente e superamento dei blocchi Oracle | Copie PC/Oracle contemporaneamente attive; ritorno su backup obsoleto | `docs/AZURE_CLOUD.md`, `docs/DECISIONS.md` |
| Quattro moduli sospesi nel portale, dati conservati | Richiesta esplicita di ridurre le funzioni esposte senza perdere storico | Cancellare moduli/dati o disattivare implicitamente i comandi WhatsApp | `docs/MILESTONE_32.md`, `src/TRobot.Web/Security/PortalModules.cs` |
| Proposte pasti separate dal menu | Raccogliere idee senza creare incarichi; eccezione DM limitata al mittente | Menu definitivo automatico o DM generici | `docs/MILESTONE_33.md` |
| Date M33 entro 31 giorni prima o dopo la data evento | Riutilizzare il calendario dei soggiorni: l'evento non ha data finale | Nessuna alternativa esplicitamente scartata nei documenti M33; eventuale intervallo evento da verificare | `docs/MILESTONE_33.md`, `docs/DECISIONS.md` |
| AI ibrida solo come disegno futuro | Parsing e autorizzazioni deterministici preservano il controllo | AI esterna attiva o scrittura diretta AI sul DB | `docs/PROJECT_CONTEXT.md`, `docs/ROADMAP_LOCAL.md` |

La colonna delle alternative distingue sostituzioni documentate, vincoli e punti da verificare: un vincolo non prova che sia stata svolta una comparazione fra soluzioni. Il registro `docs/DECISIONS.md` e cronologico e conserva stati intermedi: leggere anche gli aggiornamenti successivi.

## 7. Stato, limiti, bug e priorita

### Funzioni presenti

- Eventi, partecipanti, presenze, soggiorni guidati, nuclei e legame bambini/accompagnatore, rinomina nuclei, storico, account e permessi.
- Alloggi con appartamenti, camere/capienza, assegnazioni, piano bozza/definitivo e schede stampabili private: `docs/MILESTONE_31.md`.
- Comandi reali implementati: presenze e modifica soggiorno, riepiloghi anche giornalieri, aiuto, elenchi approvati menu/attivita e impegni con conferma, oltre a M33. Routing in `src/TRobot.Application/WhatsApp/WhatsAppService.cs` e relative parti. L'aiuto reale non e un catalogo completo di tutte le funzioni implementate.
- Menu, attivita, sondaggi e giochi hanno implementazioni e test, ma sono **sospesi nel portale per default**: tab disabilitati, GET/POST diretti 404 e relativi comandi simulati bloccati. I comandi WhatsApp operativi sono indipendenti da tale filtro. Proposte pasti e un archivio distinto e attivo.
- Contabilita locale e simulatore/outbox presenti; nessun trasferimento di denaro. Quote dinamiche, non un rendiconto congelato: `src/TRobot.Application/Accounting/ShareCalculator.cs`, `docs/ROADMAP_LOCAL.md`.

### Stato operativo e verifica disponibile

`docs/MILESTONE_33.md` dichiara Web e gateway rilasciati su Azure nella release `m33-20261031-arm64`, migrazione conservativa riuscita, gruppo originale recuperato e collaudo pubblico positivo. Riporta build senza errori/avvisi, **634/634 test .NET**, **56 test Node**, review APPROVED, 44 controlli browser e tre percorsi senza JavaScript positivi. Sono risultati storici documentati, non eseguiti in questa analisi. Stato live, commit effettivamente in esecuzione e servizi correnti: **da verificare**.

Secondo `AGENTS.md` e `docs/AZURE_CLOUD.md`, Oracle e PC sono fermi/disabilitati come copie operative; stato live da verificare. Lo scenario sintetico e stato rimosso da Azure su richiesta: non ricrearlo nel DB operativo. `tools/TRobot.ScenarioSeeder/` e il vecchio README non autorizzano a reinserirlo. `docs/SCENARIO_REMOVAL.md` documenta la rimozione; Revoca evento nel portale revoca l'assegnazione, non elimina l'evento.

### Incompleto o da verificare

- M33: ricezione del primo DM e completamento reale dal telefono ancora da verificare. Test e recovery del gateway non equivalgono a questo collaudo.
- M33 non include modifica/ritiro di proposte confermate, apertura/chiusura raccolta, decisione comune, menu definitivo o lista della spesa.
- AI esterna non integrata; i comandi e i moduli previsti nei disegni iniziali non vanno considerati tutti attivi su WhatsApp.
- Recupero password, MFA, eliminazione account e promozioni globali via Web risultano non disponibili secondo `README.md`; roadmap aggiornata da verificare.
- Copertura su telefono fisico/Safari oltre alle prove dichiarate: da verificare. I collaudi mobile descritti sono soprattutto Chromium con viewport/touch emulati.

### Problemi noti

- Causa dei precedenti blocchi della VM Oracle: non accertata; non attribuirla automaticamente a OOM o al gateway. `docs/MILESTONE_30.md` mantiene aperta la diagnosi storica.
- Primo rilascio M33 fallito nel recovery per gruppo filesystem errato su `node_modules`: corretto nella nuova release, con ripristino e secondo tentativo riusciti. Preservare proprieta e permessi nei futuri pacchetti (`docs/MILESTONE_33.md`).
- Node nel PATH locale non rispetta `engines`; selezionare Node 24 per installazione e test gateway.
- Documentazione con stati contraddittori o superati. Questa analisi non ha accertato altri bug funzionali riproducibili; eventuali issue esterne: da verificare.

Priorita proposte per il prossimo agente, da concordare nel perimetro del nuovo task:

1. Verificare manualmente M33 dal telefono con l'utente: avvio dal gruppo, isolamento DM, annullamento, conferma e scadenza. Nessun invio automatico di prova.
2. Confermare stato Azure, backup/recovery e scadenze dei certificati con accesso autorizzato; proteggere l'unica fonte operativa.
3. Eseguire build/test con SDK e Node corretti prima di qualunque nuovo sviluppo; verificare modello EF e upgrade su copia sintetica.
4. Definire il prossimo incremento pasti: modifica/ritiro e gestione della raccolta, poi scelta comune/menu/spesa. Sono sviluppi successivi, non comportamento gia disponibile.
5. Allineare i documenti storici e valutare manutenzione dipendenze/autenticazione in task separati. In questa consegna viene creato soltanto questo file.

## 8. Punti delicati e trappole

- **Trasporto non ufficiale:** Baileys non e WhatsApp Cloud API. Disconnessioni, cambi protocollo e restrizioni dell'account restano possibili. Compatibilita e policy ufficiali attuali: da verificare prima di cambiare integrazione; nessuna nuova verifica online svolta qui.
- **Una sola identita operativa:** non avviare copie PC o Oracle obsolete. Un rollback dei binari non autorizza un rollback del DB; tornare ad altro host richiede export coerente da Azure.
- **Ripartenze:** il processo Node perde il gruppo ammesso al riavvio; in Production il recovery autorizzato ripristina la sessione e il gruppo verificati. `disconnect` conserva l'autenticazione: non sostituirlo con logout. Riferimenti: `gateway/TRobot.WhatsApp/src/transport.js`, `deploy/oracle/resume-gateway.py`, `deploy/azure/trobot-whatsapp-resume.conf`.
- **Identita PN/LID:** non dedurre corrispondenze dai nomi. Associazioni esplicite, alias e revisioni del collegamento fanno parte dell'autorizzazione e delle sessioni.
- **Nessun exactly-once esterno:** il journal persiste lo stato `pending` prima dell'invio; alla rilettura restituisce `uncertain` se trova `pending`, senza riscriverlo automaticamente. Gli esiti incerti non vengono ritentati e richiedono controllo umano (`gateway/TRobot.WhatsApp/src/journal.js`).
- **Limiti applicativi, non quote ufficiali WhatsApp:** risposte al callback fino a 2000 unita UTF-16; invii manuali `/send` fino a 1000. Gli input hanno limite 100 nel gruppo e 300 in privato. Il gateway filtra messaggi anteriori all'attivazione/connessione, vecchi oltre due minuti o futuri oltre trenta secondi. Applica budget separati per identita del mittente: sei input/minuto nel gruppo e quindici in privato. Ritardi o clock fuori sincronia possono far scartare input (`gateway/TRobot.WhatsApp/src/gateway.js`).
- **Sessioni diverse:** M33 dura cinque minuti fissi; il dialogo soggiorni ha regole proprie. Non riusare la scadenza di un flusso per un altro. Le sessioni scadute M33 vengono ripulite alla successiva interazione, senza timer di notifica.
- **Approvazioni obsolete:** fingerprint degli elenchi include le pagine esatte; cambiare testo o impaginazione richiede nuova approvazione. Modifiche di gruppo, permessi o nuclei possono invalidare dialoghi e conferme.
- **Dati personali:** saldi individuali e dati riservati restano nel sito. Il logger delle dipendenze Baileys/libsignal e soppresso per evitare esposizione di sessioni crittografiche; non riattivare console o log di payload per diagnosticare rapidamente.
- **Backup e chiavi:** SQLite, eventuali WAL, Data Protection e sessione WhatsApp richiedono coerenza. Non copiare alla cieca il solo file DB durante scritture. Non pubblicare file sotto `.local/` o backup privati. Su Windows le chiavi DPAPI possono essere illeggibili da un utente diverso.
- **Produzione ARM64 e memoria ridotta:** usare runtime/dipendenze della piattaforma corretta, compilare fuori dalla VM e rispettare ownership/ACL. Certificato TLS pubblico e certificato interno Kestrel hanno cicli distinti; rinnovo interno documentato, scadenza corrente da verificare in `docs/AZURE_CLOUD.md` e `docs/ORACLE_CLOUD.md`.
