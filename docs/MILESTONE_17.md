# Milestone 17 — Giochi volontari e quiz locali

Verifica coordinata: review indipendente positiva, build con zero errori e avvisi, 13 test mirati e 292 test superati nella suite completa. Migrazione `LocalGames` applicata al database locale; dati precedenti invariati, integrità e chiavi esterne OK. Nessuna modifica a credenziali o dati demo.

Obiettivo: simulare l'intrattenimento del gruppo mantenendo scelta volontaria, storico e classifica nel sito riservato. Nessuna AI, presa di mira, penalità, invio reale o integrazione gateway.

## Flusso verificabile

1. Dalla pagina evento aprire **Giochi volontari e quiz locali**. Nessuno aderisce automaticamente, nemmeno gli organizzatori. Registrare con conferma la scelta comunicata da un partecipante già appartenente all'evento; non viene imposto uno stato presenza o adulto/bambino.
2. Creare un quiz in bozza con domanda, 2–10 opzioni distinte, posizione corretta e scadenza nel fuso evento. Definizione immutabile dalla creazione. Per correggerla annullare prima della pubblicazione e creare un'altra bozza.
3. Esaminare l'anteprima e confermare: un messaggio `Entertainment` viene accodato atomicamente. La soluzione rimane nel sito. Usare **Coda e invio simulato** per il tick manuale: silenzio, pausa e limite giornaliero si applicano come per l'altro intrattenimento.
4. Solo dopo `Sent` e prima della scadenza l'organizzatore può registrare, con conferma, la risposta simulata di una persona aderente. Una sola risposta definitiva per persona/quiz. Il sito dei partecipanti e i DM non esistono.
5. Esaminare conteggio e soluzione nell'anteprima chiusura e confermare. La chiusura può essere anticipata; a scadenza le risposte sono già bloccate, ma la chiusura resta esplicita.
6. La classifica privata assegna un punto per risposta corretta solo nei quiz chiusi. Nessun punteggio negativo; pari punti significa stessa posizione. Nessun messaggio di risultato/classifica viene generato.
7. Ritirare un'adesione con conferma: la persona scompare dalla classifica, risposte e adesione rimangono. La riadesione ripristina i punti precedenti senza permettere una seconda risposta allo stesso quiz.

## Sicurezza e persistenza

Servizi Application autorizzano ogni lettura/scrittura con account abilitato e assegnazione attiva all'evento; i POST MVC richiedono anti-CSRF. Revisioni proteggono quiz e adesioni, risposta e chiusura aggiornano la stessa revisione del quiz. Unicità database su evento/quiz/persona impedisce doppie risposte; riferimenti composti impediscono opzioni e pubblicazioni di altri eventi. Tutte le foreign key preservano lo storico.

`QuizAnswer` è immutabile e usa l'operationId come identificativo, conservando attore, persona, opzione e timestamp UTC: funge anche da ricevuta storica. Un reinvio identico rimane innocuo dopo ritiro/scadenza/chiusura e non ricalcola o ripristina dati; payload diverso genera conflitto. L'autorizzazione attuale precede anche il controllo della ricevuta. Scrittura, revisione e audit sono nella stessa transazione.

Annullare una bozza o una pubblicazione non ancora `Sent` annulla anche l'outbox conservando tutte le righe. Dopo `Sent` si usa la chiusura. Una pubblicazione bloccata da silenzio/cap può arrivare a scadenza senza aprire risposte: l'organizzatore può annullarla se ancora non inviata; nessun rinnovo automatico della scadenza.

## Verifiche

`GameTests` copre invarianti, opt-in/out e riadesione, punti solo dopo chiusura, parità, pubblicazione simulata e quiet hours/cap, scadenza esatta, unicità risposta e collisione ricevuta, conflitti di revisione, riferimenti tra eventi, account disabilitato anche sui reinvii, rollback dopo salvataggio e HTTP autenticato/anti-CSRF/encoding.

Comandi di integrazione eseguiti sequenzialmente dal coordinatore:

```powershell
dotnet build TRobot.sln --no-restore
dotnet test TRobot.sln --no-build --filter FullyQualifiedName~GameTests
dotnet test TRobot.sln --no-build
```

Migrazione `LocalGames` e snapshot gestiti dal coordinatore; esiti finali riportati nella roadmap. Modulo esclusivamente locale Development/loopback.
