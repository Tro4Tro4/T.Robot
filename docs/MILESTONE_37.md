# M37 - Date della presenza e domande aggiuntive

Rilasciata su Google il 27 novembre 2026 (Web `domande-presenza-20261127-x64`, commit `8fea061`), su richiesta dell'utente. Gateway invariato (`multi-evento-20261127d-x64`).

## Obiettivo

1. Per ogni evento l'organizzatore decide se il bot deve chiedere ai partecipanti data di arrivo e numero di giorni. Se non servono (per esempio una festa in un giorno) la presenza vale per la data dell'evento e `!presenza si` non chiede né date né notti.
2. L'organizzatore può aggiungere dal portale domande a risposta chiusa, per esempio "come sali alla baita?" con "A piedi" e "Con il gatto delle nevi".

L'utente ha chiesto prima una regola per gli eventi di un giorno, poi l'ha generalizzata in un'opzione per evento. Le scelte di disegno sono in `docs/DECISIONS.md`, sezione "Date della presenza e domande aggiuntive (M37)".

## Comportamento

- **Impostazione:** pagina Soggiorni del portale, sezione "Date della presenza": casella "Chiedi a ogni partecipante data di arrivo e numero di giorni" e campo "Giorni dell'evento" (1-31, usato solo con la casella spenta). Valori in `AttendanceCollectionSettings` (`AskStayDates`, default vero; `EventDays`, default 1). Modifica con conferma esplicita, controllo di revisione e audit `StayModeChanged`.
- **Date non richieste:** `!presenza si` salva subito un `AttendanceStay` con la data dell'evento nel fuso configurato e `EventDays` giorni, in modo che menu, alloggi e `!proponi` continuino a funzionare. Chi ha un nucleo riceve solo la conferma per tutta la famiglia, con testo "presenza". Un adulto senza nucleo non riceve conferme aggiuntive.
- **Domande aggiuntive:** testo fino a 200 caratteri, da 2 a 8 risposte (massimo 60 caratteri, diverse tra loro), al massimo 10 domande per evento. Si aggiungono e si disattivano (o riattivano) dalla pagina Soggiorni; non si modificano né si cancellano, le disattivate conservano le risposte. Dal testo sono rimossi i caratteri di controllo e di formato e i segni di formattazione WhatsApp (asterisco, trattino basso, tilde, apice inverso) e la chiocciola.
- **Dialogo:** dopo il salvataggio del soggiorno, nello stesso dialogo di `!presenza si` (stessi 15 minuti, `!annulla` salta le domande restanti), il bot pone le domande attive in ordine, con le risposte numerate; si risponde con il numero. Nessun nuovo DM.
- **Modifica:** `!presenza modifica` con date fisse ripropone solo le domande (senza domande risponde che non ci sono altri dettagli). Con date libere il flusso di modifica è invariato.
- **Destinatari delle risposte:** chi scrive e i bambini collegati (stesso criterio dei soggiorni individuali). Una riga per persona e domanda, riscritta alla modifica. Gli altri adulti di un nucleo rispondono con il proprio `!presenza`.
- **Portale:** per ogni opzione l'elenco dei soli partecipanti confermati con il conteggio. Nessuna risposta individuale viene pubblicata nel gruppo.
- **Promemoria:** con date fisse il testo del promemoria non parla più di arrivo e giorni.
- **Scadenza:** se il dialogo scade durante le domande il bot ricorda che la presenza resta salvata e indica `!presenza modifica`.

## Schema

Migrazione `20261127142715_AttendanceStayModeAndQuestions`, solo additiva e senza ricostruzione di tabelle:

- colonne `AskStayDates` (default 1) e `EventDays` (default 1) in `AttendanceCollectionSettings`;
- colonna `QuestionId` nullable in `AttendanceDialogues` e nuovo step `Question` (valore 6 dell'enum);
- tabelle `AttendanceQuestions`, `AttendanceQuestionOptions`, `AttendanceAnswers` (indice univoco su evento, persona e domanda; foreign key `Restrict`).

Non c'è un CHECK sui giorni perché aggiungerlo avrebbe ricostruito `AttendanceCollectionSettings`: la validazione è nel dominio e in `SetAttendanceStayModeAsync`.

## Verifiche

- Build senza errori o avvisi e 737 test .NET superati (8 nuovi: flusso a date fisse, domande, modifica, disattivazione, validazione, autorizzazione, scadenza, panoramica, upgrade di migrazione).
- Due test di migrazione già esistenti (`MealPreferenceMigrationTests`, `AttendanceFamilyStatusMigrationTests`) leggevano lo schema più recente: ora si fermano alla loro migrazione o completano la migrazione prima di leggere le entità.
- Revisione indipendente in sola lettura: APPROVED, con due correzioni minori applicate (messaggio di scadenza, promemoria).
- Produzione: prova della migrazione su una copia del DB, poi `activate` con `--allow-changes AttendanceCollectionSettings`; righe esistenti conservate, identità e gruppo WhatsApp invariati, `verify` e smoke positivi, 59 tabelle.

## Limiti e da fare

- Non provato dal telefono né dal portale in un browser.
- Cambiando modalità con un dialogo già in conferma, il dialogo prosegue con le date calcolate prima del cambio.
- Se l'organizzatore disattiva la domanda corrente a metà dialogo, il bot riparte dalla prima domanda attiva.
- Gli strumenti monouso in `tools/` che eliminano un evento non conoscono ancora le tre nuove tabelle.
