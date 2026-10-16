# Milestone 10 — Luogo e programma dell'evento

Estensione circoscritta dei moduli di creazione/modifica evento, definita con brainstorming e realizzata nella modalità autonoma richiesta dall'utente.

Ogni evento può indicare un luogo (300 caratteri) e un programma libero (4000 caratteri normalizzati), entrambi facoltativi. I campi compaiono nei moduli esistenti e nella dashboard riservata. Un evento senza questi dati mostra Da definire; la migrazione non inserisce contenuti negli eventi precedenti.

Il programma conserva le righe, uniformate a LF. Entrambi i campi rimuovono gli spazi esterni e convertono valori vuoti in null. Il contenuto è testo semplice codificato da Razor, senza esecuzione di HTML o script.

Domain valida prima di modificare qualsiasi campo. Application applica limiti e controlli di accesso nella transazione esistente; Infrastructure aggiunge colonne nullable e vincoli SQLite. Luogo, programma, data, nome e fuso condividono la revisione evento. L'audit EventUpdated rimane atomico e registra metadati senza copiare il contenuto.

La creazione resta riservata all'amministratore globale; la modifica e la lettura richiedono assegnazione attiva e account abilitato. Programma strutturato, attività, mappe e comunicazioni esterne sono fuori da questa milestone.

Verifiche previste: flusso HTTP completo, codifica del testo, normalizzazione e limiti compresi i CRLF al limite massimo, campi svuotati, salvataggi invariati, autorizzazione e revoca, pagine obsolete, concorrenza EF, rollback dopo SQL e upgrade conservativo da tutte le milestone precedenti.

## Esiti del 16 ottobre 2026

- `dotnet build TRobot.sln --no-restore`: 0 errori e 0 avvisi.
- `dotnet test TRobot.sln --no-build --no-restore`: **183 test superati**, 0 falliti e 0 ignorati. I 10 nuovi casi coprono il flusso completo, codifica del testo, limiti e righe CRLF, rimozione e salvataggi invariati, autorizzazione, rollback, concorrenza e upgrade.
- Generata e applicata `20261016143534_EventLogistics`; modello EF allineato. Copia preventiva in `.local/trobot-before-event-logistics.db`.
- Confrontate tutte le colonne precedenti delle tabelle applicative: nessuna differenza. Nuovi campi null per tutti gli eventi esistenti, `integrity_check` OK e `foreign_key_check` vuoto. SQLite ha segnalato il PRAGMA fuori transazione durante la ricostruzione dei vincoli; i controlli sono stati eseguiti dopo il completamento della migrazione.
- Smoke test sull'app reale: login, nuovi campi nei moduli, POST invariato senza audit, dashboard con valori Da definire e presenza precedente conservata, creazione e logout. Nuovo confronto di eventi, persone, account, partecipazioni, ricevute e audit senza differenze. Le modifiche effettive sono state provate nei database isolati dei test.
- Listener solo `127.0.0.1` e `::1`, porta 5238; processo poi arrestato. Nessun cambio delle credenziali, nessun deploy o invio esterno.

Prossimo incremento proposto: prime attività organizzative per evento con stato e responsabile.
