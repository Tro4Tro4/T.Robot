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

## 2026-10-16 — Milestone 2

- Scelta dell'utente: simulatore tramite modulo nel sito locale. Flusso e perimetro in [MILESTONE_2.md](MILESTONE_2.md).
- Primo comando limitato a `!presenza si`/`sì` per una sola persona già iscritta; conteggi, famiglie e altri stati richiedono una milestone dedicata. I nomi dei partecipanti simulati sono visibili solo all'organizzatore autenticato.
- Accesso reale locale tramite codice casuale di 256 bit e cookie ASP.NET Core. Hash PBKDF2 con `PasswordHasher`, cookie HttpOnly/SameSite Strict di 30 minuti, anti-CSRF e limite tentativi. Codice e configurazione solo in `.local`, mai versionati; HTTP resta esclusivamente su loopback. Prima di pubblicare servono sistema multiutente, HTTPS e gestione chiavi/credenziali adatta all'hosting.
- Preparazione demo solo con opzione CLI esplicita e database vuoto; nessun seed all'avvio HTTP. Il bootstrap è un'operazione locale dell'operatore, distinta dai servizi che verificano l'organizzatore per evento. Riavvii del sito non modificano i dati.
- Dati dimostrativi: evento il 31 dicembre 2026 alle 20:00 Europe/Rome (19:00 UTC), un organizzatore non conteggiato tra i partecipanti e una persona con mittente sintetico `demo-participant`. Data e ora sono esempi tecnici, non configurazione dell'evento reale.
- Deduplica per `(EventId, MessageId)` in ricevuta persistente. Storico precedente/nuovo stato, persona e organizzatore, istante UTC; nessuna conservazione del corpo del messaggio. Una conferma ripetuta con nuovo ID non aumenta il numero delle persone.
- Transazione per stato e ricevuta; vincoli DB, token di concorrenza sullo stato e rollback in caso di errore. Un conflitto concorrente richiede reinvio dello stesso identificativo, senza scritture parziali.

## 2026-10-16 — Milestone 3

- Disegno circoscritto approvato dall'utente con la skill `brainstorming`: completare `!presenza si|sì|forse|no` per una sola persona già iscritta. Stati rispettivi: `Confirmed`, `Undecided`, `Absent`; nessun comando di reset a `NoResponse`.
- Un nuovo ID permette di passare da qualsiasi stato iniziale a una delle tre scelte. Lo storico registra vecchio e nuovo stato nella transazione esistente; il totale delle persone resta invariato.
- Reinviare un vecchio ID con lo stesso comando normalizzato è un duplicato, anche dopo modifiche successive, e non ripristina la scelta precedente. Stesso ID con stato diverso: conflitto senza modifiche. Nuovo ID con stato invariato: ricevuta aggiunta, conteggi e timestamp dell'ultimo cambio invariati.
- Il risultato Application include lo stato effettivo della persona; la Web lo usa per messaggi coerenti con `Confermato`, `Indeciso` e `Assente`.
- Nessuna modifica allo schema o alle entità; nessuna nuova migrazione o preparazione demo necessaria.

## 2026-10-16 — Modalità autonoma e Milestone 4

- L'utente richiede autonomia: `brainstorming` rimane obbligatorio per chiarire il disegno, ma si documentano e applicano le scelte ragionevoli senza approvazioni intermedie. Si chiede conferma solo per dubbi che impediscono di decidere.
- Scelta di ASP.NET Core Identity con EF Core SQLite 10.0.6, username/password e cookie, senza provider esterno o email obbligatoria. [Disegno e piano](MILESTONE_4.md). Le opzioni seguono i meccanismi supportati dalla [documentazione Identity](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-10.0).
- Account Identity con PersonId univoco; il claim `trobot:person_id` mantiene distinta la persona dall'ID account. I permessi sulle presenze restano in EventOrganizer e vengono controllati nel database.
- Il privilegio globale di amministrazione degli account è un attributo Identity separato dai ruoli per evento. Solo il bootstrap locale crea il primo super amministratore; la Web può creare esclusivamente organizzatori normali. Nessuna registrazione pubblica o promozione tramite campi del modulo.
- Creazione persona/account/assegnazione/audit nella stessa transazione. Cambio password autenticato con password attuale, audit minimo e security stamp per invalidare le altre sessioni. Nessuna password o hash nell'audit o nell'elenco account.
- Password minimo 12 caratteri con maiuscole/minuscole/cifre/simboli; lockout 5 errori/15 minuti; cookie 30 minuti senza rinnovo automatico, HttpOnly e SameSite Strict. Validazione security stamp a ogni richiesta per invalidare rapidamente le sessioni precedenti.
- Migrazione incrementale `OrganizerIdentity`, nessuna cancellazione dei dati precedenti. Il bootstrap per la demo esistente riusa solo il PersonId del vecchio file locale e genera una password nuova in `.local/identity-admin.txt`. Le vecchie credenziali/cookie non vengono usate dall'app.
- Restano Development e loopback; HTTPS, configurazione delle chiavi in hosting, recupero password, MFA, revoca account e modifica assegnazioni richiedono lavoro dedicato. Nessun gateway o invio reale.

## 2026-10-16 — Milestone 5

