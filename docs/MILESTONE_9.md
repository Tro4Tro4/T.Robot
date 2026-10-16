# Milestone 9 — Presenze registrate dall'organizzatore

Estensione circoscritta dei servizi e della tabella delle presenze. Obiettivo: gestire una persona senza mittente simulato dalla scheda evento, conservando gli stessi controlli di audit, autorizzazione e deduplica.

Ogni riga partecipante offre **Registra presenza**. Il modulo mostra nome e stato corrente e consente Confermato, Indeciso o Assente; richiede conferma esplicita. Application verifica account abilitato, assegnazione attiva e appartenenza della persona all'evento. Lo stato di altri eventi non viene modificato.

Una versione basata su stato e timestamp protegge dai moduli obsoleti. La transazione riconvalida i permessi, aggiorna lo stato e conserva una ricevuta con attore e provenienza Organizer. La provenienza Simulation resta il default per le ricevute precedenti. Un vecchio reinvio non ripristina uno stato superato; dati o provenienza diversi con lo stesso identificativo generano conflitto.

Le registrazioni invariate con nuovo ID hanno una ricevuta ma non aggiornano timestamp e conteggi. Lo storico distingue registrazioni dell'organizzatore e simulazioni. Non si richiedono mittenti, numeri telefonici, account partecipante o messaggi WhatsApp.

Verifiche previste: persona senza mittente, flusso HTTP/CSRF/conferma, tutte le transizioni consentite, autorizzazione per evento, pagine obsolete e ritorno allo stato iniziale, reinvii superati, collisioni fra sorgenti, rollback dopo SQL e upgrade conservativo.

## Esiti del 16 ottobre 2026

- `dotnet build TRobot.sln --no-restore`: 0 errori e 0 avvisi.
- `dotnet test TRobot.sln --no-build --no-restore`: **173 test superati**, 0 falliti e 0 ignorati. Compresi i test della Milestone 8 e 27 nuovi casi per registrazioni senza mittente, transizioni, conferme, anti-CSRF, campi contraffatti, permessi e revoche, reinvii, collisioni, pagine obsolete e rollback dopo SQL.
- Upgrade ripetibile dal precedente schema con ricevute conservate come Simulation e rifiuto di origini non valide; restano verdi gli upgrade delle milestone precedenti.
- Generata e applicata `20261016135707_OrganizerAttendance`; modello EF allineato. Copia preventiva `.local/trobot-before-organizer-attendance.db`.
- Confronto di tutte le colonne precedenti delle tabelle applicative senza differenze. `integrity_check` OK, `foreign_key_check` vuoto, ricevute precedenti tutte Simulation. Verifiche eseguite dopo il completamento della ricostruzione SQLite dei vincoli; il tool ha segnalato il PRAGMA fuori transazione già osservato nelle migrazioni precedenti.
- Smoke test reale: login, modulo presenza, invio senza conferma rifiutato, storico della presenza precedente e conteggi conservati, logout. Nuovo confronto di persone, account, presenze, ricevute e audit senza differenze. Le registrazioni effettive sono state provate nei database isolati dei test.
- Listener solo `127.0.0.1` e `::1`, porta 5238. Processo poi arrestato; credenziali invariate, nessun deploy o trasporto esterno.

Prossimo incremento proposto: luogo e programma dell'evento, mantenendo autorizzazioni, revisione e audit.
