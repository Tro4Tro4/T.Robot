# T.Robot — Registro decisioni

## Confermate
- Nome pubblico: **T.Robot**; nomi tecnici `TRobot.*`.
- Partecipanti solo nel gruppo WhatsApp esistente, senza messaggi privati.
- Mini sito MVC accessibile solo agli organizzatori autenticati.
- Conferme tramite reazioni emoji quando supportate; riepiloghi a frequenza crescente verso Capodanno.
- Integrazione WhatsApp sperimentale separata, non prevista nella prima milestone.

## Da decidere durante l’implementazione
- Autenticazione effettiva degli organizzatori prima di pubblicare il sito.
- Dettagli di deploy e hosting.
- Regole per voti di accompagnatori e nuclei familiari.

Aggiornare questo file quando si prende una nuova decisione, riportando data e motivazione.

## 2026-10-16 — Milestone 1

- SDK disponibile: .NET 10.0.401, target `net10.0`, soluzione classica `TRobot.sln`. `global.json` consente aggiornamenti patch della stessa feature band.
- EF Core e tool locale `dotnet-ef` fissati a 10.0.6. Bundle `SQLitePCLRaw.bundle_e_sqlite3` esplicito a 3.0.5 per sostituire la dipendenza nativa 2.1.11 segnalata da NuGet ([GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q)). Non vengono soppressi avvisi di sicurezza.
- NuGet.Config usa solo nuget.org per rendere il restore indipendente dalle sorgenti aziendali configurate sulla macchina. Cache di lavoro opzionali in `.local`, escluse da Git.
- ID interni Guid; una persona per partecipante, WhatsApp sender ID opzionale e univoco quando presente. Nessun identificativo di gruppo finché non viene progettato il trasporto. Famiglie e relazioni tra accompagnatori rimandate: nessuna dipendenza obbligatoria da WhatsApp.
- Chiavi composte `(EventId, PersonId)` per partecipazioni e organizzatori. Un organizzatore può esistere senza partecipazione; ruolo per evento `Organizer`/`Administrator`, senza implicare autenticazione o permessi già operativi. Il futuro super amministratore globale richiederà un modello di sicurezza dedicato.
- Partecipazione iniziale `NoResponse`; gli altri stati previsti sono mappati ma non sono ancora esposti comandi di modifica. Storico e autorizzazione delle modifiche rientrano nei successivi servizi Application.
- Date `DateTime` UTC con ripristino esplicito di `Kind` alla lettura SQLite. L'evento contiene l'istante UTC e un fuso configurabile valido, default già approvato `Europe/Rome`.
- Cancellazioni dei riferimenti a eventi e persone con `Restrict`; nessuna cancellazione a cascata dei collegamenti. Retention/anonimizzazione da progettare prima di usare dati reali.
- Database locale `src/TRobot.Web/trobot.db`, escluso da Git. Migrazioni esplicite da CLI, nessun seed e nessuna migrazione automatica all'avvio.
- Web MVC solo Development su loopback, porta 5238. Nessun pannello dati o endpoint di scrittura fino a validazione e autorizzazione Application; autenticazione reale richiesta prima di pubblicare. Application resta un progetto predisposto, senza servizi fittizi.
- La cartella iniziale non contiene `.git`: nessuna inizializzazione, commit o pubblicazione impliciti.
- Log su console per non richiedere accesso a Event Log Windows. Data Protection mantiene le impostazioni ASP.NET Core standard: nell'ambiente sandbox le chiavi DPAPI del profilo utente non sono utilizzabili; lo smoke test va eseguito nel contesto utente ordinario. Prima dell'autenticazione configurare il ciclo di vita protetto delle chiavi secondo l'hosting scelto.
