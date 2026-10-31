# M33 - Proposte pasti dal gruppo al dialogo privato

## Obiettivo e disegno approvato

Il 31 ottobre 2026 l'utente approva la raccolta di suggerimenti per giorno e pasto. Ogni persona puo suggerire un piatto oppure offrirsi di prepararlo. L'offerta resta provvisoria: menu definitivo, incarichi e lista della spesa saranno decisi successivamente.

Eccezione esplicita alla precedente regola senza DM: `!proponi` nel gruppo autorizzato avvia automaticamente il dialogo privato con il solo mittente associato. Nel gruppo rimane una reazione di conferma; domande, risposte e conferma finale avvengono in privato. Il sito resta riservato agli organizzatori.

## Flusso

1. `!proponi` nel gruppo avvia la sessione personale o riprende quella aperta.
2. Il bot chiede giorno, pasto (colazione, pranzo, cena), piatto e disponibilita a cucinare.
3. Il riepilogo richiede `!conferma` prima di creare la proposta.
4. `!annulla` interrompe la sessione privata e scarta i dati incompleti.
5. La sessione scade esattamente dopo cinque minuti dall'avvio. Le risposte e la ripresa non prolungano il termine. Nessun messaggio automatico alla scadenza; risposte tardive non confermano la proposta.
6. Le proposte confermate restano nel portale, raggruppate per giorno e pasto, con autore e disponibilita. Proposte simili di autori diversi rimangono distinte.

## Confini della milestone

- Archivio separato dal Menu preesistente, che resta sospeso.
- Prima consegna: dialogo e archivio in consultazione. Modifica/ritiro di proposte confermate e apertura/chiusura della raccolta restano incrementi successivi.
- Il modello evento non ha ancora una data finale. Si riusa il calendario dei soggiorni: date entro 31 giorni dalla data locale dell'evento, con anno inferito in prossimita della data evento quando omesso.
- Nessuna creazione automatica di menu definitivo, votazione, acquisto o incarico.
- Autorizzazioni del gruppo, partecipante, alias e attivatore ricontrollate a ogni risposta.
- Sessioni legate al collegamento e alle identita verificate; cambi invalidano la conversazione.
- Ricevute persistenti e journal senza reinvii automatici; destinatario DM derivato dal messaggio reale.
- Nessuna attivazione del gateway locale e nessun messaggio di prova reale.
- Migrazione additiva da verificare su database sintetico; database operativo Azure invariato durante lo sviluppo.

## Validazione

Sono richiesti test del dialogo completo, conferma, isolamento tra persone e canali, annullamento, scadenza esatta, duplicati, revoche, ripartenza, archivio riservato e conservazione delle righe precedenti durante la migrazione. Build e test integrati precedono la review indipendente.

## Implementazione locale

- `MealProposal` conserva soltanto la proposta confermata. `MealProposalSession` contiene la raccolta provvisoria, con chiave composta evento/persona e scadenza persistita.
- La scadenza rende immediatamente inutilizzabile il dialogo; alla successiva interazione la sessione scaduta viene chiusa e i campi provvisori ripuliti. Non esiste un timer di messaggi privati alla scadenza.
- Pagina `/MealProposals?eventId=...`, raggiungibile dalla navigazione evento, con schede per giorno e pasto, autore e disponibilita. Accesso solo agli organizzatori assegnati, contenuti codificati da Razor.
- Nuova migrazione `20261031133619_AddMealProposals`: due sole tabelle aggiuntive, nessuna modifica alle tabelle precedenti. Verificati confronto di tutte le righe, integrita SQLite e foreign key su database sintetico.
- Gateway: destinatario privato derivato esclusivamente dal messaggio originario, invio DM disponibile solo nel percorso di risposta al callback. Endpoint degli invii manuali limitato ai gruppi come prima. Quote separate: 6 messaggi/minuto nel gruppo, 15 in privato; coda globale invariata.
- Build senza errori o avvisi; 12 test mirati e 56 test Node positivi. Modello EF allineato. Review indipendente APPROVED dopo correzione e regressione della sessione della stessa persona in eventi diversi.
- Suite integrata finale: 634/634 test .NET positivi. Comandi: `dotnet build TRobot.sln --no-restore`, `dotnet test TRobot.sln --no-build --no-restore`, `node --test` dalla cartella gateway e `dotnet ef migrations has-pending-model-changes --project src/TRobot.Infrastructure --startup-project src/TRobot.Infrastructure --no-build`.
- Sviluppo locale concluso senza invii reali. Il successivo rilascio autorizzato e descritto sotto; la prova del dialogo sul telefono resta da eseguire dall'utente.

