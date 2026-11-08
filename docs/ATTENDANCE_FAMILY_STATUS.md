# Presenze per famiglia e stato dal sito - 8 novembre 2026

Richiesta dell'utente: anche con `!presenza no` e `!presenza forse` chiedere se la risposta vale per tutta la famiglia o per il singolo, come gia avviene con `!presenza si`; dal sito riservato poter impostare Confermato, Indeciso, Assente o Senza risposta per il singolo partecipante.

Disegno breve (sostituisce la skill `brainstorming` non disponibile in Claude Code), modalita autonoma senza approvazioni intermedie.

## Obiettivo e vincoli

- Stesso modello di domanda gia usato per `!presenza si`: nucleo con altri partecipanti attivi, risposta `si`/`no`, `!annulla`, conferma prima di modificare altre persone.
- Nessuna mutazione di altri membri prima della risposta esplicita; controlli ripetuti nella transazione; snapshot del nucleo per rifiutare conferme obsolete.
- Soggiorni, storico e ricevute conservati: un'assenza non cancella date registrate (i conteggi correnti escludono gia gli assenti).
- Nessun nuovo DM, nessun messaggio automatico alla scadenza, nessun invio di prova.

## WhatsApp: `!presenza no` / `!presenza forse`

1. Lo stato del mittente cambia subito, come oggi.
2. Se il mittente e un adulto in un nucleo con altri partecipanti attivi (stessa regola di `AttendanceHouseholdAsync`), il bot risponde con una domanda invece della sola reazione:
   - `si` = stesso stato per tutti i partecipanti attivi del nucleo;
   - `no` = solo il mittente e i bambini collegati a lui come accompagnatore (stessa regola di `AttendanceIndividualTargetsAsync`, come per `!presenza si`);
   - `!annulla` = resta modificato soltanto il mittente.
3. Il dialogo usa l'`AttendanceDialogue` esistente con un nuovo passo e lo stato richiesto; scadenza di 15 minuti come il dialogo presenze. Se nucleo, stati o soggiorni cambiano prima della risposta, la conferma viene rifiutata e si chiede di ripetere il comando.
4. Mittente senza nucleo con altri partecipanti, oppure bambino: comportamento invariato (reazione).
5. Ogni cambio di stato di un altro membro e registrato nello storico e nell'audit con il mittente come autore.

## Sito: stato del singolo partecipante

- Pagina esistente "Registra presenza" dal dettaglio evento: aggiunto "Senza risposta" (`NoResponse`) alle opzioni gia presenti.
- Stessi controlli: organizzatore abilitato, conferma esplicita, versione attesa, operazione idempotente, ricevuta `Organizer`. Il reset non cancella soggiorni o storico.
- Su WhatsApp resta escluso un comando di reset a `NoResponse`.

## Stato

Implementato nel branch di sessione l'8 novembre 2026. Non rilasciato su Azure: il rilascio richiede richiesta esplicita, backup coerente e rehearsal della migrazione come nei rilasci precedenti.

- Migrazione `20261108152657_AddAttendanceFamilyStatus`: sola colonna nullable `FamilyStatus` su `AttendanceDialogues`, test di upgrade da `AddMealPreferences` con righe conservate e controlli integrity/FK.
- Storico: per ogni altro membro effettivamente cambiato una `WhatsAppIncomingReceipt` con hash sintetico dedicato (fingerprint `attendance-family-status`, senza collisioni con i messaggi reali) e audit `FamilyPresenceApplied` con il mittente come autore. Scartate `ParticipationReceipt` (vincolo CHECK sull'origine, ricostruzione tabella in SQLite) e `AttendanceStayChange` (richiede date).
- Verifiche nel container cloud con SDK 10.0.114 su copia senza `global.json` (10.0.401 non scaricabile dalla rete dell'ambiente): build 0 avvisi/errori, 679/679 test (baseline 663), nessuna modifica di modello pendente. Revisione indipendente in sola lettura APPROVED.
- Suggerimenti facoltativi non applicati: test dedicato al cambio di collegamento tra domanda e risposta; plurale "1 bambini" ereditato dal flusso `si`; un "si" generico entro 15 minuti estende lo stato alla famiglia, stesso rischio gia accettato per `!presenza si`.
- Collaudo dal telefono non eseguito.
