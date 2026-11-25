# M35 - Invio reale dei messaggi del pannello

Incarico del 19 novembre 2026: completare il modulo Messaggi collegandolo al gateway WhatsApp esistente, con invio immediato e programmato, stati affidabili, diagnostica e protezione delle vecchie code simulate.

Stato al 21 novembre 2026: Web M35 pubblicato su Azure e invio manuale dal pannello riuscito. Il worker di invio automatico resta disabilitato (`Messaging__Dispatcher__Enabled` assente o `false`) e va attivato con la procedura sotto. L'invio manuale riuscito conferma che schema M35 e gateway sono attivi; le righe storiche restano `Simulated`.

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

Non eseguiti da questo incarico: test Node del gateway (codice gateway invariato), verifica diretta del database e dei servizi Azure, collaudo dal telefono. L'invio manuale e' stato eseguito dall'utente dal pannello, non da un test automatico.

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

## Stato Azure al 21 novembre 2026

Disabilitato. Questo incarico non ha creato o attivato servizi su Azure, non ha aperto sessioni WhatsApp e non ha inviato messaggi. L'attivazione resta un passaggio esplicito, successivo al collaudo controllato.

## Rilascio Azure 21 novembre 2026

Su richiesta esplicita dell'utente il Web M35 e' stato pubblicato su Azure; gateway invariato in `m34-20261031-arm64`. Release `message-delivery-20261121-arm64` (226 file, 9.157.702 byte, SHA-256 `71c4f5b71023d4eb96f6c537ae4c24a7151680a1445b9856f6cb6d9a238c5c2b`), script di migrazione SHA-256 `9b87540c1959e1d39f8a51c0d6b5bfea3a4e8526040db6f43952579a3ae10a89`. Worker di invio non attivato.

Procedura eseguita: upload e staging senza modifiche ai servizi; arresto di timer di backup, recovery, gateway e Web; snapshot del DB; backup coerente con DB, chiavi Data Protection e stato WhatsApp, piu' copia della configurazione Web; rehearsal della migrazione su copia del DB con FK attive e confronto di tutte le 56 tabelle; applicazione della migrazione in transazione; cambio atomico del link `current`; riavvio Web con verifica di DB, chiavi, configurazione, identita e associazione al gruppo; recovery del gateway `connected` sul gruppo autorizzato; timer di backup riattivato.

Il DB operativo era a `AddMealPreferences`: il rilascio `family-status-20261109-arm64` non era mai stato attivato. Sono quindi state applicate `20261108152657_AddAttendanceFamilyStatus` (colonna nullable `FamilyStatus`, righe conservate) e `20261119084028_MessageDelivery`. Tutte le righe di `OutboundMessages` restano `Simulated` con `DeliveryMode=0`, senza conferme, tentativi o stati di invio; `BotSettings` e le altre tabelle risultano invariate; integrity e foreign key OK.

Verifiche dopo l'attivazione: `/health` del gateway `connected` con `activeGroupId` invariato, NRestarts=0 per Web e gateway, configurazione e chiavi identiche, smoke pubblico con TLS verificato senza override (login originale, 12 pagine riservate, 8 GET/POST ai moduli sospesi 404, 3 POST senza antiforgery 400, `Messages/Status` con gateway connesso e gruppo corrispondente, asset identici ai sorgenti, logout). Report privato `.local/azure/message-delivery-release-smoke-report.json`; contesto e backup sulla VM in `/root/trobot-provisioning/message-delivery-20261121-arm64-context.json`.

Rollback previsto: rimuovere `Messaging:Dispatcher:Enabled` e riavviare il Web; per tornare ai binari precedenti ripristinare il pacchetto precedente e il DB dal backup coerente. Non necessario in questo rilascio.

- Web M35 pubblicato su Azure; `js/messages.js` coincide con il commit e il pannello e' protetto dal login.
- Migrazione `20261119084028_MessageDelivery` attiva; invio manuale dal pannello riuscito.
- Worker di invio automatico ancora disabilitato: `Messaging__Dispatcher__Enabled` assente o `false`.
- Le righe storiche restano `Simulated`; nessun reinvio automatico.

## Attivazione del worker su Azure

Il worker non ha un servizio systemd separato: viene registrato all'avvio di `trobot.service` solo con `Messaging__Dispatcher__Enabled=true`. La configurazione sta in `/etc/trobot/trobot.env`, root 0600, fuori dalla release. Il file contiene anche il token del gateway e non va stampato.

### Prima di attivare

1. Eseguire un backup coerente del DB e delle chiavi secondo `docs/AZURE_CLOUD.md`.
2. Aprire Messaggi e controllare tutte le righe `Confermato, in attesa`: il primo ciclo invia ogni messaggio WhatsApp dovuto, non solo l'ultimo composto.
3. Verificare gateway `connected`, gruppo attivo corrispondente e pausa invii disattivata.
4. Controllare silenzio e limite giornaliero: valgono per i messaggi non operativi; gli operativi li superano.
5. Sapere che il primo ciclo parte subito dopo il riavvio e che il limite e' 20 invii per evento per ciclo.

### Comandi

```bash
sudo grep -n '^Messaging__Dispatcher__' /etc/trobot/trobot.env || true
sudo sed -i '/^Messaging__Dispatcher__Enabled=/d; /^Messaging__Dispatcher__IntervalSeconds=/d' /etc/trobot/trobot.env
printf '%s\n' \
  'Messaging__Dispatcher__Enabled=true' \
  'Messaging__Dispatcher__IntervalSeconds=30' \
  | sudo tee -a /etc/trobot/trobot.env >/dev/null
sudo systemctl restart trobot
sudo systemctl status trobot --no-pager
pid=$(systemctl show -p MainPID --value trobot)
sudo tr '\0' '\n' < /proc/$pid/environ | grep '^Messaging__Dispatcher__'
```

Nel pannello Messaggi la voce `Elaborazione automatica` deve passare ad `Attiva`, oppure a `Sospesa: pausa invii` se la pausa e' attiva. Nei log il worker riporta solo conteggi, mai testo o identificativi WhatsApp.

### Primo ciclo: cosa puo' partire

- Solo righe `DeliveryMode=WhatsApp`, `Status=Queued` e `ScheduledAtUtc <= now`.
- Le righe `Simulated`, comprese le quattro storiche e le vecchie `Queued` simulate, restano escluse.
- Un messaggio confermato con un gruppo diverso da quello attivo, un attivatore non piu' autorizzato, pausa, silenzio o limite giornaliero restano in coda e vengono saltati.
- Un claim `Sending` piu' vecchio di 5 minuti viene marcato `Uncertain`, non reinviato.
- Timeout, rete e 5xx producono `Uncertain`; un 4xx produce `Failed`; nessun esito viene ritentato automaticamente.
- Se ci sono piu' di 20 messaggi dovuti per evento, i successivi restano in coda per il ciclo seguente.

### Dopo l'attivazione

Controllare i conteggi nei log per i primi due o tre cicli, verificare nel gruppo che ogni messaggio compaia una sola volta e che in tabella lo stato sia `Inviato`. Un invio gia' avviato o concluso non puo' essere annullato; un esito incerto si risolve solo manualmente, senza reinvio.

Per fermare il worker:

```bash
sudo sed -i '/^Messaging__Dispatcher__Enabled=/d' /etc/trobot/trobot.env
printf '%s\n' 'Messaging__Dispatcher__Enabled=false' | sudo tee -a /etc/trobot/trobot.env >/dev/null
sudo systemctl restart trobot
```

I messaggi gia' confermati restano in coda e non partono.
