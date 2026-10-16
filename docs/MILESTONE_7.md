# Milestone 7 — Correzione dei nomi dei partecipanti

Estensione circoscritta della gestione eventi esistente, definita con brainstorming e realizzata secondo la modalità autonoma richiesta dall'utente. Obiettivo: correggere un nome senza ricreare la persona o perdere presenze, ricevute e identità del simulatore.

La scheda evento offre **Correggi nome** quando l'organizzatore è autorizzato. Il modulo accetta nome e revisione corrente. Una pagina obsoleta non può sovrascrivere una correzione successiva; un salvataggio invariato non genera audit o revisioni.

Il nome appartiene alla persona ed è condiviso dai suoi eventi. La modifica richiede accesso attivo a tutti gli eventi interessati e conferma esplicita se sono più di uno. La UI non espone eventi altrui. Le persone collegate ad account o ruoli organizzativi sono escluse, compresi i ruoli revocati. Non viene creato un elenco globale delle persone.

Application applica queste regole e costruisce un audit per ciascun evento coinvolto. Infrastructure esegue controlli e scritture nella stessa transazione SQLite; Domain valida il nome e incrementa la revisione. Web usa cookie esistenti, anti-CSRF e input specifici senza campi per privilegi, presenze o mittenti.

La deduplica delle registrazioni confronta un'impronta del payload originale, anziché il nome attuale che può essere corretto. Per le registrazioni precedenti, l'impronta viene acquisita prima della prima rinomina, nella stessa transazione. Gli audit conservano attore, ID, azione e istante; nessuna copia dei nomi precedenti.

Sono esclusi: modifica dei nomi degli account, fusione/collegamento delle persone, cancellazioni, modifica dello stato presenza, famiglie, accompagnatori e trasporti WhatsApp.

Verifiche: HTTP con cookie/anti-CSRF, validazione, revisione obsoleta, concorrenza fra DbContext, account disabilitati e accessi revocati, persone condivise e conferme, protezione degli organizzatori, rollback dopo SQL, reinvii prima/dopo rinomina e upgrade da tutte le milestone precedenti.

## Esiti del 16 ottobre 2026

- `dotnet build TRobot.sln --no-restore`: 0 errori e 0 avvisi.
- `dotnet test TRobot.sln --no-build --no-restore`: **137 test superati**, 0 falliti e 0 ignorati. Compresi rinomina HTTP, ricevute e stato conservati, campi contraffatti ignorati, revisione mancante/obsoleta, permessi su tutti gli eventi, conferma condivisa e audit multiplo, blocco degli account/ruoli organizzativi, concorrenza EF e rollback dopo SQL.
- Verificati reinvii con payload originale dopo più rinomine, anche per registrazioni precedenti senza impronta, e conflitti per payload diverso. Upgrade dagli schemi precedenti ripetibili; dati e ricevute conservati.
- Generata e applicata `20261016131112_ParticipantNames`; controllo modello EF senza differenze. Copia preventiva in `.local/trobot-before-participant-names.db`.
- Confrontate tutte le colonne precedenti delle tabelle applicative: nessuna differenza. Revisione delle persone zero e nuove impronte assenti per le righe precedenti, `integrity_check` OK e `foreign_key_check` vuoto. SQLite ha segnalato il PRAGMA fuori transazione per la ricostruzione dei vincoli di EventAudits: la migrazione è terminata e i controlli sono stati eseguiti dopo il completamento.
- Smoke test sull'app reale: login, collegamento Correggi nome, modulo con revisione, POST con nome invariato senza audit, conteggi precedenti e logout. Nuovo confronto di persone, account, presenze, ricevute e audit senza differenze. Le rinomine effettive sono state provate nei database isolati dei test.
- Listener solo `127.0.0.1` e `::1`, porta 5238. Processo di prova arrestato; credenziali invariate. Nessuna pubblicazione o integrazione esterna.

Prossimo incremento proposto: consultazione dello storico delle operazioni e delle presenze per evento.
