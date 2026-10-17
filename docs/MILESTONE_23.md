# Milestone 23 — Primo collegamento WhatsApp sperimentale

## Obiettivo e autorizzazione

Il 17 ottobre 2026 l'utente richiede l'integrazione WhatsApp e conferma di avere un numero dedicato e un gruppo di prova. Questa richiesta autorizza il gateway sperimentale locale, superando l'esclusione precedente per questo incremento. Nessun deploy, servizio di sistema, AI esterna, DM o invio indiscriminato.

Nuova integrazione fra moduli esistenti e gateway Node: il disegno viene documentato prima dell'implementazione secondo brainstorming, con sviluppo e review indipendenti. La modalità autonoma autorizzata evita approvazioni intermedie del disegno. Il collegamento del dispositivo richiede comunque la scansione del QR da parte dell'utente.

## Primo flusso verificabile

1. Organizzatore autenticato, amministratore globale e assegnato all'evento apre la pagina WhatsApp e richiede il collegamento. Il gateway crea la sessione e il QR appare solo nella pagina riservata, mai nei log.
2. L'utente scansiona con il numero dedicato, sceglie il gruppo di prova e ne conferma l'attivazione. Solo un gruppo/evento può essere attivo per questo gateway.
3. I membri del gruppo vengono associati esplicitamente a partecipanti già esistenti nell'evento. Nessuna importazione automatica, duplicazione o sostituzione delle identità simulate/storiche.
4. Un messaggio nuovo `!presenza si|forse|no` dal gruppo e da un'identità associata aggiorna la presenza attraverso il servizio applicativo. Ricevuta tecnica e audit atomici, deduplica e controllo attuale dell'accesso dell'attivatore. La conferma è una reazione generica al messaggio, senza nomi o saldi.
5. Un organizzatore prepara un nuovo messaggio operativo reale, vede l'anteprima e conferma l'invio al gruppo attivo. La coda reale è separata dalla simulazione: vecchi messaggi Sent/Queued simulati, riepiloghi e pubblicazioni di sondaggi/quiz non vengono convertiti o inviati automaticamente.

Gli altri comandi restano disponibili nel simulatore e non vengono eseguiti implicitamente come azioni reali in questa prima milestone. Il collegamento e la prova reale precedono l'estensione dei flussi complessi e delle loro conferme.

## Scelte tecniche

- Baileys `7.0.0-rc14`, versione verificata sulla registry npm ufficiale e nel repository dei manutentori il 17 ottobre. È una prerelease: dipendenza fissata e lockfile versionato, nessuna promessa di affidabilità o approvazione da WhatsApp. L'API ufficiale non viene assunta compatibile con il gruppo ordinario esistente; la scelta del gateway sperimentale era già candidata nel contesto.
- Node 24 LTS portabile in `.local`, senza installazione globale. Stato autenticazione, token e configurazione soltanto in `.local/whatsapp`, esclusi da Git. Nessun numero reale o token negli esempi versionati.
- HTTP esclusivamente loopback: Web `127.0.0.1:5238`, gateway `127.0.0.1:5240`. Autenticazione bearer a token casuale condiviso, confronti a tempo costante, limiti ai body, nessun endpoint pubblico anonimo del gateway, nessun URL arbitrario o CORS.
- Configurazione locale `.local/whatsapp/gateway.json`, sezione `WhatsAppGateway`: `Enabled`, `BaseUrl`, `Token`, `CallbackUrl`, `StateDirectory`. Setup idempotente senza sovrascrivere credenziali già presenti. Il gateway avvia solo il server HTTP: apre la connessione WhatsApp soltanto su richiesta esplicita dalla pagina.
- Gruppo selezionato verificato sia nel gateway sia in Application. Scartati DM, altri gruppi, messaggi propri, append/history, allegati e messaggi anteriori all'attivazione. Nessun download/salvataggio dell'intera cronologia.
- Alias mittente PN/LID gestiti come identità verificate dal gateway; in DB si conservano fingerprint HMAC e riferimenti alle persone, non numeri o testo della chat. Alias che risolvono a persone differenti vengono rifiutati. Non si riscrivono `Person.WhatsAppSenderId` né ricevute precedenti.
- Presenze reali con ricevute dedicate, distinte dalla simulazione. Timestamp UTC e comandi limitati a 100 caratteri. Attore delegante attuale ed evento rivalidati nella transazione. Ricevute senza contenuto della chat e audit riconoscibile WhatsApp.
- Invio reale manuale separato: Draft → Sending → Sent oppure Uncertain. Un timeout/esito ambiguo non viene ritentato automaticamente; il gateway conserva un journal tecnico per ID operazione prima dell'invio. Richieste ripetute identiche non inviano nuovamente e payload diversi con lo stesso ID sono conflitti. Non si può garantire exactly-once su un trasporto esterno: l'ambiguità deve restare visibile.
- Lo scheduler locale della milestone 22 rimane esclusivamente simulato; nessun background worker lo collega al trasporto reale.

