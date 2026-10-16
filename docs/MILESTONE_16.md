# Milestone 16 — Contabilità locale

Verifica coordinata: review indipendente positiva, build con zero errori e avvisi, 14 test contabili mirati e 279 test superati nella suite completa. Migrazione `LocalAccounting` applicata al database locale; dati precedenti invariati, integrità e chiavi esterne OK. Nessuna modifica alle credenziali o ai dati demo.

## Obiettivo e disegno

Contabilità riservata agli organizzatori assegnati all'evento, con account abilitato. Il modulo conserva anticipi di spesa, rimborsi manuali tra partecipanti, pesi e quote; tutte le scritture passano da AccountingService, con autorizzazione, validazione, revisione, transazione e audit atomici. Non produce messaggi outbox, trasferimenti di denaro o integrazioni esterne. Unica valuta EUR, senza conversioni.

Si riusano LocalOperations e LocalStore: Domain indipendente per invarianti e stati, Application per riparto e riconciliazione, mapping EF SQLite con FK composte evento/partecipante, controller e moduli MVC con anti-CSRF. Alternative come contabilità generale a partita doppia o integrazione bancaria introdurrebbero concetti e servizi fuori dal prototipo; si sceglie un registro locale tipizzato e verificabile.

## Regole dei movimenti

- Expense è un anticipo già sostenuto, dichiarato Pending; soltanto Approved contribuisce alle spese e al credito dell'anticipante. Pending può diventare Approved, Rejected o Cancelled; Approved può diventare Cancelled. Gli stati terminali non vengono riaperti.
- Payment è un rimborso dichiarato tra due partecipanti distinti. Soltanto Confirmed modifica i saldi; Declared può diventare Confirmed o Cancelled, e Confirmed può diventare Cancelled. La conferma è una verifica manuale dell'organizzatore, senza trasferimenti reali. Un pagamento non aumenta le spese.
- Importi decimal positivi fino a 1.000.000 EUR per movimento, con centesimi esatti. I moduli accettano punto o virgola, senza separatori delle migliaia e senza arrotondamenti silenziosi. Descrizione spesa fino a 150 caratteri, testo codificato in HTML.
- Importi, persone, descrizioni e identificativi dei movimenti restano immutabili. Per correggerli si annulla una registrazione e se ne dichiara una nuova. Le righe e i riferimenti storici non vengono cancellati; timestamp UTC di dichiarazione e ultimo cambio conservati.
- Le dichiarazioni e tutte le transizioni richiedono conferma esplicita. Gli ID operazione sono stabili: reinviare lo stesso contenuto e attore non duplica movimenti, anche dopo una transizione; contenuti diversi con lo stesso ID producono conflitto. Le transizioni richiedono revisione corrente: pagine obsolete non sovrascrivono stati successivi.
- Qualsiasi organizzatore abilitato e assegnato può dichiarare e approvare anche movimenti propri. Nel prototipo non esiste un ruolo finanziario separato o una regola dei due approvatori.

## Quote e riconciliazione

Le quote vengono ricalcolate dai dati correnti. Contribuiscono soltanto partecipanti Confirmed: adulto peso 1; bambino da membership attiva peso evento predefinito 0,5, configurabile 0–10 con tre decimali. Senza membership attiva il default è adulto. Il peso individuale facoltativo prevale, con zero per esenzione e campo vuoto per ripristinare il default. Anche configurazione e override richiedono conferma e revisione.

Il totale Approved viene convertito in centesimi interi, i pesi in millesimi interi. ShareCalculator usa BigInteger per quoziente e resto esatti: ogni quota prende prima i centesimi interi inferiori; quelli rimasti vanno ai resti maggiori, con parità risolta dall'ID persona in formato N, ordinamento ordinale. La somma delle quote coincide esattamente con il totale ripartibile, indipendentemente dall'ordine di lettura.

Ogni partecipante resta nella lista saldi anche se assente, indeciso o senza risposta. Anticipi e rimborsi confermati continuano a contare; queste persone hanno quota zero. La formula è:

`saldo = quota - anticipi Approved - rimborsi Confirmed inviati + rimborsi Confirmed ricevuti`

Saldo positivo significa da versare; negativo significa credito. Esempio: A anticipa 100 EUR e A/B hanno quota 50 EUR ciascuno; i saldi sono A −50 e B +50. Il rimborso B → A di 50 EUR confermato porta entrambi a zero, lasciando le spese a 100 EUR.

Con nessun confermato o con tutti i pesi confermati a zero, non si ripartisce arbitrariamente: quote zero, budget interamente NonRipartito e avviso esplicito. Vale sempre `somma(saldi) + NonRipartito = 0`. Il residuo di riconciliazione viene visualizzato; finché esiste NonRipartito i saldi non vanno usati per la chiusura dei conti. I saldi sono letti da un unico snapshot transazionale coerente.

## Perimetro delle persone e privacy

Anticipanti, mittenti e destinatari provengono esclusivamente dai partecipanti già registrati nell'evento. Un organizzatore con solo account/assegnazione non compare nell'elenco: questa milestone non iscrive automaticamente organizzatori e non aggiunge un flusso per collegare identità esistenti alle partecipazioni. Non creare persone duplicate per aggirare il limite. Le FK composte impediscono riferimenti a partecipanti di eventi diversi.

Saldi, nomi e dettagli finanziari sono visibili esclusivamente nel sito riservato, con risposte no-store. Non entrano in riepiloghi, sondaggi, outbox o messaggi al gruppo.

## Prova locale e verifiche

Dalla scheda evento aprire Contabilità locale. Dichiarare un anticipo, verificarlo nello storico Da approvare e confermarne l'approvazione. Osservare le quote, dichiarare il rimborso tra partecipanti e confermare manualmente la registrazione. Un movimento dichiarato non è ancora conteggiato. Pesi, presenze e annullamenti aggiornano il prospetto mantenendo lo storico.

AccountingTests copre confini importi e pesi, workflow e mancata mutazione su errore, residui dei centesimi e parità deterministiche, grandi importi senza overflow intermedi, bambini/override/membership inattive, assenti con anticipi, nessun confermato e pesi zero, esclusione Pending/Rejected/Declared/Cancelled, riconciliazione e assenza di doppio conteggio, reinvii e conflitti, autorizzazioni/account disabilitato/riferimenti estranei, conferme/revisioni e rollback audit. I test HTTP reali coprono login, anti-CSRF, ID dei moduli renderizzati, descrizioni codificate, ciclo dichiarazione → approvazione → rimborso confermato → saldi zero, parsing decimale e rigetto degli importi invalidi.

Migrazione LocalAccounting, build e test vengono integrati sequenzialmente dal coordinatore; risultati effettivi da riportare dopo l'esecuzione.