## Passo operativo successivo

Rilascio coordinato Web e gateway su Azure, con backup e rehearsal della migrazione su copia del database corrente. Poi prova manuale con telefono: `!proponi` nel gruppo, primo messaggio privato, annullamento, conferma e scadenza. Non avviare una seconda copia del gateway sul PC.

## Rilascio Azure completato il 31 ottobre 2026

Su richiesta esplicita dell'utente, Web e gateway sono attivi entrambi in `releases/m33-20261031-arm64`. Risorse Azure, configurazione dei servizi, scheduler escluso e limite di spesa invariati. Vecchie release conservate per il rollback dei soli binari; nessuna copia operativa Oracle o PC riattivata.

Pacchetto Web ARM64 framework-dependent: 223 file, 9.000.456 byte, SHA-256 `d9667614e6f9fd5cbc5ffd8519aa2578cbf9017e67b633a0fba6d16a0e14124a`. Gateway: otto file sorgente/package, 26.197 byte, SHA-256 `ee149cf0a588e76eb368cd94f064ab93c4e729100b13c738a48197a45f51ee28`. Dipendenze originali conservate dopo confronto esatto dei due manifest. SQL EF SHA-256 `bd505410d0bc2cf0fc5972b9acfdb4094f964b3e583660c8273a70da87f83c88`. Nessun database, chiave, sessione o secret nei pacchetti.

Procedura e correzioni sottoposte a review indipendente GPT-6.1 Sol, APPROVED; tre test Python offline positivi su migrazione, rollback SQL e rifiuto traversal archivio. Backup consistenti privati sulla VM con Web/gateway fermi: DB, chiavi, configurazione e stato WhatsApp. Rehearsal su copia del DB remoto corrente e successiva migrazione operativa con foreign key attive, transazione immediata e confronto prima del commit. Tutte le righe e gli schemi precedenti conservati, eccetto l'aggiunta attesa alla cronologia EF; due nuove tabelle inizialmente vuote, integrity/FK OK e scenario eliminato ancora assente.

Il primo tentativo ha applicato correttamente la migrazione ma non superato il recovery del nuovo gateway. Rollback automatico dei binari riuscito, vecchio gateway riconnesso e schema additivo conservato. Diagnosi: `copytree` conservava i modi ma non il gruppo delle dipendenze, rendendo `node_modules` root:root anziche root:trobot-whatsapp. Corretto il gruppo esclusivamente nella nuova release, verificati gli import Baileys/qrcode/pino con l'utente reale del servizio senza aprire socket. Secondo tentativo con nuovo backup corrente, rehearsal distinto e schema gia applicato, senza ripetere SQL o ripristinare DB storici. Attivazione e recovery finali riusciti sul gruppo originale, identita, configurazione e chiavi conservate.

Collaudo pubblico HTTPS con TLS verificato, nessun override: sessione originale conservata dopo il rilascio, undici pagine riservate HTTP 200/no-store compreso l'archivio Proposte pasti, otto GET/POST ai quattro moduli sospesi 404 e cinque POST Alloggi senza antiforgery rifiutati 400. CSS/JavaScript identici ai sorgenti. **44 controlli Chromium desktop/mobile e tre percorsi senza JavaScript positivi**, nessun overflow, errore JavaScript o errore HTTP. Logout verificato. Web/gateway/Caddy/timer attivi, recovery Result=success e ExecMainStatus=0, NRestarts=0 per Web e gateway. Circa 510 MiB disponibili al controllo finale.

Materiale privato locale: `.local/azure/m33-release.py`, `m33-release-material.json`, `test-m33-release.py`, `smoke-m33-release.py`, `m33-release-smoke-report.json`. Contesti remoti e riferimenti ai backup in `/root/trobot-provisioning/m33-20261031-context.json` e `m33-20261031-attempt2-context.json`. Backup e dati operativi rimasti esclusivamente sulla VM.

Nessun pairing, messaggio di prova automatico o proposta dimostrativa creati. Ora l'utente puo provare `!proponi` dal gruppo e completare il dialogo privato; la ricezione e il completamento effettivi di questo nuovo flusso non sono ancora verificati sul telefono.
