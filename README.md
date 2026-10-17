# T.Robot

Prototipo locale per organizzare Capodanno 2027 con sito riservato agli organizzatori: eventi, presenze, nuclei, attività, menu, messaggi simulati, sondaggi, contabilità locale e quiz volontari. Le milestone 20–22 aggiungono impegni, sondaggi e quiz dal simulatore e scheduler automatico locale; 380 test superati. UI MVC/Bootstrap rifinita con Impeccable. Gateway WhatsApp, AI esterna, trasferimenti reali e deploy restano esclusi.

## Struttura

- `TRobot.sln`: Web MVC → Application + Infrastructure; Application → Domain; Infrastructure → Application + Domain. Domain senza dipendenze esterne.
- `src/TRobot.Domain`: entità e invarianti dei moduli, revisioni e ricevute, senza dipendenze esterne.
- `src/TRobot.Application`: servizi validati e autorizzati per evento, presenze, nuclei, attività, menu, outbox, sondaggi, contabilità, giochi, dashboard e storico.
- `src/TRobot.Infrastructure`: EF Core SQLite, store Identity, mapping, factory per CLI e migrazioni.
- `tests/TRobot.Tests`: xUnit, regole di dominio e integrazione SQLite con migrazioni reali.
- [Contesto](docs/PROJECT_CONTEXT.md), [decisioni](docs/DECISIONS.md), [flusso presenze](docs/MILESTONE_2.md), [account organizzatori](docs/MILESTONE_4.md), [gestione accessi](docs/MILESTONE_5.md), [eventi/partecipanti](docs/MILESTONE_6.md), [correzione nomi](docs/MILESTONE_7.md), [storico](docs/MILESTONE_8.md), [presenze organizzatore](docs/MILESTONE_9.md) e [luogo/programma](docs/MILESTONE_10.md).

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

Il comando EF crea o aggiorna `src/TRobot.Web/trobot.db` applicando tutte le migrazioni versionate, comprese `OrganizationTasks`, `Households`, `MenuPlanning`, `SimulatedOutbox`, `LocalPolls`, `LocalAccounting`, `LocalGames`, `SimulationActions` e `AutomaticLocalScheduler`. Quest'ultima mantiene l'automazione disattivata per gli eventi esistenti. Ripetere l'aggiornamento non duplica lo schema. La factory individua la radice della soluzione; il comando documentato usa Infrastructure anche come startup project, senza avviare Web. Il manifest degli strumenti è `dotnet-tools.json` nella radice (EF CLI 10.0.6). Non usare `EnsureCreated` in alternativa alle migrazioni.

La connection string Web è `ConnectionStrings:TRobot` in `appsettings.Development.json`; i percorsi relativi sono risolti rispetto alla cartella Web. La factory CLI usa il percorso predefinito; per un database diverso passare esplicitamente `--connection "Data Source=C:/percorso/trobot.db;Foreign Keys=True"` a `database update` e configurare la Web con lo stesso percorso. Non inserire credenziali nel repository.

Per future variazioni di modello:

```powershell
dotnet ef migrations add NomeModifica --project src/TRobot.Infrastructure --output-dir Persistence/Migrations
dotnet ef database update --project src/TRobot.Infrastructure
```

Versionare migrazioni e snapshot, mai database o contenuti personali. I test automatici usano SQLite in memoria e dati sintetici, senza modificare il database locale.

## Account per la demo già esistente

Dopo migrazione e build, per aggiornare una demo preparata con la Milestone 2/3:

```powershell
dotnet run --project src/TRobot.Web --launch-profile http --no-build -- --initialize-identity
```

Il comando locale legge il PersonId del vecchio organizzatore da `.local/local-access.json` e crea il primo account amministratore solo se non esistono account. Non modifica partecipazioni o ricevute. Le nuove credenziali casuali sono in `.local/identity-admin.txt` (username `admin`); il precedente codice di accesso non autentica più nel sito. Il file delle nuove credenziali non viene sovrascritto se esiste già.

## Prima preparazione su database vuoto

Dopo migrazioni e build, eseguire una sola volta su un database vuoto:

```powershell
dotnet run --project src/TRobot.Web --launch-profile http --no-build -- --initialize-demo
```

Il servizio crea un evento chiamato **Evento dimostrativo — Capodanno 2027**, un organizzatore e un partecipante con mittente sintetico `demo-participant`. Prepara anche l'account `admin` e scrive la password casuale in `.local/identity-admin.txt`. Non crea più i vecchi file di accesso a codice. La cartella `.local` è esclusa da Git e fuori dagli asset pubblici.

