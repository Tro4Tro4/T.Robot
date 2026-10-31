# M34 - Preferenze pasti

Implementazione locale autorizzata dopo il brainstorming del 31 ottobre 2026. Specifica: `docs/superpowers/specs/2026-10-31-meal-preferences-design.md`.

## Piano di implementazione

1. Domain e servizi: ciclo raccolta/preparazione/voto/chiusura, elenco curato con fonti conservate, schede personali, eleggibilita, storico e snapshot. Mapping EF e test sintetici.
2. Web: preparazione, anteprima, apertura e chiusura dal modulo Proposte pasti; risultati adulti/bambini e conteggi congelati/correnti distinti. Autorizzazione per evento, revisioni e antiforgery.
3. WhatsApp: `!preferenze` dal gruppo e dialogo privato persistente; scelta persona/giorno/pasto, selezione multipla paginata, conferma/annullamento e cinque minuti assoluti. Isolamento dal dialogo M33.
4. Integrazione: migrazione additiva generata una sola volta dal coordinatore; suite .NET e Node, test upgrade conservativo, controlli modello e revisione indipendente. Nessun accesso al DB operativo o invio reale.

Stato: implementazione e rilascio Azure completati su richiesta esplicita dell'utente, con revisioni indipendenti APPROVED. Web e gateway sono attivi in `m34-20261031-arm64`; raccolta aperta e nessuna votazione avviata automaticamente. Le modifiche preesistenti ad AGENTS e ai README/handoff sono conservate separatamente.

## Funzioni implementate

- Regia `/MealVoting?eventId=...`, collegata dall'archivio Proposte pasti: ciclo di raccolta, preparazione con correzione/esclusione/accorpamento, anteprima e apertura, chiusura manuale. Il modulo Menu sospeso rimane indipendente.
- Le fonti originali M33 e le disponibilita a cucinare restano conservate. Un accorpamento sposta i collegamenti alla voce scelta, senza cancellare proposte; le voci senza fonti non vengono pubblicate. La raccolta vuota non puo essere chiusa.
- `!preferenze` dal gruppo autorizzato avvia il dialogo privato. Persona, giorno, pasto, selezioni multiple e riepilogo con conferma. Il genitore sceglie separatamente per se e per i figli collegati. Nessun avvio da un DM spontaneo.
- Pagine da cinque voci; numeri per aggiungere/togliere una scelta, tutti/nessuna, `!pagina N`, `!riepilogo`, `!modifica`, `!cambia` e `!annulla`. Prima del salvataggio vanno consultate tutte le pagine del riepilogo. Termine fisso di cinque minuti, senza estensioni; nessuna sovrapposizione col dialogo M33.
- Conferma vincolata a persona gestibile, soggiorno confermato inclusivo, collegamento, identita, tornata e revisioni correnti. Messaggio privato esplicativo se manca la classificazione adulta. Preferenze distinte per bambino, con adulto operatore nello storico.
- Conteggi provvisori adulti/bambini/totale visibili nel dialogo; risultato congelato alla chiusura e conteggio operativo aggiornato separato nel pannello. Scelte non eleggibili escluse senza cancellazione e ricontate se l'eleggibilita viene ripristinata.
- Storico privato consultabile, pagine da 25 modifiche con scelte prima/dopo. Ricevute idempotenti e journal esistente senza reinvii automatici di esiti incerti. Limite tecnico del servizio: 1000 voci per scheda confermata.

## File principali

- `src/TRobot.Domain/MealVoting.cs`, `src/TRobot.Domain/MealPreferenceSession.cs`: ciclo, elenco/fonti, schede/storico e sessione.
- `src/TRobot.Application/Menu/MealVotingService.cs`: servizi autorizzati, preparazione, eleggibilita, conteggi, chiusura e storico.
- `src/TRobot.Application/WhatsApp/WhatsAppMealPreferencesService.cs` e integrazione nelle parti `WhatsAppService.cs`/`WhatsAppMealProposalService.cs`: dialogo privato e separazione dalla raccolta M33.
- `src/TRobot.Infrastructure/Persistence/MealVotingConfiguration.cs`, `MealPreferenceSessionConfiguration.cs`, migrazione `Migrations/20261031205555_AddMealPreferences.cs` e relativo designer/snapshot.
- `src/TRobot.Web/Controllers/MealVotingController.cs`, `Models/MealVotingModels.cs`, `Views/MealVoting/Index.cshtml`; collegamento dall'archivio, DI in `Program.cs` ed etichette dello storico.
- `gateway/TRobot.WhatsApp/src/gateway.js` e `test/gateway.test.js`: nuovo candidato ammesso, risposta privata solo al mittente verificato, regressioni del contratto.
- Test nuovi `MealVotingTests.cs`, `MealPreferenceWhatsAppTests.cs`, `MealPreferenceMigrationTests.cs`, `MealPreferenceWebTests.cs` sotto `tests/TRobot.Tests/`.

## Schema e conservazione dei dati

Una sola migrazione nuova, applicata su Azure dopo backup e rehearsal sulla copia corrente; non applicata al DB locale dell'utente. Sei tabelle nuove: cicli, voci, fonti, schede, cambiamenti e sessioni. La chiave alternativa `(EventId, Id)` sulle proposte M33 consente il vincolo composto delle fonti ed evita riferimenti fra eventi.

