# M31 — Appartamenti e assegnazione camere

## Obiettivo approvato

Gestire dalla regia riservata agli organizzatori gli appartamenti dell'evento, le camere e la sistemazione di ogni persona. Un nucleo può dividersi fra più camere e una camera può ospitare persone di nuclei diversi, per esempio una camera bambini. Riepilogo per appartamento e scheda stampabile per nucleo. Nessun nuovo comando WhatsApp, DM o invio automatico.

Nuovo modulo: percorso architetturale della skill brainstorming, proporzionato al monolite esistente. Disegno presentato in chat e confermato dall'utente il 23 ottobre 2026; autonomia AGENTS applicata ai dettagli tecnici, senza ulteriori approvazioni intermedie. Questo documento registra specifica e piano esecutivo.

## Modello e regole

- Appartamenti e camere appartengono a un solo evento; non si impone un limite artificiale di tre o quattro appartamenti.
- Appartamento con nome e indicazioni; camera con nome, capienza positiva e note. Nessun dato reale precompilato.
- Assegnazione per persona già partecipante all'evento, indipendente dall'identità WhatsApp e dall'appartenenza familiare. Una sola camera per persona nell'evento.
- Sistemazione valida per l'intero evento: turni e assegnazioni per notte esclusi dal primo incremento. I soggiorni restano separati e invariati.
- Capienza vincolante: spostamenti individuali o dell'intero nucleo sono atomici e rifiutati se manca posto. Il nucleo rapido comprende soltanto membri attivi, non inventa appartenenze; lo spostamento di una persona non trascina i familiari.
- Persone di tutti gli stati di presenza gestibili dalla regia, con stato esplicito; i non assegnati e le persone prive di nucleo rimangono visibili.
- Piano in bozza o definitivo. Le modifiche invalidano la conferma precedente; revisioni proteggono da salvataggi obsoleti. La UI non deve rappresentare come definitivo un piano divenuto incoerente dopo cambi di partecipanti o nuclei.
- Dati personali e note restano nel sito organizzatori. La stampa di un nucleo contiene la sistemazione dei suoi membri, senza elencare persone di altri nuclei che condividono la camera.

## Architettura e sicurezza

Domain indipendente per entità e invarianti; Application per operazioni, autorizzazione evento e transazioni; Infrastructure per mapping e persistenza EF SQLite; Web per controller, input validati e viste MVC. Riusare LocalOperations/ILocalStore e i controlli di account abilitato/organizzatore assegnato. POST con antiforgery, revisione attesa e conferma dove prevista dal flusso; audit atomico. Vincoli e indici unici proteggono isolamento evento e unicità delle assegnazioni. Nessuna eliminazione di identità, presenze o storico preesistenti.

## Piano e verifiche

1. Implementatore dedicato gpt-6.1-sol medium: modulo completo, UI integrata nella navigazione evento, riepilogo/stampa e test delle regole.
2. Coordinatore: migrazione additiva versionata, verifica su database temporaneo, build e suite .NET. Database operativo remoto e gateway esclusi.
3. Revisore diverso gpt-6.1-sol medium in sola lettura dopo build/test; eventuali correzioni e nuova verifica fino ad APPROVED.
4. Documentare comandi, risultati e limiti. Nessun deploy né aggiornamento del database remoto implicito.

Test richiesti: nucleo diviso, camera condivisa/bambini senza account, capienza e riduzione posti, spostamento nucleo atomico, unicità, persona/camera/evento estranei, permessi revocati, revisioni obsolete, bozza/definitivo, riepilogo con non assegnati e senza nucleo, protezione stampa e antiforgery. Migrazione con integrity/FK e conservazione delle righe precedenti su database di prova.

## Utilizzo dalla regia

Aprire un evento e scegliere **Alloggi**. Aggiungere gli appartamenti, poi le camere e i rispettivi posti. Dalla sezione per nucleo assegnare insieme i membri attivi oppure salvare la camera di una singola persona; la voce Non assegnato rimuove la sistemazione conservando la riga e l'audit. Il riepilogo superiore mostra gli occupanti e i posti liberi per camera.

La scheda stampabile del nucleo riporta per ogni componente appartamento, camera e indicazioni; per chi è senza nucleo il link personale mostra solo quella persona. Usare la funzione Stampa del browser. Le schede espongono sempre bozza/definitivo e le sistemazioni mancanti. Nessun PDF viene generato o inviato automaticamente.

Il piano definitivo richiede una camera per ogni confermato. Il fingerprint comprende anche presenze, nomi e nuclei: se cambiano fuori dal modulo, il piano risulta da verificare nuovamente. Salvare un'assegnazione identica non modifica stato, revisione o audit. Salvare esplicitamente i dati di un appartamento o camera invalida la conferma del piano.