La preparazione non viene eseguita all'avvio del sito. Su un database con persone/eventi usare l'aggiornamento Identity, non ripetere il seed demo. Non cancellare un database esistente per ripetere la preparazione. Custodire le credenziali locali con i permessi del proprio account.

## Avvio locale

```powershell
dotnet run --project src/TRobot.Web --launch-profile http --no-build
```

Aprire <http://localhost:5238>, accedere con nome utente e password di `.local/identity-admin.txt`, selezionare il partecipante e inviare uno dei comandi:

| Comando | Stato |
| --- | --- |
| `!presenza si` oppure `!presenza sì` | Confermato |
| `!presenza forse` | Indeciso |
| `!presenza no` | Assente |

Il simulatore supporta anche questi comandi. I riferimenti sono gli otto caratteri mostrati negli elenchi; il GUID completo resta valido in alternativa e in caso di ambiguità.

| Comando | Risultato |
| --- | --- |
| `!aiuto` | Sintassi supportate |
| `!menu [pagina]`, `!todo [pagina]` | Conteggi e massimo cinque elementi locali con riferimenti |
| `!porto <voce>` | Impegno per una voce disponibile, senza sostituire altri |
| `!prendo <attività>`, `!fatto <attività>` | Presa in carico e completamento di un'attività propria |
| `!sondaggi [pagina]`, `!sondaggio <id>` | Sondaggi aperti e dettaglio di un sondaggio pubblicato |
| `!vota <id> <numero opzione>` | Voto modificabile fino alla chiusura/scadenza |
| `!quiz`, `!quiz lista [pagina]`, `!quiz <id>` | Quiz aperti e dettaglio senza soluzione |
| `!rispondi <id> <numero opzione>` | Unica risposta definitiva con adesione volontaria già attiva |

Voti, risposte e impegni richiedono la casella di conferma esplicita del modulo. Gli impegni e i voti richiedono adulti confermati; per i quiz vale l'adesione volontaria registrata nella pagina Giochi. I sondaggi/quiz devono essere pubblicati con esito Sent e ancora aperti. Un nuovo ID messaggio consente di cambiare voto; il reinvio identico non duplica né ripristina vecchi stati. I titoli negli elenchi restano risultati locali per l'organizzatore: non vengono automaticamente accodati. Nessun messaggio reale viene inviato.

Da VS Code è disponibile **T.Robot · Debug locale**, con compilazione preliminare del progetto C#. Prima di avviarlo fermare eventuali istanze già in ascolto sulla porta 5238. Dettagli dell'incremento e del collaudo in [Milestone 19](docs/MILESTONE_19.md).

Ogni nuovo messaggio può modificare la scelta della persona. Ad esempio `si` → `forse` sposta una persona dai confermati agli indecisi, mantenendo invariato il totale. Maiuscole e spazi separatori sono tollerati; quantità e accompagnatori vengono rifiutati. Riavviando l'app, lo stato resta nel database. Se la demo è già stata utilizzata, i conteggi riflettono gli invii precedenti.

Per verificare l'idempotenza, annotare identificativo e comando prima di inviare e reinserirli nel modulo: il reinvio mostra «Messaggio già elaborato» senza altre ricevute. Anche dopo una scelta successiva, il vecchio messaggio non ripristina lo stato precedente. Riutilizzare lo stesso ID con uno stato diverso produce un conflitto. Un nuovo identificativo con lo stesso stato registra l'elaborazione senza alterare conteggi o timestamp dell'ultimo cambio.

L'amministratore globale può aprire **Organizzatori** e creare account normali, assegnandoli a un evento. La persona creata non viene automaticamente aggiunta ai partecipanti. Il nuovo organizzatore vede solo gli eventi assegnati; anche l'amministratore deve essere assegnato a un evento per leggerne le presenze.

Dalla stessa pagina si possono assegnare altri eventi a un account esistente e revocare singole assegnazioni. La revoca richiede la casella di conferma e impedisce subito lettura e modifiche nell'evento, anche con cookie già aperto; gli altri eventi restano accessibili. Assegnazione e storico non vengono cancellati. Una successiva riassegnazione riusa la stessa riga e il ruolo precedente.

**Disabilita account** richiede una conferma esplicita, blocca i nuovi login e invalida le sessioni. **Riattiva account** consente nuovamente il login con la password esistente; i vecchi cookie restano invalidi. Le assegnazioni ancora attive vengono mantenute. L'amministratore globale non può essere disabilitato tramite questa pagina. Le operazioni ripetute non duplicano l'audit e le revoche per evento registrano anche l'evento interessato.

