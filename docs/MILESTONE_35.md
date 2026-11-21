# M35 - Invio reale dei messaggi del pannello

Incarico del 19 novembre 2026: completare il modulo Messaggi collegandolo al gateway WhatsApp esistente, con invio immediato e programmato, stati affidabili, diagnostica e protezione delle vecchie code simulate.

Stato: implementazione e test locali completati. **Nessuna modifica all'ambiente Azure, alla sessione WhatsApp o al database operativo e' stata eseguita da questo incarico.** Lo scheduler Production resta disabilitato finche' non viene attivato esplicitamente secondo la procedura sotto.

Aggiornamento del 21 novembre 2026: aggiunto al pannello lo stato live di gateway e connessione WhatsApp, separato dallo stato dello scheduler e dal singolo messaggio.

## Cosa cambia per l'organizzatore

1. Compone un messaggio e sceglie subito l'invio immediato oppure una data e ora.
2. La bozza non viene inviata: si apre l'anteprima con testo, fuso evento, data prevista e stato.
3. La conferma e' una vera autorizzazione all'invio WhatsApp: registra organizzatore, istante e gruppo. Se il collegamento non e' attivo la conferma viene rifiutata.
4. Dopo la conferma il messaggio e' `Confermato, in attesa`; il worker persistente lo trasmette quando e' dovuto, anche senza sessioni aperte.
5. Il pannello separa quattro livelli: stato del gateway, connessione WhatsApp con gruppo attivo e corrispondenza, elaborazione automatica e singolo messaggio (stato, creazione, invio previsto, invio effettivo, errore, tentativi, ultimo aggiornamento). Gateway e connessione sono un controllo live ogni 10 secondi; il pulsante `Elabora ora` permette l'elaborazione manuale dei messaggi gia' confermati.
6. Un esito incerto o fallito si risolve solo manualmente, con nota e audit, senza nuovi invii.

Riepiloghi automatici, sondaggi e quiz restano simulati e sono etichettati come tali: non vengono trasmessi a WhatsApp.

## Stati e canali

| Stato | Significato |
| --- | --- |
| `Draft` | Bozza non autorizzata |
| `Queued` | Confermato, in attesa di elaborazione |
| `Sending` | Claim persistito, invio in corso |
| `Sent` | Invio confermato dal gateway |
| `Failed` | Rifiuto noto prima della trasmissione |
| `Uncertain` | Esito non determinabile (timeout, rete, 5xx, processo interrotto) |
| `Cancelled` | Annullato prima dell'avvio |

`DeliveryMode` distingue `Simulated` (default storico) da `WhatsApp` (solo pannello). Una riga simulata non puo' diventare `Sent` tramite WhatsApp e una riga WhatsApp non puo' essere marcata inviata dalla simulazione.

## File principali

- `src/TRobot.Domain/OutboundMessage.cs`: modalita' di consegna, stati, campi di conferma/esito e transizioni validate.
- `src/TRobot.Application/Messaging/MessageDispatchService.cs`: claim, invio, registrazione esiti, recupero dei claim interrotti, limite per ciclo.
- `src/TRobot.Application/Messaging/MessageService.cs`: conferma reale, risoluzione manuale, elaborazione su richiesta; la simulazione ignora i messaggi WhatsApp.
- `src/TRobot.Application/Messaging/SchedulePolicy.cs`: pausa, silenzio e limite giornaliero per gli invii reali.
- `src/TRobot.Application/WhatsApp/Contracts.cs`, `src/TRobot.Infrastructure/WhatsApp/WhatsAppGatewayClient.cs`: rifiuto definitivo 4xx distinto dagli errori ambigui.
- `src/TRobot.Infrastructure/Persistence/OutboxDispatchStore.cs`: discovery dei messaggi dovuti e dei claim scaduti.
- `src/TRobot.Infrastructure/Persistence/MessageConfiguration.cs`: colonne, vincoli e indici.
- `src/TRobot.Web/Services/MessageDispatchWorker.cs`: worker persistente.
- `src/TRobot.Web/Controllers/MessagesController.cs`, `Models/MessageModels.cs`, `Views/Messages/*.cshtml`, `wwwroot/js/messages.js`: conferma, stato live di gateway e connessione, diagnostica, risoluzione.
- `src/TRobot.Web/Program.cs`: registrazioni e attivazione condizionale del worker.
- Test: `tests/TRobot.Tests/MessageDeliveryTests.cs`, aggiornamenti a `MessageTests.cs`, `AutomaticSchedulerTests.cs`, `PollTests.cs`.

## Migrazione

`20261119084028_MessageDelivery` (additiva, conservativa):

