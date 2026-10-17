# Presenza guidata e soggiorni

## Obiettivo e perimetro

Richiesta dell'utente: nel gruppo, dopo `!presenza si`, chiedere nome/data di arrivo, durata e, per un nucleo familiare, se confermare per tutti. Domande brevi, simpatiche e con emoji. Primo incremento dedicato alla presenza; la spesa sarà definita successivamente. Nessuna AI esterna, DM, invio di prova automatico o deploy. Autonomia autorizzata; brainstorming completato con disegno in chat e questa specifica.

## Flusso e default

- Conservare l'effetto già collaudato di `!presenza si`: conferma subito il solo mittente e avvia/reinizializza la raccolta dei dettagli, senza cancellare soggiorni precedenti. La risposta diventa una domanda nominativa anziché la sola reazione.
- Domandare prima arrivo (`gg/mm` oppure `gg/mm/aaaa`), poi giorni (intero 1–31). Giorni inclusivi: 30/12 + 4 giorni = ultimo giorno 02/01, tre notti. Senza anno scegliere la data più vicina all'inizio evento fra anno precedente/corrente/successivo, nel fuso dell'evento; non dedurre date dal programma libero. Date ammesse entro 31 giorni prima/dopo la data locale iniziale dell'evento, limite esplicitato negli errori. Non aggiungere un inesistente orario di partenza.
- Dopo la durata mostrare riepilogo. Per adulti di un nucleo attivo con altri membri chiedere «Confermi le stesse date per tutta la famiglia [nome]? Sì per tutti, no solo per te». Per gli altri chiedere conferma del proprio soggiorno. Non elencare nomi dei familiari o classificazioni dei minori nel gruppo.
- Conferma familiare solo da adulto attivo dello stesso nucleo, solo membri attivi già partecipanti all'evento, stato Confirmed e stesse date per tutti. Nessuna nuova persona creata. Un minore può compilare la propria presenza ma non confermare altri. Se il nucleo o una presenza/soggiorno interessati cambiano dopo il riepilogo, rifiutare la conferma obsoleta e chiedere di ricominciare.
- `!presenza no|forse` conserva il comportamento individuale, chiude il dialogo pendente e non cambia la famiglia. Soggiorni storici conservati, esclusi dai conteggi se il partecipante non è confermato. `!annulla` interrompe solo la raccolta dettagli, senza revocare la presenza o cancellare soggiorni già registrati. `!presenza si` consente di correggere ripartendo.
- Le risposte possono essere brevi (`30/12`, `4`, `sì`, `no`) solo durante il dialogo del medesimo mittente/evento/gruppo/attivatore/revisione. Durata massima 15 minuti dall'avvio, senza estensioni indefinite. Ignorare chat normale e risposte fuori sessione. Una domanda invalida non avanza lo stato; una risposta breve riconoscibile ma non valida produce istruzioni per correggere.
- Il gateway inoltra soltanto i comandi ammessi e un insieme ristretto di risposte candidate (date numeriche, numeri brevi, sì/no), mantenendo tutti i filtri, limiti, deduplica e whitelist di gruppo esistenti. L'Application decide se esiste una sessione autorizzata. Non inoltrare tutto il testo della chat.

## Persistenza e autorizzazione

Domain indipendente; soggiorni strutturati e sessioni persistenti con migrazione additiva, senza ricreare il DB. Un solo dialogo corrente per persona/evento; sessione legata a gruppo, attivatore e revisione. Non conservare testo libero delle risposte o nomi nelle sessioni/ricevute; solo dati strutturati, HMAC e metadati tecnici. Invalidare le bozze dopo scadenza/cambio collegamento; preservare storico e ricevute pregresse.

Ogni passaggio passa dal servizio Application, con controlli ripetuti dentro la transazione, mittente associato univocamente, ricevute HMAC idempotenti e collisioni tra tipi di comandi rifiutate. Invio risposta con operation ID persistente e journal esistenti: nessun reinvio su esito incerto. Salvataggio finale soggiorni, eventuali stati familiari, audit e ricevuta atomico; aggiornamenti concorrenti non devono sovrascriversi silenziosamente. Storico strutturato delle modifiche ai soggiorni e presenze familiari. Nessun dato personale nuovo in riepiloghi automatici pubblici; il nome del mittente e del proprio nucleo nelle domande sono richiesti esplicitamente dall'utente e vanno neutralizzati per formattazione WhatsApp.

## Sito e collaudo

Vista riservata agli organizzatori con persona, stato, arrivo, giorni, ultimo giorno e notti; indicare i confermati con dettagli mancanti. Conteggi giornalieri dei soggiorni confermati e completi, senza inventare date per dati storici. Accesso coerente con i permessi evento. Nessun cambio alle quote contabili in questo incremento.

Verificare dialogo completo singolo/famiglia, ramo solo mittente, isolamento di due mittenti, anno nuovo/fuso, errori data/durata, conferma/restart/annullamento, scadenza, perdita permessi, repin, duplicati/collisioni e concorrenza, conservazione storico e privacy. Build e test .NET/Node; review separata prima della migrazione sull'originale. Migrazione e prove prima su copia SQLite; backup consistente e confronto delle righe preesistenti/credenziali prima dell'originale. Nessun invio reale di collaudo; browser in sola lettura.

## Esito

Implementazione completata con `gpt-6.1-sol` medium per core e gateway. Review indipendenti APPROVED: high per core/autorizzazioni/concorrenza, medium per la modifica circoscritta alla whitelist gateway. Build soluzione senza errori/avvisi; 87 test mirati, suite completa 472/472 .NET e 35/35 Node. Inclusi callback HTTP con risposta breve, isolamento e concorrenza fra adulti dello stesso nucleo, minori e nomi con markup.

Migrazione `20261017143515_AddAttendanceDialoguesAndStays` con sole tre tabelle nuove e relativi indici, prima sulla copia poi sull'originale: tutte le righe delle 39 tabelle precedenti e credenziali invariate, integrità e foreign key verificate. Backup locale consistente `.local/trobot-before-m26.db`. Browser autenticato/anonimo e mobile positivo sulla vista soggiorni, senza generare presenze, sessioni o invii. Host e gateway aggiornati, stesso gruppo già autorizzato ripristinato. Nessun soggiorno inventato per i partecipanti esistenti; dettagli mancanti espliciti. La prova del nuovo dialogo con telefono resta da effettuare dall'utente.