Ogni utente attivo può usare **Cambia password**: serve la password attuale; la nuova richiede almeno 12 caratteri con maiuscole, minuscole, cifre e simboli. Gli altri cookie vengono invalidati, mentre la sessione che effettua il cambio rimane attiva. Il file delle credenziali iniziali non viene aggiornato dopo il cambio password. Non sono ancora disponibili recupero password, MFA, eliminazione account o promozioni globali tramite Web.

Arrestare con `Ctrl+C`. Il sito protegge tutti i POST con anti-CSRF e verifica i permessi nei servizi Application. Il login consente cinque tentativi al minuto per processo e blocca un account per 15 minuti dopo cinque password errate. Il cookie dura 30 minuti. Il prototipo ascolta solo su loopback e rifiuta ambienti diversi da Development. Prima di un deploy occorre configurare HTTPS e il ciclo di vita protetto delle chiavi per l'hosting scelto.

## Gestione di eventi e partecipanti

Aprire **Eventi** dopo il login. L'amministratore globale può scegliere **Crea evento**, indicare nome, data/ora locali e fuso (`Europe/Rome` predefinito). Riceve automaticamente accesso all'evento appena creato; dalla pagina Organizzatori può assegnare gli altri account.

Gli organizzatori vedono solo i propri eventi attivi. Nella scheda evento possono modificare nome/data/fuso e registrare singoli partecipanti, che partono da **Senza risposta**. Il fuso dell'evento determina la conversione in UTC: il fuso del PC non modifica l'orario inserito. Orari ambigui o inesistenti al cambio dell'ora richiedono di scegliere un orario diverso. Se un'altra pagina ha già modificato l'evento, il salvataggio viene rifiutato: usare **Ricarica dati salvati** prima di riprovare.

Creazione e modifica includono **Luogo** (facoltativo, 300 caratteri) e **Programma** (facoltativo, 4.000 caratteri). Il programma è una nota libera della serata, con righe e orari nel fuso evento. Il testo viene mostrato nella dashboard senza interpretare HTML o Markdown. Per rimuoverlo, svuotare il campo e salvare; gli eventi senza dati mostrano **Da definire**. Nome, data, fuso, luogo e programma condividono revisione e audit, quindi una pagina obsoleta non può sovrascrivere le correzioni altrui.

`EventLogistics` aggiunge colonne nullable e relativi vincoli, senza riempire gli eventi precedenti o modificare credenziali e partecipazioni. Nessun nuovo bootstrap necessario.

La casella **Abilita nel simulatore locale** crea un mittente sintetico; deselezionandola la persona viene conteggiata ma non compare fra i mittenti. Dalla scheda evento aprire **Presenze e simulatore** per provarne la presenza. Non si creano account per i partecipanti e non si richiedono numeri telefonici.

Ogni nuova registrazione crea una persona distinta; usare nomi distinguibili per gli omonimi. Il reinvio dello stesso modulo non duplica la registrazione, anche dopo una correzione del nome. Non sono ancora disponibili collegamento di anagrafiche tra eventi, rimozione di partecipanti, famiglie o accompagnatori.

La migrazione `EventManagement` aggiunge la revisione dell'evento (zero per i dati esistenti) e l'audit delle operazioni, senza modificare credenziali o ripetere la demo. Le modifiche e il relativo audit sono salvati nella stessa transazione.

Nella tabella partecipanti usare **Correggi nome** per correggere il solo nome visualizzato. La persona conserva ID, mittente, presenze e ricevute. Se il nome è stato già corretto da un'altra pagina, occorre **Ricarica dati salvati** prima di riprovare. Un nome invariato non produce ulteriori audit.

Per una persona già condivisa tra più eventi, la correzione è consentita solo a chi gestisce tutti gli eventi coinvolti e richiede conferma esplicita. Il nome viene corretto in tutti questi eventi e l'audit registrato in ciascuno. Se mancano i permessi, oppure la persona è collegata ad account o ruoli organizzativi, la tabella indica che la correzione non è disponibile; il servizio impedisce anche invii diretti al relativo endpoint. Le anagrafiche degli organizzatori richiedono un flusso dedicato.

`ParticipantNames` aggiunge revisione delle persone e dati tecnici per riconoscere i moduli di registrazione precedenti alla rinomina. Non cambia nomi, account o partecipazioni esistenti e non richiede di ripetere la preparazione demo.