Non sono incluse eliminazioni di appartamenti/camere né sistemazioni per notte. La migrazione `20261023072906_AddLodgingPlanning` aggiunge quattro tabelle vuote; non assegna persone e non cambia i soggiorni. Per un rilascio occorre applicare la migrazione al database di destinazione con backup, secondo le procedure di hosting. Il database remoto rimane l'unica fonte operativa e non viene aggiornato da questo sviluppo.

## Comandi di verifica

```powershell
dotnet build TRobot.sln --no-restore
dotnet test tests/TRobot.Tests/TRobot.Tests.csproj --no-restore --filter FullyQualifiedName~Lodging
dotnet test TRobot.sln --no-build --no-restore
dotnet ef migrations has-pending-model-changes --project src/TRobot.Infrastructure --no-build
```

I test HTTP utilizzano cookie e antiforgery reali su TestServer, con SQLite temporaneo in memoria e gateway/scheduler disabilitati. Nessun avvio del sito operativo o collaudo con browser esterno in questa milestone.

## Esito dello sviluppo locale del 23 ottobre 2026

Implementazione e review indipendente con due agenti distinti gpt-6.1-sol medium: **APPROVED**. Build soluzione senza errori o avvisi; baseline 565/565, tutti i 14 test mirati e suite finale **579/579 .NET** superati. Modello EF allineato alla migrazione; controllo whitespace positivo.

Il primo giro mirato aveva una sola asserzione HTTP errata (atteso 403 immediato, il middleware cookie restituisce 302 verso Access/Denied): corretta verificando anche il 403 della pagina di destinazione e l'assenza di scritture dopo revoca. Aggiunta regressione per conservare stato definitivo/revisione/audit sulle assegnazioni invariate; secondo giro interamente positivo.

Upgrade da M28 provato in SQLite temporaneo con confronto di tutte le righe delle tabelle precedenti, comprese credenziali e ricevute sintetiche: nessuna variazione. Quattro nuove tabelle vuote, integrity_check OK e foreign_key_check senza violazioni. Non applicata la migrazione al DB locale storico o al DB remoto; nessun deploy, avvio Web/gateway o invio reale. Le modifiche preesistenti di hosting e gateway sono state conservate. Prossimo passo: rilascio sulla VM con backup, migrazione e collaudo della regia.

## Primo tentativo di rilascio autorizzato il 23 ottobre 2026

L'utente ha richiesto esplicitamente di procedere con il rilascio. Autorizzazione acquisita; nessuna nuova conferma richiesta alla ripresa per il medesimo rilascio.

Pubblicazione Linux x64 framework-dependent Release completata senza errori o avvisi, con UseAppHost=false. Archivio privato `.local/oci/release-m31.tar.gz` (8.932.218 byte), SHA-256 `17DDBAA34185E0649FD812A68230A464D932FD6119A998E96A9326862DA59836`. Script EF `.local/oci/m31-migration.sql`, SHA-256 `8B1FD2B79771AFA082FC86FC97EC8F8A9011FDB596F80BCDA437490684B37DB9`. Review indipendente del materiale con gpt-6.1-sol medium: APPROVED. Pacchetto senza DB, credenziali, chiavi o sessioni; provider SQLite ELF64 Linux x64 verificato. Nessuno dei file privati viene versionato.

Ulteriore rehearsal dello script SQL generato su una copia isolata dello snapshot storico del 19 ottobre: tutte le righe delle 43 tabelle precedenti invariate, quattro tabelle nuove vuote, cronologia migrazioni corretta, integrity_check OK e nessuna violazione FK. Report privato `.local/oci/m31-rehearsal-report.json`. Questa verifica riguarda uno snapshot storico locale, non attesta lo schema remoto corrente.

Primo preflight remoto riuscito: SSH e login HTTPS 200, servizi Web/gateway/Caddy attivi e NRestarts=0, circa 603 MiB di RAM disponibile; release corrente m30. La query inline dei metadati DB non ha aperto il database e non ha prodotto scritture; lo schema remoto rimane da verificare. Subito dopo, SCP ha chiuso la connessione e i successivi tentativi hanno restituito timeout durante il banner SSH e timeout HTTPS. Causa non verificata, comportamento simile al blocco M30. Nessuna modifica a release, database, configurazione o stato dei servizi eseguita; nessun backup remoto M31 creato. Anche il controllo browser ha fallito all'avvio del sandbox, impedendo l'accesso alla console Oracle da questa sessione.

