# T.Robot — Contesto di progetto e specifiche (handoff per Codex)

## Identità e obiettivo

**M33 rilasciata su Azure il 31 ottobre 2026:** `!proponi` nel gruppo avvia un dialogo privato personale per proporre un piatto per giorno e pasto, come suggerimento o disponibilita a cucinare. Eccezione esplicita alla precedente regola senza DM, limitata a questo flusso. Conferma prima del salvataggio, `!annulla` e scadenza fissa dopo cinque minuti dall'avvio. Archivio web riservato agli organizzatori separato dal Menu sospeso; decisione comune, menu definitivo e spesa successivi. Build pulita, 634/634 test .NET, 56/56 Node e review indipendente APPROVED. Web e gateway in `m33-20261031-arm64`, migrazione additiva verificata su copia corrente e applicata con tutte le righe precedenti conservate. Backup privati sulla VM, chiavi/configurazione/identita preservate e gateway connected sul gruppo originale. Undici pagine e 44 controlli browser pubblici positivi, piu tre senza JavaScript. Prova del nuovo dialogo dal telefono ancora da eseguire. Dettagli in `docs/MILESTONE_33.md`.

**Aggiornamento mobile Azure del 23 ottobre 2026:** su richiesta esplicita rilasciata `mobile-20261023-arm64`, con menu compatti, schede partecipanti/nuclei e ricerca locale, selettore solo per piu eventi e simulatore espandibile. Default desktop e fallback senza JavaScript conservati. Nessuna migrazione; tutte le righe operative, configurazione, chiavi e identita preservate tramite confronto prima/dopo. Backup privato sulla VM e gateway connesso al gruppo originale. 624/624 test .NET, 80 controlli browser locali, 40 pubblici e tre percorsi pubblici senza JavaScript positivi. Dettagli in `docs/UI_MOBILE.md`.

**Aggiornamento UI Azure del 23 ottobre 2026:** grafica e usabilita rifinite con Impeccable, release `ui-20261023-arm64` attiva su richiesta dell'utente. Navigazione per compiti con Soggiorni, guide contestuali, azioni rapide e assegnazioni Alloggi espandibili. Nessuna migrazione; tutte le righe operative, configurazione, chiavi e identita conservate. Backup privato sulla VM, stessa sessione login valida e gateway connesso al gruppo originale. Suite locale 622/622, collaudo pubblico di dieci pagine e 20 controlli desktop/mobile positivo. Dettagli in `docs/UI_RELEASE_AZURE.md`.
Nome definitivo: **T.Robot** (sostituisce CapoDannoBot, tRobot e T-Robot). Convenzioni tecniche: soluzione `TRobot.sln`, namespace radice `TRobot`, progetti `TRobot.*`, repository/cartella `T.Robot`, database `trobot.db`. Progetto per organizzare Capodanno 2027 in un gruppo WhatsApp già esistente, con oltre 20 membri. Bot utile ma sarcastico/provocatorio, mai offensivo; circa 50% organizzazione e 50% intrattenimento, senza spam. Frase guida: «Voi fate casino, io tengo i conti».

