# Milestone 15 — Sondaggi locali

Flusso completo riservato agli organizzatori: bozza, anteprima, conferma pubblicazione, outbox simulata, voto dopo Sent, chiusura, risultato ed eventuale decisione esplicita. Il sito resta Development/loopback; nessun gateway o invio reale.

Domain: Poll, PollOption, PollVote e PollVoteReceipt. Application: PollService su LocalOperations transazionale, DTO condiviso LocalParticipation. Infrastructure: mapping con riferimenti composti, unicità del voto e Snapshot con numero reale di sondaggi aperti. Web: PollsController e viste Index/Edit/Details con POST anti-CSRF e revisione obbligatoria. DI e collegamento nella scheda evento attivi.

Le opzioni sono fisse dalla creazione. Scadenza UTC inserita e mostrata nel fuso evento; orari DST ambigui o inesistenti rifiutati. Un adulto confermato ha un voto modificabile fino alla scadenza, registrato dall'organizzatore come simulazione con conferma; nessuna delega di nucleo. Ricevute immutabili rendono innocui i reinvii di voti precedenti anche dopo cambi voto o chiusura. Voti accettati conservati anche in caso di successive modifiche alla presenza o alla classificazione.

Annullamento prima dell'invio Sent conserva tutte le righe e annulla l'outbox; dopo Sent si usa la chiusura. Close congela i conteggi senza annunciare nulla. L'organizzatore può selezionare una decisione senza automatismi; una seconda anteprima con conferma esplicita accoda il risultato aggregato. Titolo e opzioni sono gli unici testi liberi approvati per il gruppo; nessun nome o voto individuale nell'outbox.

Test: invarianti e workflow, eleggibilità, idempotenza dopo cambio voto/chiusura, scadenza, revisione e precisione temporale, accessi revocati/disabilitati, riferimenti estranei, annullamento, rollback di voto/ricevuta/audit, snapshot, privacy e HTTP autenticazione/CSRF/codifica testo. Upgrade da SimulatedOutbox con messaggio Sent e storico sintetico preservati, controllo FK e migrazione ripetuta. Build, migrazione e suite integrate dal coordinatore.

Smoke in sola lettura dopo login: GET /Polls?eventId=<evento>, GET /Polls/Create?eventId=<evento>, GET /Polls/Details?eventId=<evento>&id=<sondaggio> per una riga già presente. La pagina elenco mostra lo stato persistito; Details deriva Aperto da Sent e scadenza e collega Coda e simulazione invii.

## Esito integrazione

Review indipendente positiva, build con zero errori e avvisi e 265 test superati nella suite completa. La prima verifica ha rilevato una proiezione DTO non traducibile da EF: corretta ordinando prima della proiezione; ricontrollata dal revisore. Migrazione `LocalPolls` applicata al database locale; dati precedenti invariati e controlli integrità/chiavi esterne OK, inclusa la ricostruzione SQLite della chiave alternativa outbox.