Stato alla prima interruzione, superato dal rilascio descritto sotto: rilascio non eseguito. Piano di ripresa allora previsto dopo recupero della VM dalla console Oracle: fermare il gateway come previsto in M30 e raccogliere metriche/log per diagnosticare il blocco prima di attestare stabilità. Verificare lo schema operativo, creare backup completo privato di database/chiavi/sessione/journal, fermare Web, provare lo script su una copia consistente del DB remoto e confrontare tutte le righe precedenti. Solo dopo applicare la migrazione al DB operativo, installare il pacchetto verificato e cambiare atomicamente il collegamento current. Avviare Web e verificare HTTPS, login e Alloggi; riavviare il gateway preservando la sessione e il gruppo già autorizzato, senza messaggi di prova o reinvii. Non riattivare il gateway PC e non sostituire il DB remoto con copie locali.

## Rilascio completato dopo il reboot, 23 ottobre 2026

L'utente ha riavviato la VM dalla console e richiesto di riprovare. Gateway subito fermato per il recupero; schema remoto M28 verificato, integrity/FK OK. I file LF sono stati trasferiti con SCP in modalita legacy (-O); le verifiche successive SSH sono riuscite. Non e stata dimostrata una causa del blocco precedente.

Preparazione della release isolata e script operativi sottoposti a review indipendente gpt-6.1-sol medium, APPROVED. Corretto prima dell'esecuzione il rollback: fermare sempre Web prima di ripristinare il collegamento alla release precedente. Script verificati anche con bash -n sulla VM. Archivio e SQL sono quelli con hash riportati sopra.

Backup completo consistente creato con Web/gateway fermi: database, chiavi Data Protection, sessione/journal e configurazione privata gateway. Contesto del rilascio e percorso del backup precedente in `/root/trobot-provisioning/m31-release-context.json`. Copia fuori VM `.local/oci/m31-private-backup/m31-before-backup.tar.gz`, ACL limitata a utente corrente/SYSTEM/Administrators e SHA-256 verificato contro l'archivio remoto. Nessun segreto nei file versionati o negli output.

Rehearsal sulla copia del DB remoto positivo, poi applicazione operativa con BEGIN IMMEDIATE e confronto prima del commit: tutte le righe delle 43 tabelle preesistenti invariate (42 applicative/Identity e `__EFMigrationsLock`; esclusa la cronologia migrazioni, alla quale viene aggiunta la voce M31). Quattro nuove tabelle inizialmente vuote, integrity_check OK e foreign_key_check senza violazioni. Report privati `/var/lib/trobot/m31-rehearsal-report.json` e `/var/lib/trobot/m31-production-report.json`. Nessuna sostituzione del DB remoto, risemina o modifica delle presenze.

`/opt/trobot/app/current` punta ora a `releases/m31`, codice root:trobot senza scrittura per gruppo/altri. Dati, identita, chiavi persistenti e configurazione privata invariati; confronto con il backup conferma chiavi Data Protection, identita WhatsApp e file gateway originali. Rinnovo automatico Caddy e configurazione TLS conservati, scheduler Production escluso e gateway PC disabilitato.

Collaudo sul sito effettivo: TLS pubblico con trust di sistema, login con account originale e cookie Secure/HttpOnly, cinque pagine autenticate (eventi, Alloggi, soggiorni, nuclei, menu), stampa HTTP 200, cinque POST senza CSRF respinti 400. Browser headless desktop 1440 e mobile 390 senza overflow o errori JavaScript; catture private in `.local/ui/m31-server-*.png`. Sessione mantenuta dopo riavvio Web e logout verificato. Nessuna nuova assegnazione o dato dimostrativo creato in produzione.

Gateway riattivato con recovery esistente, stato connected e gruppo gia autorizzato ripristinato senza mostrare identificativi; nessun invio di prova. Timer backup ripristinato e ciclo reale di backup con gateway attivo concluso con Result=success/ExecMainStatus=0, seguito da recovery e nuova verifica del gruppo. Web, Caddy, gateway e timer attivi e abilitati al boot, NRestarts=0. HTTPS pubblico finale 200. Circa 379 MiB di RAM disponibili all'ultima verifica e nessun evento OOM/oom_kill nei cgroup Web/gateway dal boot corrente.

Rilascio M31 completato. Resta aperta la diagnosi M30: il journal del boot precedente non era persistente, quindi i blocchi passati non possono essere attribuiti a memoria, CPU o rete sulla base delle evidenze raccolte. Nessun nuovo reboot completo della VM eseguito dopo l'attivazione M31; verificato il riavvio del servizio Web e il recupero del gateway. Prossimo passo funzionale: inserire appartamenti/camere dalla regia. Rinomina nuclei e disattivazione dei tab accantonati restano richieste successive in roadmap.