- nuove colonne su `OutboundMessages`: `DeliveryMode` (default 0), `CreatedAtUtc`, `UpdatedAtUtc`, `ConfirmedAtUtc`, `ConfirmedByActorId`, `TargetGroupId`, `Attempts` (default 0), `LastAttemptAtUtc`, `LastError`, `OutcomeNote`;
- backfill `CreatedAtUtc` da `ScheduledAtUtc` e `UpdatedAtUtc` da `SentAtUtc` o `ScheduledAtUtc`;
- vincoli: modalita' 0/1, stati 0-6, esito coerente con `SentAtUtc`, campi di conferma tutti presenti o tutti nulli, tentativi non negativi;
- FK `ConfirmedByActorId` verso `People` (Restrict) e indice `(DeliveryMode, Status, ScheduledAtUtc)`;
- nessuna riga modificata oltre al backfill: tutte le righe preesistenti restano `Simulated`, comprese le vecchie `Queued`.

Rollback del solo schema: `Down` riporta 4 a `Queued` e 5/6 a `Cancelled` prima di ripristinare il vincolo precedente. Un rollback dei binari richiede comunque un backup coerente: gli stati nuovi non esistono nella versione precedente.

## Configurazione

Nomi delle variabili (valori fuori dal repository):

```text
Messaging__Dispatcher__Enabled            # false di default; true abilita il worker
Messaging__Dispatcher__IntervalSeconds    # default 30, limitato tra 5 e 300
WhatsAppGateway__Enabled
WhatsAppGateway__ProductionEnabled
WhatsAppGateway__BaseUrl                  # Production: solo http://127.0.0.1:5240
WhatsAppGateway__Token
```

Lo scheduler simulato locale mantiene `LocalScheduler__Enabled` e la registrazione solo in Development. Il worker di invio reale viene registrato anche in Production quando `Messaging__Dispatcher__Enabled` e' `true`; con valore `false` il codice resta installato e gli invii restano in attesa, elaborabili con `Elabora ora`.

Protezioni attive: pausa invii per evento, orari di silenzio e limite giornaliero per i non operativi, verifica dell'attivatore del gruppo e dell'organizzatore che ha confermato, gruppo confermato invariato, massimo 20 invii per evento per ciclo, nessun reinvio automatico degli esiti incerti. I log riportano solo conteggi, identificativi evento e codici tecnici, mai testo o identificativi WhatsApp.

## Verifiche eseguite

- `dotnet build TRobot.sln --no-restore`: riuscita senza errori o avvisi.
- `dotnet test TRobot.sln --no-build --no-restore`: 711/711 test superati (21 novembre 2026).
- `dotnet ef migrations has-pending-model-changes --project src/TRobot.Infrastructure --startup-project src/TRobot.Infrastructure --no-build`: nessuna differenza.
- Test mirati: conferma esplicita e gruppo memorizzato; stato live distinto per gateway, connessione e gruppo; invio immediato e programmato; gateway disabilitato e pausa; rifiuto definitivo; timeout e 5xx come incerti senza retry; claim interrotto recuperato; doppio dispatch concorrente con un solo invio; riavvio del worker senza duplicati; gruppo cambiato dopo la conferma; autorizzazione revocata; permessi; risoluzione manuale; coda storica esclusa dopo l'upgrade; simulazione che non puo' marcare inviato un messaggio WhatsApp; flusso HTTP di conferma ed elaborazione.

Non eseguiti: test Node del gateway (codice gateway invariato), invii reali di prova, accesso al database o ai servizi Azure, collaudo dal telefono.

## Procedura di rilascio (quando richiesta)

1. Backup coerente su Azure con Web e gateway fermi: DB, eventuali WAL, chiavi Data Protection, configurazione e stato WhatsApp, secondo `docs/AZURE_CLOUD.md`.
2. Rehearsal della migrazione `20261119084028_MessageDelivery` su copia del DB corrente, con FK attive; confronto di tutte le righe di `OutboundMessages` e `BotSettings` prima/dopo; verifica `integrity_check` e `foreign_key_check`.
3. Publish `linux-arm64` e pacchetto Web; gateway invariantato (nessuna modifica necessaria).
4. Applicazione della migrazione in transazione, servizi riavviati, verifica `/health` del gateway e gruppo originale connesso.
5. Collaudo controllato con `Messaging__Dispatcher__Enabled=false`: composizione, anteprima, conferma, stato in attesa, `Elabora ora` su un messaggio operativo concordato con l'organizzatore.
6. Solo dopo il collaudo, attivazione esplicita del worker con `Messaging__Dispatcher__Enabled=true` e intervallo desiderato; monitoraggio dei log per conteggi e stati.
7. Verifica che le righe storiche restino `Simulated` e che nessuna sia passata a `Sending`/`Sent`.

## Rollback

1. Fermare il worker rimuovendo `Messaging__Dispatcher__Enabled` (o impostandolo a `false`) e riavviare il Web: nessun invio parte, i messaggi restano in coda.
2. Se serve tornare ai binari precedenti, ripristinare il pacchetto precedente e il DB dal backup coerente eseguito al punto 1. Non ripristinare un DB storico senza aver prima fermato Azure e acquisito una copia corrente.
3. Non cancellare righe di `OutboundMessages` per "ripulire": gli stati storici restano consultabili.

## Stato dello scheduler Production

Disabilitato. Questo incarico non ha creato o attivato servizi su Azure, non ha aperto sessioni WhatsApp e non ha inviato messaggi. L'attivazione resta un passaggio esplicito, successivo al collaudo controllato.