## Contratto del gateway

Tutte le route richiedono `Authorization: Bearer <token>`. JSON camelCase. Errori con codici tecnici, senza eccezioni, JID o contenuti privati nei log.

| Route Node | Richiesta / risposta |
| --- | --- |
| `GET /health` | `{ status, qrDataUrl, activeGroupId }`, status disconnected/connecting/qr/connected/error |
| `POST /connect` | Avvio esplicito socket; risposta status come health |
| `POST /disconnect` | Chiude socket senza cancellare la sessione o fare logout |
| `GET /groups` | `{ groups: [{ id, name }] }` solo a socket collegato |
| `GET /groups/{groupId}/members` | `{ members: [{ id, alternateIds, label }] }`, metadati del solo gruppo richiesto |
| `POST /group` | `{ groupId: string|null }`, imposta/scarta il solo gruppo ammesso e l'istante di attivazione |
| `POST /send` | `{ operationId, groupId, text }` → `{ status: sent|uncertain, messageId }`; max 1000 caratteri, journal idempotente |

Callback Node → Web: `POST /internal/whatsapp/incoming` con lo stesso bearer e `{ groupId, senderIds: [canonical, alternate?], messageId, text, timestampUnixSeconds }`. Application risponde `{ handled, duplicate, reaction }`; solo `handled && !duplicate` può produrre la reazione sul messaggio originale. Mittente sconosciuto o comando non supportato non produce messaggi di gruppo.

## Verifiche

Test senza account reale per filtro messaggi, isolamento gruppo, alias, autorizzazioni/revoche, deduplica e rollback, journal/esiti incerti, endpoint e anti-CSRF. Migrazioni integrate sequenzialmente e provate prima su copia SQLite; baseline di dati e credenziali originali conservata. Pairing e messaggi reali non vengono dichiarati riusciti senza prova effettiva.