## Storico e registrazione delle presenze

Dalla dashboard o dalla scheda evento aprire **Storico evento**: presenze, modifiche di evento/partecipanti e assegnazioni/revoche degli organizzatori, filtrabili per categoria e paginati a 25 righe. Gli orari usano il fuso evento con offset esplicito; i nomi sono quelli attuali. Sono disponibili solo le operazioni registrate dalle milestone che hanno introdotto i rispettivi audit. Sono esclusi dati tecnici dei messaggi e audit globali degli account. Anche l'amministratore globale deve essere assegnato all'evento.

Nella tabella partecipanti scegliere **Registra presenza**, selezionare Confermato/Indeciso/Assente e spuntare la conferma. Il modulo funziona anche per chi non compare nel simulatore e attribuisce la registrazione all'organizzatore autenticato. La presenza cambia solo nell'evento selezionato, anche quando la persona è condivisa. Non è disponibile il reset a Senza risposta.

Se la presenza è cambiata dopo l'apertura del modulo, il salvataggio viene rifiutato: usare **Ricarica dati salvati**. Un reinvio della stessa operazione non duplica ricevute e non ripristina uno stato precedente. Un nuovo invio con stato invariato registra l'operazione senza modificare timestamp o conteggi. Lo storico distingue **Presenza simulata** e **Presenza registrata dall'organizzatore**.

La Milestone 8 non richiede migrazioni; `OrganizerAttendance` della Milestone 9 aggiunge la provenienza delle ricevute e mantiene quelle precedenti come simulazioni. Non occorre ripetere il bootstrap o modificare le credenziali.

## Verifiche e prossimo passo

I test verificano regole e vincoli della Milestone 1 più parsing, autorizzazione, storico, deduplica anche dopo cambio DbContext, rollback, concorrenza e aggiornamento del database precedente senza perdere partecipazioni. Sono coperte tutte le transizioni da ciascuno stato iniziale verso confermato, indeciso e assente, i reinvii di messaggi superati e gli ID riutilizzati con scelte diverse. I test HTTP usano cookie e anti-CSRF reali, SQLite in memoria e verificano login, simulazione, conteggi, esiti invariati, duplicati, accesso a eventi non assegnati, logout e limite tentativi.

Milestone 2, verificata il 16 ottobre 2026: build con 0 errori e 0 avvisi; **48 test passati**. Migrazione `AttendanceSimulation` applicata al database locale e ripetuta senza modifiche; `dotnet ef migrations has-pending-model-changes --project src/TRobot.Infrastructure --no-build` senza differenze. Smoke test sull'app avviata: anonimo reindirizzato al login, accesso con codice locale, confermati da 0 a 1, reinvio senza duplicato e logout; listener verificati solo su `127.0.0.1` e `::1`. Lo smoke test ha lasciato una presenza confermata nella demo. L'avvio è stato verificato nel contesto utente ordinario: la sandbox non può decifrare le chiavi DPAPI del profilo Windows. Nessuna verifica di deploy o integrazioni esterne.

Milestone 3, verificata il 16 ottobre 2026: `dotnet build TRobot.sln --no-restore` con 0 errori e 0 avvisi; `dotnet test TRobot.sln --no-build --no-restore` con **79 test passati**. Controllo EF del modello senza differenze. Verificati tramite pipeline HTTP reale in TestServer login, ciclo `si` → `forse` → `no` → `si`, conteggi, esiti invariati e reinvii di messaggi superati; database SQLite in memoria. Queste verifiche non modificano la demo locale.

Milestone 4, verificata il 16 ottobre 2026: build con 0 errori e 0 avvisi; **86 test passati**. Test su due account con eventi separati, privilegi globali distinti dall'accesso alle presenze, creazione senza promozioni tramite campi contraffatti, rollback completo anche dopo la scrittura Identity, lockout, cambio password e invalidazione delle altre sessioni. Upgrade da schema precedente con partecipazione e ricevuta conservate. Migrazione locale applicata e ripetuta senza modifiche; modello EF allineato. Amministratore locale preparato con credenziali fuori da Git; copia del database precedente in `.local/trobot-before-identity.db`. Smoke test sull'app avviata: vecchio codice rifiutato, login Identity, dashboard con presenza precedente, pagine account/cambio password e logout. Listener solo loopback; processo poi arrestato. Nessun deploy o invio WhatsApp.