Il test di upgrade parte da M33 con dati sintetici, Identity, una proposta e una sessione privata aperta. Confronta tutte le righe di ogni tabella preesistente per nome colonna; SQLite ricostruisce la tabella proposte per il nuovo vincolo e puo cambiarne l'ordine fisico delle colonne. Verificati `integrity_check` e `foreign_key_check`. Il test storico M33 e stato fissato alla sua migrazione specifica, mantenendo distinto il nuovo upgrade.

## Verifiche effettive

- `dotnet build TRobot.sln --no-restore`: build finale con zero errori e zero avvisi.
- `dotnet test TRobot.sln --no-build --no-restore`: suite completa finale 651/651 positiva, dopo i correttivi della review.
- 25/25 test mirati positivi dopo le correzioni; successiva suite completa finale positiva. Per la navigazione Preferenze pasti e la guida contestuale aggiunte al rilascio, 18/18 test HTTP/portale positivi e publish Release ARM64 riuscito. I conteggi dei run si sovrappongono e non vanno sommati.
- Node 24.22.0, `node --test` dalla cartella gateway: 60/60 positivi, nessuna sessione o invio WhatsApp reale.
- `dotnet ef migrations has-pending-model-changes --project src/TRobot.Infrastructure --startup-project src/TRobot.Infrastructure --no-build`: nessuna differenza sulla build finale.
- `git diff --check` positivo; nessun pattern di token, chiavi private o identita WhatsApp reali rilevato nei nuovi file applicativi esaminati.
- Revisione indipendente GPT-6.1 Sol: APPROVED dopo correzione di raccolta vuota senza uscita e risposta silenziosa alla classificazione mancante. Modifiche rilette, test finali eseguiti dal coordinatore.

Il successivo rilascio autorizzato e il collaudo pubblico sono descritti sotto. Restano escluse prove della nuova funzione dal telefono, pairing, messaggi o voti dimostrativi e collegamenti del gateway locale. I test HTTP modificano soltanto dati sintetici.

## Prossimo passo

Dalla pagina Proposte pasti aprire Preferenze pasti, avviare il ciclo, chiudere la raccolta e preparare l'elenco. Dopo anteprima e apertura manuale, collaudare `!preferenze` dal gruppo con adulto e figlio, modifica/azzeramento, scadenza e conteggi. Nessun voto o messaggio dimostrativo automatico.


## Rilascio Azure completato il 31 ottobre 2026

Su richiesta esplicita rilasciati Web e gateway in `m34-20261031-arm64`, partendo dalle rispettive release M33 correnti. Dipendenze Node conservate dopo confronto esatto dei manifest, proprieta/gruppo verificati e import eseguiti con l'utente reale del servizio senza aprire connessioni WhatsApp aggiuntive.

Backup coerente privato sulla VM con Web/gateway fermi, comprensivo di DB, chiavi, configurazione e sessione. Rehearsal su copia del DB remoto corrente e migrazione operativa in un'unica transazione immediata con FK sempre ON: rimosse soltanto le istruzioni di gestione transazione/PRAGMA esterne dello SQL EF, preservate tutte le operazioni di schema. Confrontate tutte le righe e i vincoli precedenti, con la sola aggiunta attesa della chiave alternativa delle proposte e della riga nella cronologia EF. Sei nuove tabelle inizialmente vuote, due proposte confermate preesistenti conservate, integrita/FK OK e scenario eliminato ancora assente. Nessun vecchio DB ripristinato.

Recovery della stessa identita e del gruppo originale riuscito. Configurazione e chiavi conservate byte per byte; Web, gateway, Caddy e timer backup attivi. NRestarts=0 per Web/gateway, recovery Result=success ed ExecMainStatus=0 al controllo finale. Scheduler Production e copie Oracle/PC invariati e disabilitati.

Collaudo pubblico con TLS verificato e sessione originale conservata: 12 pagine riservate 200/no-store, otto GET/POST ai moduli sospesi 404, dieci POST privi di antiforgery rifiutati 400, CSS/JavaScript corrispondenti ai sorgenti. 48 controlli Chromium desktop/mobile e quattro percorsi senza JavaScript positivi, nessun overflow, errore JavaScript o HTTP; logout verificato. Nessun ciclo, voto, proposta o invio dimostrativo creati. La prova effettiva del voto dal telefono resta all'utente dopo l'apertura della votazione.

Materiale privato: `.local/azure/m34-release.py`, `run-m34-remote.py`, `test-m34-release.py`, `m34-release-material.json`, `smoke-m34-release.py`, `m34-release-smoke-report.json`; contesto remoto `/root/trobot-provisioning/m34-20261031-context.json`. Quattro test Python offline positivi su SQL reale, dati sintetici, conservazione/rollback, hash e traversal. Script, wrapper e correttivi UI revisionati indipendentemente APPROVED. Backup e dati operativi restano sulla VM, materiale privato escluso dal commit.

Pacchetto Web: 224 file, 9.120.209 byte, SHA-256 `eb4ffdf74855b3cae790f74beb67ee47c8d505511ba098b23a23407d42eb82f9`. Gateway: otto file, 26.204 byte, SHA-256 `3da531dc946b8820f1ceaf28ab0cbf8fba9c790f7554a6e0ab23e160ca91bb1b`. SQL: SHA-256 `194f088d3da8d73f07395b6403af550073e02888bc20c94aa6a41cb452696c4a`. Questi sono hash di artefatti, non segreti.
