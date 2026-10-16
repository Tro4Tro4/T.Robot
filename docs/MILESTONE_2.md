# Milestone 2 — Presenza simulata

## Obiettivo e perimetro

Un organizzatore accede al sito locale, seleziona un partecipante di prova e invia `!presenza si`. Application verifica comando, identità e autorizzazione per evento. Infrastructure aggiorna SQLite e la dashboard mostra il nuovo conteggio. Nessun messaggio reale, AI o sito per i partecipanti.

## Flusso

1. Preparazione esplicita da console con `--initialize-demo`, su database migrato e vuoto. Il servizio Application crea un evento chiaramente dimostrativo, un organizzatore e un partecipante con mittente sintetico. Non modifica database già popolati.
2. Generazione crittografica di un codice casuale locale: solo hash PBKDF2 nella configurazione; copia del codice in `.local/access-code.txt` per l'operatore. Entrambi i file sono esclusi da Git e fuori da `wwwroot`.
3. Accesso via codice e cookie ASP.NET Core, durata 30 minuti senza rinnovo automatico. Anti-CSRF su login, logout e simulazione; massimo cinque tentativi di login al minuto per processo. Solo Development e loopback come nella milestone precedente.
4. `AttendanceService` ricava l'organizzatore dai claim del cookie, senza accettarlo dal modulo; verifica il ruolo `EventOrganizer` per l'evento. Il mittente deve essere già collegato a un partecipante dell'evento. Nessuna iscrizione implicita.
5. Parser deterministico: accetta soltanto `!presenza si` e `!presenza sì`, ignorando maiuscole e spazi separatori. Richieste con conteggi, accompagnatori o altri stati sono respinte senza cambiamenti.
6. Stato e ricevuta vengono salvati nella stessa transazione. Ricevute univoche per `(EventId, MessageId)`; nessun testo originale conservato. Stesso ID e stessa persona/stato: duplicato ignorato. Stesso ID e persona/stato diversi: conflitto. Un nuovo ID per una persona già confermata registra l'elaborazione senza incrementare il conteggio.
7. Dashboard con eventi assegnati, cinque conteggi (incluso il totale), modulo e messaggio di esito. POST seguito da redirect in caso di successo. Gli errori di validazione conservano il messaggio e l'ID per correggere/reinviare.

## Dati e responsabilità

- Domain: transizione validata di partecipazione, timestamp UTC e ricevuta di elaborazione.
- Application: parsing, autorizzazione per evento, orchestrazione, deduplica e bootstrap locale esplicito.
- Infrastructure: query autorizzate dal servizio, mapping EF e transazioni SQLite; nessuna regola di comando nei controller.
- Web: autenticazione locale, anti-CSRF, modelli del modulo e rendering. Il cookie identifica l'organizzatore; la verifica nel DB decide l'accesso a ogni evento.
- Migrazione incrementale: `UpdatedAtUtc` facoltativo sulle partecipazioni e tabella `ParticipationReceipts`, con chiavi esterne verso partecipazione e organizzatore. Nessuna riscrittura della migrazione iniziale.

## Verifica prevista

Test di parsing, autorizzazione per evento, mittente non valido, conferma persistita, conteggi, reinvio dopo nuovo DbContext, conflitto sul MessageId, salvataggio atomico e rollback dopo errore. Test HTTP con SQLite reale in memoria, cookie reale e anti-CSRF: accesso anonimo, codice errato, limite tentativi, invio, reinvio, ID evento contraffatto e logout. Applicazione e ripetizione della migrazione sul DB locale; smoke test HTTP su loopback.

## Limiti deliberati

La credenziale locale serve alla demo e rappresenta un solo organizzatore; non è il sistema multiutente definitivo. La produzione resta disabilitata. Nessun gateway, deduplica di trasporto o validazione di gruppi WhatsApp reali. Le ricevute conservano solo metadati minimi; la policy operativa di retention/anonimizzazione va definita prima di impiegare dati reali. Nessuna pulizia automatica o cancellazione di dati preesistenti.