- Gestione accessi riservata all'amministratore globale tramite servizi Application e moduli anti-CSRF; disabilitazioni e revoche richiedono conferma esplicita nel modulo e nel servizio. [Disegno e piano](MILESTONE_5.md).
- `EventOrganizer.IsActive` consente revoca/riassegnazione senza cancellare righe, ruoli o riferimenti dalle ricevute. Le query delle presenze verificano l'assegnazione attiva e l'eventuale account disabilitato a ogni richiesta. La revoca di un evento non invalida la sessione per gli altri eventi.
- `OrganizerUser.IsEnabled` blocca login e operazioni dell'account disabilitato. La modifica aggiorna il security stamp: sessioni precedenti invalidate e non riutilizzabili dopo la riattivazione. Le assegnazioni ancora attive vengono mantenute.
- Gli amministratori globali non sono disabilitabili dalla Web. È invece consentito revocare anche la loro assegnazione a un evento: conservano la gestione globale e possono ripristinarla. È consentito che un evento rimanga temporaneamente senza organizzatori attivi.
- Un account disabilitato non può ricevere assegnazioni nuove; le sue assegnazioni possono essere revocate. Nessuna assegnazione aggiunge automaticamente un partecipante o promuove a ruoli superiori. La riassegnazione riusa il ruolo della riga esistente.
- Operazioni ripetute già applicate non producono altri audit. La migrazione `OrganizerAccessLifecycle` mantiene attivi account e assegnazioni precedenti; aggiunge EventId facoltativo e nuove azioni all'audit. Stato, security stamp e audit restano atomici.

## 2026-10-16 — Milestone 6

- Incremento circoscritto dei flussi esistenti: creazione eventi, modifica nome/data/fuso e aggiunta di singoli partecipanti. L'amministratore globale crea eventi ed è assegnato automaticamente; per leggere o modificare un evento è sempre necessaria un'assegnazione attiva e un account abilitato, anche per l'amministratore.
- Data inserita nell'ora locale del fuso evento, default `Europe/Rome`, convertita in Application e conservata in UTC. Orari inesistenti o ambigui al cambio dell'ora vengono rifiutati: scegliere un orario inequivocabile. Non si impone che l'evento sia futuro, per consentire preparazione e verifica di eventi passati.
- `Event.Revision` parte da zero per gli eventi esistenti. La modifica richiede la revisione visualizzata nel modulo e un token di concorrenza EF; richieste obsolete non sovrascrivono le modifiche altrui. Salvataggi identici non incrementano revisione o audit.
- Ogni nuova registrazione crea una persona distinta nell'evento, anche in presenza di omonimi; usare nomi distinguibili. Non viene mostrato un elenco globale di persone e non vengono riutilizzate automaticamente identità di altri eventi. Modifica globale delle persone, collegamento fra eventi, famiglie, accompagnatori e cancellazioni rinviati.
- Partecipazione iniziale sempre `NoResponse`, senza creare account o assegnazioni organizzative. La casella simulatore genera un identificativo `sim-<guid>`; se deselezionata, identità assente. Nessun modulo per numeri telefonici o identità WhatsApp reali. La persona senza identificativo viene conteggiata ma non selezionabile nel simulatore.
- Audit dedicato `EventAudits`, con attore, evento, azione, eventuale persona e timestamp UTC; nessuna copia di nomi o messaggi nell'audit. Creazione evento/assegnazione/audit e creazione persona/partecipazione/audit sono atomiche. Il GUID del modulo di aggiunta funge da identificativo operazione: reinvii uguali non creano persone duplicate; riutilizzo con dati, attore o evento differenti viene rifiutato.
- Aggiornamento incrementale `EventManagement`, senza seed automatici o nuove credenziali. L'interfaccia rimane locale e riservata agli organizzatori.

## 2026-10-16 — Milestone 7

- Correzione del solo nome visualizzato dei partecipanti dalla scheda evento. ID persona, mittente, partecipazioni, stati, ricevute e timestamp precedenti restano invariati. Nessuna fusione, rimozione o modifica delle identità del trasporto.
- Il nome è globale alla persona: un organizzatore può correggerlo solo se ha un account abilitato e un'assegnazione attiva in tutti gli eventi che condividono la partecipazione. Anche l'amministratore globale rispetta questa regola. Per persone condivise serve conferma esplicita validata dal servizio; nessuna esposizione di nomi o dettagli di altri eventi a chi non ha accesso.
- Persone collegate ad account Identity o assegnazioni organizzative, anche revocate, escluse da questo modulo. Le correzioni dei dati degli organizzatori richiedono un flusso dedicato. Il pulsante viene mostrato solo quando l'operazione è autorizzata; le richieste contraffatte vengono comunque verificate dal servizio.
- `Person.Revision` con concorrenza EF e revisione obbligatoria nel modulo. Richieste obsolete rifiutate, nomi invariati senza audit aggiuntivo. Audit `ParticipantRenamed` in ogni evento coinvolto, salvato atomicamente con la persona; non si duplicano nell'audit i nomi precedenti.
- Per preservare i reinvii della registrazione dopo una rinomina, `EventAudit.RegistrationFingerprint` conserva SHA-256 del nome originale normalizzato con flag simulatore. È un identificatore tecnico di deduplica, non un meccanismo di anonimizzazione. Le ricevute precedenti con impronta assente vengono completate nella stessa transazione della prima correzione, prima di cambiare il nome. Impronta immutabile, nessuna copia in chiaro del nome originale nell'audit.
- Migrazione incrementale `ParticipantNames`: revisione zero per persone esistenti, impronta facoltativa per audit precedenti, nuovi vincoli audit. Nessuna modifica a credenziali, assegnazioni o dati demo.