Fonti primarie: [repository Baileys](https://github.com/WhiskeySockets/Baileys), [package ufficiale](https://registry.npmjs.org/@whiskeysockets%2fbaileys/latest), [rilasci Node.js](https://nodejs.org/dist/index.json). La documentazione Meta sui gruppi non era recuperabile durante l'ispezione: nessuna affermazione non verificata sui suoi limiti viene usata come presupposto tecnico.

## Esito dell'implementazione

- Sviluppatori dedicati .NET e Node, revisore indipendente diverso da entrambi. Review finale positiva dopo i fix della concorrenza invio/cambio gruppo, logging HTTP e identificazione dei membri con metadata reali rc14. L'invio tiene il gate del dispositivo dal claim fino alla registrazione esito; collegamento e scelta gruppo rivalidano l'attore dopo l'attesa.
- Build completa senza errori o avvisi; **401/401 test .NET**, compresi **21 mirati WhatsApp**, e **14/14 test Node**. Modello EF senza modifiche pendenti. `npm ci` riuscito e import effettivo di Baileys/qrcode/pino e modulo WASM verificato senza creare socket WhatsApp.
- Migrazione `AddExperimentalWhatsApp` con quattro tabelle additive, prima applicata sulla copia. Conservati backup consistente e baseline delle 31 tabelle precedenti e delle credenziali in `.local`. Nessuna conversione della coda simulata o sovrascrittura dei mittenti storici.
- Browser Chromium sulla copia SQLite e gateway HTTP finto: otto verifiche superate (accesso/stato iniziale, CSRF, gruppo/mapping PN-LID, presenza/deduplica, filtri callback, anteprima/conferma/non reinvio, desktop/mobile senza overflow, disconnessione). Nessun errore JavaScript. Report e screenshot in `.local/ui/whatsapp-*`; questi test non sono messaggi realmente inviati a WhatsApp.
- Gateway effettivo avviato su `127.0.0.1:5240`: health anonima 401, health autenticata `disconnected`, nessun QR o gruppo attivo. Nessuna connessione WhatsApp viene avviata dallo startup.
- Migrazione applicata anche all'originale dopo le verifiche: baseline delle 31 tabelle e credenziali invariata, integrity/FK OK. Smoke autenticato sul vero host: tutti i moduli, nuova pagina WhatsApp e stato ottenuto tramite il client .NET dal Node effettivo, CSS/no-store/logout riusciti. Nessuna mutazione business o attivazione di gruppo sull'originale.

**Passaggio ancora necessario:** scansione del QR con il numero dedicato, selezione del gruppo, associazione di un membro e prova `!presenza` dal suo telefono. Nessun pairing o invio reale è dichiarato riuscito. Procedura riproducibile in [WHATSAPP_LOCAL.md](WHATSAPP_LOCAL.md).

## Correzione durante il collaudo reale

L'utente ha successivamente collegato l'account aziendale, selezionato un gruppo di prova con tre numeri e associato i due mittenti di prova. Il gateway risulta connesso, il gruppo coincide con il binding DB, l'attivatore è autorizzato e gli alias corrispondono ai membri. La prima prova `!presenza si` non ha prodotto ricevute.

I sorgenti Baileys rc14 installati (`lib/Utils/decode-wa-message.js`) mostrano che `senderKeyDistributionMessage` viene elaborato ma resta insieme al testo nel messaggio decifrato. Il filtro precedente respingeva questa struttura. La correzione accetta soltanto metadati crittografici/context e un contenuto testuale, anche dentro massimo cinque wrapper effimeri; media, modifiche, protocollo e view-once restano esclusi. Un test riproduce il caso reale; ulteriori test verificano esclusioni e privacy della diagnostica.

Review indipendente positiva e **17/17 test Node superati** dal coordinatore. Nessun cambiamento .NET o schema: la suite .NET rimane quella precedente di 401 test superati. Contatori tecnici aggregati solo RAM nella health autenticata distinguono scarto, callback ed esito della reazione, senza testo, identità o eccezioni. Gateway riavviato conservando l'autenticazione, sessione riconnessa e stesso gruppo già autorizzato ripristinato; nessun messaggio di prova inviato dal coordinatore. La conferma della causa dell'episodio e del funzionamento richiede un nuovo comando dal telefono dell'utente.

L'utente ha poi confermato reazione ✅, cambio presenza e prova dal numero dell'amico con aggiornamento del solo partecipante associato. Ricezione reale e isolamento dei mittenti sono quindi verificati, senza invii di prova del coordinatore.

## Nome del gruppo nell'anteprima

Su richiesta dell'utente, l'anteprima risolve il nome attuale del gruppo per l'esatto ID memorizzato nel messaggio. Il nome è mostrato in evidenza, l'ID tecnico resta secondario. Il nome non è persistito e non cambia la destinazione. La lettura è autorizzata per evento, restituisce soltanto il nome pertinente ed è disponibile anche per organizzatori assegnati non amministratori; liste generali, QR e gestione del dispositivo restano riservati.

Il confronto in sola lettura sul runtime ha confermato corrispondenza fra gruppo DB e gateway e disponibilità del nome. Ha anche individuato una bozza del medesimo gruppo ma di revisione precedente: resta leggibile con il nome corretto, senza pulsante di invio e con istruzione di preparare una nuova bozza. Offline, metadata mancanti/ambigui, gruppo diverso o attivatore non più autorizzato non producono un nome alternativo né abilitano l'invio. Il controllo applicativo gruppo/revisione sotto gate e la whitelist Node non cambiano. Nessuna migrazione o messaggio reale inviato per questa verifica.

Review indipendente positiva, build senza errori o avvisi e **31/31 test WhatsApp** superati, inclusi dieci nuovi casi per anteprima, escaping HTML, organizzatore non amministratore, mancata risoluzione del nome, pin diverso, revisione precedente e storico oltre le ultime trenta righe. Collaudo browser in sola lettura sul vero host: nome risolto per il gruppo esatto, ID visibile, assenza del pulsante per la bozza obsoleta, desktop/mobile senza overflow e nessun cambio stato/invio. Report locale `.local/ui/whatsapp-preview-report.json`.

Suite completa conclusiva: **411/411 test .NET superati**, zero ignorati. Comando `dotnet test TRobot.sln --no-build --no-restore --logger 'trx;LogFileName=whatsapp-preview-final.trx' --results-directory .local/test-results`; rapporto locale conservato. Node resta alla verifica precedente di 17/17, senza modifiche in questo intervento. Sito riavviato sul database originale, gateway/sessione non riavviati né modificati.