La Milestone 4 aggiunge la migrazione `OrganizerIdentity` allo schema precedente. Il bootstrap crea un account, senza ripetere la preparazione demo.

La Milestone 5 aggiunge `OrganizerAccessLifecycle`: account e assegnazioni precedenti rimangono attivi. Non occorre ripetere il bootstrap o cambiare le credenziali.

Milestone 5, verificata il 16 ottobre 2026: build con 0 errori e 0 avvisi; **98 test passati**. Coperti revoca per evento con cookie esistente, blocco di lettura e invio nell'evento revocato, altri eventi ancora accessibili, disabilitazione/riattivazione, vecchio cookie non riutilizzabile, protezione dell'amministratore, conferme lato server, idempotenza e rollback dell'audit con security stamp. Upgrade con account, assegnazioni e ricevute precedenti conservati. Migrazione locale applicata e ripetuta, modello EF allineato. Copia precedente in `.local/trobot-before-access-lifecycle.db`; confronto dei conteggi prima/dopo invariato, `foreign_key_check` senza errori e `integrity_check` OK. SQLite segnala la ricostruzione dei vincoli audit con PRAGMA fuori transazione: la verifica del DB è stata eseguita dopo il completamento della migrazione.

Smoke test della Milestone 5 sull'app avviata: login, dashboard precedente, moduli di revoca e conferma, amministratore protetto e logout; listener solo loopback. Processo poi arrestato, nessuna revoca sull'account reale della demo.

Milestone 6, verificata il 16 ottobre 2026: build con 0 errori e 0 avvisi; **116 test passati**. Coperti creazione/modifica eventi, registrazione e simulazione di nuove persone, anti-CSRF, permessi per evento, account disabilitati, revisioni obsolete, conversione UTC e cambio dell'ora, reinvii e rollback dopo le scritture SQL. Migrazione `EventManagement` applicata, modello allineato, dati precedenti confrontati senza differenze; integrità e chiavi esterne OK. Copia in `.local/trobot-before-event-management.db`. Smoke test reale su login, conteggi precedenti e nuove pagine; listener solo loopback e processo poi arrestato. Nessun nuovo evento o partecipante aggiunto alla demo dallo smoke.

Milestone 7, verificata il 16 ottobre 2026: build con 0 errori e 0 avvisi; **137 test passati**. Coperti correzione nomi, permessi e conferme su eventi condivisi, concorrenza effettiva fra DbContext, rollback di nome/revisione/audit/impronta e reinvii dopo più rinomine, anche da registrazioni precedenti. Migrazione `ParticipantNames` applicata, modello allineato e dati precedenti invariati; integrità e chiavi esterne OK. Copia in `.local/trobot-before-participant-names.db`. Smoke test reale su modulo e salvataggio invariato senza audit; dati demo e credenziali conservati, listener solo loopback e processo poi arrestato.

Milestone 8: build con 0 errori/avvisi e **146 test superati**. Verificati storico combinato, filtri, paginazione stabile su dati invariati, isolamento per evento, privacy, cambio dell'ora e revoca con cookie aperto. Modello EF invariato; smoke test locale riuscito.

Milestone 9: build con 0 errori/avvisi e **173 test superati**. Verificate presenze organizzatore anche senza mittente, conferma/anti-CSRF, tutte le transizioni, reinvii superati, pagine obsolete, collisioni fra sorgenti e rollback dopo SQL. Migrazione `OrganizerAttendance` applicata, modello allineato, dati precedenti invariati e integrità/chiavi esterne OK. Copia preventiva `.local/trobot-before-organizer-attendance.db`. Smoke test reale con invio non confermato rifiutato, storico e conteggi precedenti conservati; listener solo loopback e processo arrestato.

Milestone 10: build con 0 errori/avvisi e **183 test superati**. Verificati luogo e programma facoltativi, limiti e normalizzazione delle righe, codifica del testo, rimozione dei valori, pagine obsolete, permessi, rollback e concorrenza. Migrazione `EventLogistics` applicata, modello EF allineato, dati precedenti invariati e integrità/chiavi esterne OK. Copia `.local/trobot-before-event-logistics.db`. Smoke test reale con salvataggio invariato e dashboard; listener solo loopback, processo arrestato e credenziali conservate.