## Decisioni già approvate — NON rimetterle in discussione senza motivo
1. I partecipanti interagiscono **esclusivamente nel gruppo WhatsApp esistente**; nessun messaggio privato, nessun sito da utilizzare, nessuna app aggiuntiva.
2. Un **mini sito ASP.NET Core MVC riservato agli organizzatori** gestisce dati, configurazione, approvazioni, messaggi e dashboard. Solo organizzatori autenticati possono accedervi.
3. Un super amministratore e altri organizzatori autorizzati; partecipanti con privilegi limitati. Operazioni sensibili richiedono controllo permessi, audit e spesso conferma.
4. Bot risponde ai comandi con prefisso `!` e a richieste in linguaggio naturale chiaramente indirizzate al bot; non commenta ogni messaggio del gruppo.
5. Conferme delle operazioni semplici tramite **reazione emoji** al messaggio originale, quando supportata dal gateway; fallback breve/consultazione esplicita. Operazioni complesse hanno conferma esplicita.
6. Riepiloghi **a frequenza crescente verso Capodanno**: >30 giorni settimanale; 30–15 giorni ogni 4 giorni; 14–7 ogni 2; ultimi 6 giorni quotidiano; il 31 dicembre solo avvisi operativi e giochi programmati. Se non ci sono novità rilevanti, omettere. Configurabile dal sito.
7. Default anti-spam: massimo 3 interventi spontanei al giorno (escluse comunicazioni operative essenziali); silenzio 23:00–09:00; rate limit sulle risposte richieste. Fuso orario dell'evento configurabile (default Europe/Rome).
8. Dati personali e saldi individuali **mai pubblicati automaticamente nel gruppo**. Poiché non sono previsti messaggi privati, tali dati rimangono nel sito riservato; nel gruppo solo dati aggregati o autorizzati e pertinenti. Non leggere o conservare chat estranee al bot salvo metadati tecnici minimi necessari.
9. AI **ibrida**: parsing deterministico per comandi e regole, AI per interpretare richieste dirette e generare battute. L'AI non scrive direttamente nel DB, non decide autorizzazioni, non esegue azioni sensibili senza validazione e conferma.
10. Iniziare con simulatore locale di messaggi, poi gateway reale. L'integrazione di un bot in un gruppo WhatsApp ordinario >20 membri **non è garantita dall'API ufficiale**; candidato sperimentale Node.js + Baileys (non ufficiale, rischio disconnessioni/restrizioni dell'account). Numero dedicato, interfaccia trasporto astratta e nessuna promessa di affidabilità. Verificare policy e compatibilità prima dell'uso reale.
11. Tecnologia: C#, ASP.NET Core MVC, EF Core, SQLite inizialmente, xUnit, logging strutturato, hosted background services per job. Preferire monolite modulare per Web+API, con gateway Node separato. .NET 10 LTS proposto, ma verificare SDK/IDE disponibili prima di vincolare i progetti.
12. **Stato al 17 ottobre 2026:** Moduli locali completati fino alla milestone 22; milestone 25 implementata su richiesta esplicita dell'utente, che dispone di numero dedicato e gruppo di prova. Gateway Baileys sperimentale esclusivamente locale, QR riservato all'amministratore assegnato, gruppo unico e associazioni PN/LID a persone esistenti. Comandi reali `!presenza si|forse|no`, `!aiuto`, `!menu [pagina]`, `!todo [pagina]`, impegni `!porto`, `!prendo`, `!fatto` con proposta personale e `!conferma` entro cinque minuti, e avvisi operativi con anteprima/conferma. Elenchi approvati dal sito con fingerprint di tutte le pagine legato a gruppo/revisione, ricevute persistenti e journal senza reinvii automatici; **pairing, ricezione reale e isolamento dei mittenti verificati dall'utente**. Review indipendenti positive, build senza avvisi, **441/441 test .NET e 30/30 Node**; collaudo browser con gateway finto su copia e smoke con Node reale ancora disconnesso riusciti. Dati delle 31 tabelle precedenti e credenziali conservati dopo migrazione additiva. Il simulatore continua a supportare presenze, aiuto, menu/attività con riferimenti brevi, `!porto`, `!prendo`, `!fatto`, sondaggi/voti e quiz/risposte. Lo scheduler ogni 30 secondi resta esclusivamente simulato, opt-in per evento e legato all'accesso dell'attivatore. Nessuna conversione della coda precedente o invio reale automatico. UI MVC/Bootstrap rifinita con Impeccable; Debug VS Code configurato. Quote dinamiche per i soli partecipanti, nessun pagamento reale. Host solo Development/loopback; nessuna AI esterna o deploy. Dettagli in `docs/MILESTONE_23.md` e `docs/MILESTONE_24.md`/`docs/MILESTONE_25.md`, procedura telefono in `docs/WHATSAPP_LOCAL.md`. Anteprima invio con nome esatto del gruppo, ID tecnico e blocco esplicito per bozze obsolete o destinatari non verificabili; invio manuale reale confermato dall'utente, nuovi impegni con conferma personale da provare sul telefono. L'utente richiede autonomia con conferme solo per dubbi bloccanti.

## Moduli funzionali

**Hosting Azure, 23 ottobre 2026:** su richiesta esplicita dopo i blocchi Oracle, creata VM gratuita Standard_B2pts_v2 ARM64 da 1 GiB in Svezia centrale, disco persistente P6 64GiB, log persistenti e swap 2 GiB. Web M32 ARM64 e gateway originale trasferiti da cold export del DB Oracle corrente con manifest/hash verificati; credenziali, chiavi, identita e storico conservati. Azure e ora l'unica fonte operativa, gateway connected sul gruppo originale anche dopo backup live. Web/Caddy/gateway/timer Oracle fermi e disabilitati, PC resta disabilitato. TLS, login, M32, desktop/mobile e sessione dopo restart verificati puntando direttamente ad Azure; DNS DuckDNS aggiornato a 4.223.75.149, TLS/login e desktop/mobile pubblici verificati senza override. Riavvio completo Azure con ripartenza automatica dei servizi, gruppo originale collegato e sessione login conservata. Nessun messaggio di prova. FreeTrial spendingLimit On conservato; IP nel credito iniziale e non gratuito permanente. Dettagli e incompatibilita recovery Ubuntu ARM risolta con drop-in circoscritto in `docs/AZURE_CLOUD.md`. Non riattivare la vecchia copia Oracle: eventuale ritorno richiede nuova copia coerente da Azure.

**M32 rilasciata sulla VM, 23 ottobre 2026:** rinomina nuclei con conferma/revisione/audit, identita e relazioni conservate, fingerprint originale per reinvii di creazione e migrazione additiva `20261023092207_AddHouseholdRenameFingerprint`. Compatibilita dei piani Alloggi M31 preservata; rinomina vera invalida conferme obsolete. Su indicazione esplicita utente il portale sospende Sondaggi, Giochi, Menu e Attivita: tab grigi senza href, GET/POST diretti 404, relativi comandi simulati bloccati e aiuto coerente. Altri moduli e comandi WhatsApp operativi invariati. Build pulita, 29 test mirati e **608/608 .NET**, EF allineato e review complessiva APPROVED. Release `m32` attiva con favicon SVG/ICO. Backup completo privato su VM e PC, rehearsal e migrazione sul DB remoto con 47 tabelle preesistenti e valori conservati. Verificati login originale, quattro moduli GET/POST 404, form rinomina, favicon, desktop/mobile e sessione dopo restart Web. Gateway recuperato sul gruppo originale anche dopo backup live; nessun messaggio di prova. Causa dei precedenti blocchi VM ancora non verificata. Dettagli in `docs/MILESTONE_32.md`.

**M31 rilasciata, 23 ottobre 2026:** modulo **Alloggi** disponibile sulla VM nella regia evento: appartamenti, camere/capienza, assegnazioni per persona o nucleo attivo, nuclei divisibili e camere condivise, schede stampabili riservate per nucleo o persona senza nucleo. Piano bozza/definitivo con fingerprint di presenze/nuclei e blocco del definitivo se mancano camere ai confermati. Build pulita, **579/579 test .NET**, 14 mirati e review indipendenti APPROVED su codice, pacchetto e script. Dopo il reboot effettuato dall'utente, backup completo privato su VM e PC, rehearsal su copia del DB remoto e migrazione `20261023072906_AddLodgingPlanning` applicata senza cambiare le righe delle 42 tabelle applicative/Identity e della tabella tecnica EF preesistenti. Release `m31`, HTTPS pubblico/login originali, Alloggi/stampa, antiforgery, desktop/mobile e sessione dopo restart verificati. Gateway collegato al gruppo originale anche dopo backup live; nessun nuovo comando o invio di prova. Causa del precedente blocco VM ancora non verificata. Dettagli in `docs/MILESTONE_31.md`.


**M30 in corso, aggiornata il 23 ottobre 2026:** gateway sulla VM e DB remoto unica fonte operativa; PC fermato e gateway locale disabilitato. Sessione/token/journal conservati. Build originaria pulita, 565 test .NET, 42 Node e 25 operativi Linux, review APPROVED. Dopo il reboot eseguito dall'utente e il rilascio M31, gateway collegato e gruppo autorizzato ripristinato, riavvio Web e backup completo con gateway attivo verificati. Nessun riavvio automatico o OOM rilevato nei controlli correnti. La causa dei blocchi SSH/HTTPS precedenti rimane non verificata: journal del boot precedente assente, diagnosi di stabilità ancora aperta. Non dichiarare M30 completata né riattivare il gateway PC. Stato in `docs/MILESTONE_30.md`.

**Hosting M29, 19 ottobre 2026:** su richiesta esplicita, servizio Production configurato sulla VM Oracle con SQLite copiato senza reinizializzazioni (45 tabelle verificate), HTTPS riservato via tunnel SSH, chiavi Data Protection persistenti e backup giornaliero con ripristino verificato. Gateway e scheduler esclusi in Production, WhatsApp continua solo sul PC: i database locale/server non sono sincronizzati. Build Release pulita, 552 test .NET, review APPROVED e browser Linux desktop/mobile positivo; dettagli in `docs/MILESTONE_29.md` e `docs/ORACLE_CLOUD.md`. Nessun dominio pubblico ancora fornito, certificato localhost autofirmato fino al 17/01/2027.

**Estensione pubblica successiva completata:** https://trobot-capodanno.duckdns.org operativo dopo l'apertura ingress Oracle confermata dall'utente. Caddy 2.11.7 con certificato pubblico Let’s Encrypt e rinnovi ACME, upstream HTTPS verificato, review APPROVED. Verificati TLS 1.3, redirect HTTP, autenticazione originale, sei pagine, cookie sicuri/CSRF, desktop/mobile, sessione dopo restart proxy e backup. Nessuna modifica al codice C# o ai dati per questa estensione; gateway continua sul PC e i database restano indipendenti. Tunnel privato conservato come alternativa. Certificato interno localhost da rinnovare separatamente prima del 17/01/2027.

M28 aggiunge `!presenza modifica` (arrivo/durata/entrambi con conferma), `!riepilogo gg/mm[/aaaa] [pagina]`, scadenza informativa facoltativa e preparazione di promemoria manuali dalla pagina **Soggiorni**. La risposta individuale e le modifiche del genitore includono i bambini attivi collegati tramite **Accompagnatore di**, con le stesse date; gli altri adulti rispondono per sé. I bambini non compilano date indipendenti. Snapshot dall'avvio della modifica e del nucleo effettivo contro conferme obsolete. Scadenza inizialmente vuota, risposte tardive ammesse; promemoria senza nomi o dati personali come bozza verso l'anteprima/gruppo e la conferma d'invio esistenti. Nessun invio automatico o modifica alle quote; dettagli in `docs/MILESTONE_28.md`.

`!riepilogo [pagina]` mostra nel gruppo il conteggio delle presenze confermate, adulti/bambini secondo appartenenze attive, dettaglio dei nuclei con bambini inclusi e aggregati degli altri stati/soggiorni mancanti. Persone prive di classificazione attiva incluse con età non specificata; ciascuna conta una volta, anche senza identità WhatsApp. Massimo cinque nuclei per pagina, nessun nome individuale o data personale. Comando informativo su richiesta con controlli e ricevute esistenti; nessuna migrazione. `!aiuto` resta per ora dedicato a `!presenza` per scelta dell'utente.

M26 aggiunge il dialogo reale `!presenza si` → arrivo → giorni inclusivi → conferma individuale/familiare, con nomi e frasi simpatiche, risposte brevi per mittente e `!annulla`. Solo adulti del nucleo attivo possono confermare le stesse date per tutti; sessione persistente di 15 minuti, controlli del collegamento e snapshot contro conferme obsolete. Nuova vista riservata **Soggiorni**, raggiungibile dalla scheda evento, con giorni/notti e conteggi giornalieri; dati precedenti senza date segnalati da completare. Nessun cambio alle quote. Build pulita, 472 test .NET/35 Node e review indipendenti positive; migrazione additiva collaudata su copia e originale con 39 tabelle preesistenti e credenziali invariate. Prova del nuovo dialogo dal telefono ancora da confermare. Dettagli in `docs/MILESTONE_26.md`.

I testi di menu e attività nel gruppo e nel simulatore usano ora blocchi separati, emoji, quantità/stato e riferimenti leggibili; aiuto e proposte reali hanno istruzioni su righe distinte. Il formato condiviso conserva cinque elementi per pagina e il limite WhatsApp di 2000 caratteri UTF-16, senza nomi degli incaricati o note. Dopo questo aggiornamento serve riapprovare i due elenchi dalla pagina WhatsApp: il fingerprint include le pagine esatte. Il collegamento del dispositivo non cambia.

Lo scenario sintetico Capodanno al Casale (20 persone iniziali, otto nuclei e moduli popolati) resta documentato in `docs/SCENARIO_CAPODANNO_2027.md` e nel seeder ripetibile. Il 23 ottobre 2026 e stato rimosso dall'ambiente operativo Azure su richiesta esplicita; non ricrearlo sul DB operativo. Dati specifici e storico dello scenario conservati nel backup privato sulla VM; dati del reale, People e Identity conservati.
Operazione completata il 23 ottobre 2026: rimosso lo scenario `ce014f6d-1a90-4388-b187-771235bcb918` dopo trasferimento WhatsApp su **Capodanno 2027**, `fc3dfb11-fbcf-46e9-b677-9521e1c07f03` (35 partecipanti). Dan/Tro confermati dall'utente e quattro alias identici verificati; gruppo originale e sessione conservati. Backup privato sulla VM, rehearsal e applicazione transazionale con tutte le righe esterne conservate, integrita/FK OK; backup live e recovery finali positivi. 620 test .NET e review APPROVED. Nessuna funzione generale di eliminazione eventi nel portale; Organizzatori > Revoca evento modifica solo l'assegnazione. Dettagli in `docs/SCENARIO_REMOVAL.md`.

### 1. Partecipazioni
Persone, famiglie/nuclei, bambini, accompagnatori esterni anche senza account WhatsApp. Stati: NoResponse, Confirmed, Undecided, Absent. Storico modifiche. Esempio `!presenza si 2 adulti 1 bambino`: il conteggio adulti include il mittente; chiedere chiarimenti in caso di ambiguità. Identità WhatsApp collegabile a persona, ma non obbligatoria per accompagnatori.

### 2. Sondaggi e decisioni
Bozza -> anteprima -> conferma organizzatore -> coda invio -> conferma pubblicazione -> votazioni -> chiusura -> risultato e, se previsto, decisione ufficiale. Voto modificabile fino a scadenza; unicità del voto per avente diritto; idempotenza messaggi; definire policy per voto familiare/accompagnatori e gestione delle identità senza WhatsApp.

### 3. Menu e bevande
Elementi, categorie, quantità, responsabili, assegnazioni e disponibilità. Partecipanti possono offrirsi di portare cose dal gruppo. Avvisi solo quando servono.

### 4. Spese, quote e pagamenti
Quote uguali con pesi personalizzabili: adulto default 1; bambino configurabile (es. 0, 0.5, 1); eccezioni individuali. Solo spese approvate nei totali. Dichiarazione spesa diversa da pagamento effettuato. Pagamenti con stati (Declared, Confirmed, Cancelled). Calcoli con `decimal`, gestione centesimi/arrotondamenti e riconciliazione verificabile. Niente pubblicazione automatica di saldi individuali nel gruppo.

### 5. Attività e scadenze
Attività con responsabili, priorità, stati, scadenze e promemoria. Stato: Todo, InProgress, Completed, Cancelled.

### 6. Intrattenimento
Quiz, sfide, countdown, classifiche ironiche, «Premio Paccaro dell'Anno», «Tribunale del Capodanno», premi post-serata. Partecipazione volontaria, possibilità di escludersi dai giochi, evitare prese di mira ripetute.

## Comandi previsti (da raffinare progressivamente)
`!aiuto`, `!presenza si|forse|no`, `!presenti`, `!sondaggi`, `!sondaggio crea`, `!vota <id> <opzione>`, `!miovoto`, `!menu`, `!porto`, `!budget`, `!spesa`, `!todo`, `!stato`, `!sentenza`, `!classifica`, `!admin aggiungi`, `!config`, `!sondaggio chiudi`, `!spesa approva`, `!annulla`, `!silenzio`, `!riprendi`, `!sarcasmo 5`, `!serio`, `!conferma <id>`. Alcuni sono riservati agli organizzatori. Non implementare tutti nella prima milestone.

## Interfaccia amministrativa — pagine previste
- Dashboard: confermati, indecisi, sondaggi aperti, attività, budget, avvisi «Serve il tuo intervento».
- Eventi: data, luogo, programma, configurazione.
- Partecipanti: anagrafiche, famiglie, accompagnatori, quote e stati.
- Sondaggi: creazione, opzioni, scadenze, anteprima, pubblicazione, risultati.
- Menu: piatti, bevande, quantità, assegnazioni.
- Spese e pagamenti: approvazioni, ripartizioni, anticipi, saldi, rimborsi.
- Attività: incarichi, priorità, responsabili, scadenze.
- Intrattenimento: giochi, quiz, classifiche, tono.
- Messaggi: compositore, anteprima, coda, programmazione, esiti invio, storico tecnico.
- Impostazioni: amministratori, permessi, WhatsApp, AI, quiet hours, sarcasmo, sicurezza.

## Flussi end-to-end da preservare
1. Messaggio WhatsApp -> gateway -> validazione gruppo/mittente -> deduplica -> router comando -> application service -> database -> reazione o risposta -> aggiornamento dashboard.
2. Creazione sondaggio nel sito -> bozza -> anteprima -> conferma -> outbox -> gateway -> esito pubblicazione -> votazione nel gruppo -> risultati nel sito -> chiusura e annuncio.
3. Dichiarazione spesa nel gruppo -> interpretazione e conferma -> spesa pending -> approvazione nel sito -> contabilità aggiornata -> conferma non sensibile nel gruppo.
4. Scheduler -> calcolo prossima frequenza in base a data evento -> filtro quiet hours/rate limit/novità -> outbox -> gateway -> log esito.

## Architettura proposta
```
T.Robot/
├── TRobot.sln (o .slnx, secondo tooling)
├── src/
│   ├── TRobot.Web/                # MVC + area Admin + API endpoints + hosted services
│   ├── TRobot.Application/        # Use cases, DTO, command router, workflow, interfaces
│   ├── TRobot.Domain/             # Entità, enum, regole senza dipendenze infrastrutturali
│   └── TRobot.Infrastructure/     # EF Core, SQLite, repo, messaging/AI adapters
├── gateway/
│   └── TRobot.WhatsApp/           # Node.js + Baileys (fase successiva)
├── tests/
│   └── TRobot.Tests/              # xUnit
├── docs/
│   └── PROJECT_CONTEXT.md
└── AGENTS.md
```
Riferimenti: Web -> Application + Infrastructure; Application -> Domain; Infrastructure -> Application + Domain; Domain -> nessuno; Tests -> progetti testati. Non accoppiare Domain a MVC, EF o Baileys.

## Entità candidate (non tutte subito)
Event; Person; EventParticipant; EventOrganizer (o EventRoleAssignment); Family/Household; Poll; PollOption; PollVote; Decision; MenuItem; MenuAssignment; Expense; ExpenseShare; Payment; Task; TaskAssignment; EntertainmentEvent; BotConfiguration; ConversationSession; PendingAction; ScheduledJob; OutboundMessage; IncomingMessage; AuditLog. Separare ID interno, WhatsApp sender ID e gruppo ID. Usare timestamp UTC e data/ora evento nel fuso locale; vincoli univoci e indici utili. Definire cascade delete e gestione privacy.

## Autenticazione e sicurezza
Sito accessibile solo ad amministratori autorizzati; introdurre autenticazione prima del deploy. Gateway protetto da secret/autenticazione interna, validazione di gruppo e mittente, deduplica message ID, rate limit, audit per modifiche sensibili. Non esporre segreti nel repository. Non attivare automazioni di invio reali in sviluppo. Ogni modifica amministrativa deve essere validata lato server.

## Prima milestone richiesta dall'utente
**Creare soluzione .NET e database, passo passo.** Obiettivo verificabile successivo: simulare `!presenza si`, salvare partecipazione nel DB e visualizzare il conteggio nella dashboard. Per iniziare:
1. Ispezionare directory e SDK `dotnet --info`; verificare se esiste già una soluzione, senza sovrascrivere file.
2. Se repository vuoto, scaffolding di Web MVC, Domain, Application, Infrastructure, Tests; riferimenti corretti.
3. Configurare SQLite/EF Core con versioni coerenti con SDK; `DbContext` e mapping per Event, Person, EventParticipant, EventOrganizer; considerare schema minimo senza sovraingegneria.
4. Aggiungere migrazione iniziale e documentare `dotnet ef database update`; verificare che DB si crei.
5. Test di base per regole di partecipazione e smoke test avvio web.
6. Eseguire build/test e riportare esito, file modificati e comandi per avvio.

## Decisioni ancora aperte (non bloccare scaffolding)
Ambiente IDE e SDK effettivi; hosting locale/VPS; autenticazione (Identity/cookie, eventuale Microsoft login); chi può votare per accompagnatori; politiche esatte di pubblicazione dei dettagli di spesa; sarcasmo predefinito; scelta AI provider; affidabilità/limiti gateway WhatsApp. Scegliere default ragionevoli per prototipo e annotarli in `docs/DECISIONS.md`.

## Criteri di qualità
Codice compilabile, migrazioni ripetibili, test significativi, naming coerente `T.Robot` ovunque, niente dati demo spacciati per reali, nessun invio WhatsApp reale nella prima milestone. Separazione tra dominio, application e infrastruttura; API/web e gateway intercambiabili; no operazioni distruttive senza conferma.