Milestone 18 completata: build con **0 errori e 0 avvisi**, review indipendente positiva, **13 test mirati e 296/296 test della suite completa superati**. Smoke autenticato sul vero host con copia consistente del database: login, pagine di tutti i moduli, no-store, CSS e logout verificati. Listener solo `127.0.0.1` e `::1` sulla porta 5238; host arrestato senza listener residui. Modello EF senza modifiche pendenti; aggiornamento database ripetuto senza operazioni. Confronto di 13 tabelle precedenti invariato, integrità e chiavi esterne OK. Nessuna mutazione business o modifica alle credenziali originali; database e `.local` ignorati e non tracciati da Git.

## Moduli locali e limiti

La dashboard conserva il simulatore `!presenza si|forse|no`, aggiunge countdown nel fuso evento, conteggi organizzativi e avvisi per attività scadute, sondaggi/quiz pubblicati da chiudere, spese da approvare e pagamenti dichiarati. La navigazione collega tutti i moduli; lo storico include gli audit locali paginati senza duplicare dati o testi dei messaggi.

- **Nuclei e menu:** raggruppano persone già partecipanti; adulto/bambino è riferito all'evento. Assegnazioni di attività o menu non cambiano le presenze. Nessuna eliminazione di riferimenti storici.
- **Sondaggi:** 2–10 opzioni fisse dalla bozza; anteprima e conferma accodano la pubblicazione. Il voto simulato richiede messaggio Sent e adulto confermato; nessuna delega familiare. Chiusura e decisione sono esplicite; il risultato passa da una seconda anteprima e conferma.
- **Contabilità:** EUR, spese solo se approvate, quote dinamiche dei partecipanti confermati con pesi e ripartizione deterministica dei centesimi. Anticipi e pagamenti sono registrazioni locali tra persone già partecipanti; un organizzatore che non partecipa non è parte contabile. Cambi a presenze, classificazioni o pesi ricalcolano le quote: non sono rendiconti congelati. Nessun trasferimento di denaro o servizio bancario.
- **Giochi:** adesione volontaria registrata dall'organizzatore, quiz con una risposta per persona, un punto per risposta corretta dopo chiusura e classifica solo nel sito. Ritiro e riadesione non azzerano risposte storiche. Nessun gioco offensivo o annuncio automatico della classifica.
- **Messaggi:** anteprima, conferma, coda ed esito Sent sono simulati. Il tick manuale e lo scheduler automatico rispettano pausa, silenzio e limite degli interventi spontanei; gli operativi già approvati superano silenzio/limite ma non pausa. Non esiste un gateway né un job che invia a WhatsApp. Nessuna AI esterna o DM; saldi e dati personali individuali non entrano nei riepiloghi aggregati.

Tutte le pagine e le simulazioni sono esclusivamente per organizzatori autenticati, in Development su loopback. I partecipanti non accedono al sito. Il parsing locale copre presenze, aiuto, menu/attività e impegni, sondaggi/voti e quiz/risposte; gli altri comandi WhatsApp descritti nel contesto rimangono specifiche future. I moduli e i comandi usano form simulati, senza ricevere messaggi reali. Nessun deploy è stato eseguito.

Per una verifica manuale locale: accedi, scegli un evento e apri i moduli dalla dashboard. Per pubblicare un sondaggio o quiz, crea la bozza, verifica l'anteprima, approva e poi usa **Messaggi e configurazione → Elaborazione manuale**, oppure attendi lo scheduler automatico se abilitato; soltanto dopo Sent saranno disponibili voto o risposta. Usa dati sintetici in un database separato per le prove che modificano dati.

Per attivare lo scheduler apri **Messaggi e configurazione → Scheduler automatico locale**, seleziona **Abilita l'elaborazione automatica per questo evento**, conferma e salva. Ogni 30 secondi elabora la coda già approvata e i riepiloghi aggregati, solo mentre T.Robot è aperto. Il default è disattivato e l'attivazione resta legata al tuo accesso: se viene revocato l'elaborazione si sospende, se viene ripristinato può riprendere. La pausa blocca tutto; i cicli senza effetti non producono audit o nuove revisioni. Nessuna chiusura automatica di sondaggi o quiz. L'opzione tecnica `LocalScheduler:Enabled=false` disabilita il worker nell'intera istanza (usata nei test HTTP generali).

Dettagli: [roadmap locale](docs/ROADMAP_LOCAL.md), [impegni](docs/MILESTONE_20.md), [sondaggi e quiz](docs/MILESTONE_21.md), [scheduler automatico](docs/MILESTONE_22.md), [decisioni](docs/DECISIONS.md). Passi successivi da concordare dopo verifica del prototipo; gateway o servizi reali richiedono una richiesta esplicita.
