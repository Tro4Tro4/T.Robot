# M36 - Più eventi sullo stesso numero WhatsApp

Disegno tecnico del 27 novembre 2026. Stato: implementata e rilasciata su Google il 27 novembre 2026 (vedi "Rilascio" in fondo). Il testo di disegno sotto è in parte storico.

## Obiettivo

Un solo numero WhatsApp dedicato deve poter servire contemporaneamente più eventi. Ogni evento ha un solo gruppo attivo, ma più eventi possono avere gruppi attivi nello stesso momento. Ogni gruppo può abilitare un sottoinsieme delle funzioni del bot.

La stessa socket Baileys resta l'unica sessione del numero. Non si introducono più numeri, più processi gateway o una sessione per gruppo.

## Non obiettivi

- Più gruppi dello stesso evento.
- Più numeri WhatsApp o più sessioni Baileys.
- Un gruppo condiviso da due eventi.
- Cambio automatico del gruppo durante una singola richiesta.
- Feature flag per persona o per ruolo: i flag sono per gruppo.
- Modifica dei moduli del sito o dei comandi simulati.
- Attivazione automatica del worker M35 o invii di prova durante la migrazione.

## Criteri di successo

1. Due eventi con gruppi diversi ricevono e inviano contemporaneamente.
2. Un comando nel gruppo A modifica solo dati dell'evento A.
3. Un comando nel gruppo B non può leggere approvazioni, presenze o persone di A.
4. Disattivare una funzione in A non cambia B.
5. Disattivare il gruppo A lascia B operativo.
6. Un riavvio o un recovery ripristina tutti i gruppi attivi e autorizzati.
7. Un gruppo non più disponibile su WhatsApp non blocca gli altri eventi.
8. Il dispositivo può essere disconnesso solo con un'azione globale esplicita.

## Vincolo attuale

Il limite è in tre punti:

- `WhatsAppConnection` ha `Id = EventId`, quindi una riga per evento; la migrazione M36 ha rimosso l'indice unico globale su `Enabled`, ma Application e gateway usano ancora un solo gruppo attivo.
- `IWhatsAppStore.ActiveConnectionAsync` usa `SingleOrDefault(x => x.Enabled)`, senza filtro per evento.
- Il gateway mantiene un solo `activeGroupId`; membri, invio, ricezione e recovery usano quel singolo gruppo.

Il modello a un gruppo per evento è quindi già presente nella riga, ma non è ancora possibile attivare più righe.

## Modello dati

Si mantiene `WhatsAppConnection` come collegamento di un evento al proprio gruppo. Non si introduce una nuova entità.

Campi aggiunti:

| Campo | Tipo | Significato |
| --- | --- | --- |
| `FeatureAttendance` | `bool` | `!presenza`, dialogo soggiorno e famiglia |
| `FeatureInformation` | `bool` | `!aiuto`, `!menu`, `!todo`, `!riepilogo` |
| `FeatureOperations` | `bool` | `!porto`, `!prendo`, `!fatto`, `!conferma` con codice |
| `FeatureMealProposals` | `bool` | `!proponi` |
| `FeatureMealPreferences` | `bool` | `!preferenze` |
| `FeatureFunReplies` | `bool` | frasi ironiche aggiuntive, non la sintassi |
| `FeaturesRevision` | `int` | revisione separata delle sole funzionalità |

`Revision` resta la revisione del collegamento usata da bozze, approvazioni, dialoghi e messaggi. `FeaturesRevision` è separata: cambiare i flag non deve invalidare un'anteprima, un'approvazione o un dialogo già aperto. Entrambe sono token di concorrenza EF; un aggiornamento concorrente dei flag produce un conflitto, non un'ultima scrittura silenziosa.

### Default

- Righe esistenti dopo la migrazione: tutte le funzioni attive, per conservare il comportamento attuale.
- Nuovo collegamento: `FeatureAttendance` e `FeatureInformation` attivi; le altre funzioni disattivate finché l'organizzatore non le abilita.
- Una funzione disattivata non blocca i messaggi manuali del pannello: i flag riguardano i comandi WhatsApp, non la consegna dei messaggi confermati.
- `!aiuto` mostra solo le voci abilitate. Un comando disabilitato viene ignorato senza risposta.

### Metodo di dominio

`WhatsAppConnection.UpdateFeatures(...)` riceve i sei valori, confronta lo stato, incrementa solo `FeaturesRevision` e restituisce `false` se non cambia nulla. Non tocca `GroupId`, `ActorId`, `ActivatedAtUtc`, `Enabled` o `Revision`.

`Allows(WhatsAppFeature)` centralizza la mappatura, così servizio, help e test usano la stessa regola.

## Indici e vincoli

- Rimosso l'indice unico `IX_WhatsAppConnections_Enabled`.
- Nuovo indice non unico su `Enabled`, per l'elenco dei collegamenti attivi.
- Nuovo indice unico filtrato su `GroupId` con `Enabled = 1`: lo stesso gruppo non può essere attivo per due eventi. Le righe disabilitate possono conservare lo storico.
- `EventId` diventa univoco: una sola riga di collegamento per evento, coerente con `Id = EventId`.
- Restano il vincolo `CK_WhatsAppConnections_Active` e le FK esistenti verso evento, persona e attivatore.

Gli alias restano per evento. Poiché ogni gruppo appartiene a un solo evento, un'identità associata in A non può autorizzare comandi in B. Non serve aggiungere `GroupId` a `WhatsAppIdentityAlias`; un test di isolamento deve comunque dimostrarlo.

## Migrazione

Nome: `20261127072505_MultiEventWhatsApp`.

Operazioni additive e conservative:

1. Aggiunta delle sei colonne booleane con default `false`.
2. Aggiunta di `FeaturesRevision` con default `0`.
3. Backfill di tutte le righe esistenti: sei funzioni a `true`, `FeaturesRevision` a `0`.
4. Rimozione dell'indice unico su `Enabled`; ricreazione non unica.
5. Creazione dell'indice unico filtrato su `GroupId` per `Enabled = 1`.
6. Sostituzione dell'indice su `EventId` con uno univoco.
7. Nessuna modifica a persone, presenze, messaggi, approvazioni o sessioni.

`Down` elimina colonne e indici nuovi e ricrea l'indice unico su `Enabled`. Se nel frattempo sono attivi più gruppi, la ricreazione dell'indice fallisce intenzionalmente: non deve essere possibile tornare al vecchio codice lasciando più righe attive. In quel caso il rollback richiede backup coerente e disattivazione delle righe eccedenti.

La migrazione viene provata su una copia sintetica con una riga attiva e una storica disattivata, verificando flag, indici e assenza di modifiche ai dati applicativi.

## Protocollo gateway

Il gateway passa da un gruppo attivo a un insieme di gruppi autorizzati.

### Stato

`GET /health` restituisce:

```json
{
  "status": "connected",
  "activeGroupIds": ["120000000000000001@g.us"],
  "unavailableGroupIds": []
}
```

`activeGroupIds` è l'insieme corrente. `unavailableGroupIds` è lo scostamento rilevato all'ultima sincronizzazione, non un dato in tempo reale. `qrDataUrl`, `errorCode` e i contatori diagnostici restano invariati.

### Autorizzazione dei gruppi

`PUT /groups` sostituisce l'insieme:

```json
{ "groupIds": ["120000000000000001@g.us", "120000000000000002@g.us"] }
```

Risposta:

```json
{
  "activeGroupIds": ["120000000000000001@g.us"],
  "unavailableGroupIds": ["120000000000000002@g.us"]
}
```

Regole:

- massimo 50 gruppi, identificativi distinti e nel formato `@g.us`;
- richiede una socket connessa;
- ogni gruppo richiesto viene confrontato con `socket.groups()`;
- i disponibili entrano nell'insieme, gli altri tornano in `unavailableGroupIds`;
- un gruppo non disponibile non blocca gli altri;
- un elenco vuoto disattiva tutti i gruppi;
- la sostituzione incrementa una `routingEpoch` globale: i callback già accodati da un insieme precedente vengono scartati. È una scelta conservativa, accettabile perché la sincronizzazione avviene solo su attivazione, disattivazione, connessione e recovery.

Il vecchio `POST /group` viene rimosso. Web e gateway vengono rilasciati insieme, con una breve finestra di manutenzione.

### Membri, invio e ricezione

- `GET /groups/{groupId}/members` richiede che il gruppo sia nell'insieme attivo e ancora disponibile.
- `POST /send` richiede che `groupId` sia nell'insieme attivo. Il journal resta per operazione e include già il gruppo nel digest.
- `incoming()` accetta un messaggio di gruppo solo se `remoteJid` è nell'insieme attivo.
- Un messaggio di un gruppo disattivato viene scartato prima del callback.
- Il limite di frequenza per i gruppi diventa `groupId + ':' + sender`, così due eventi non consumano lo stesso budget di sei comandi al minuto. I messaggi privati mantengono un budget separato per mittente.
- Il limite globale di callback accodati resta 50.

### Connessione e disconnessione

- `connect()` apre la socket ma non attiva gruppi.
- Dopo il collegamento il Web invia `PUT /groups` con tutti i gruppi attivi autorizzati.
- `disconnect()` chiude la socket e svuota l'insieme attivo.
- Una riconnessione interna mantiene l'insieme corrente; il recovery lo ricostruisce dal database.
- Il recovery ripristina tutti i gruppi disponibili e fallisce solo se nessuno è attivabile. Un gruppo indisponibile viene registrato solo come conteggio, senza bloccare gli altri eventi.
- Un gruppo rimosso da WhatsApp resta nell'insieme fino alla sincronizzazione successiva. L'invio fallisce in modo esplicito e il pannello mostra il gruppo non disponibile.

## Routing applicativo

### Gruppo verso evento

Per un messaggio di gruppo il percorso è:

1. validazione di `groupId`, `messageId`, testo e timestamp;
2. `ConnectionByGroupAsync(groupId)` cerca un collegamento `Enabled = 1`;
3. verifica che l'attivatore sia ancora amministratore globale e assegnato all'evento;
4. verifica che il timestamp non preceda l'attivazione;
5. risoluzione dell'alias del mittente nell'evento;
6. controllo della funzione abilitata;
7. transazione sull'evento risolto.

Nessuna query usa un "evento attivo globale". Il gruppo è la chiave di instradamento.

### Interfacce store

`IWhatsAppStore`:

- `ActiveConnectionsAsync()` restituisce tutti i collegamenti attivi, solo per discovery e sincronizzazione;
- `ConnectionByGroupAsync(groupId)` restituisce il collegamento attivo del gruppo;
- resta `IsAdministratorAsync`, `ParticipantAsync`, `ReceiptAsync`, `AuditsSinceAsync`;
- `ActiveConnectionAsync()` viene rimosso.

`WhatsAppService` aggiunge:

- `RequireConnectionByGroup(groupId)`;
- `SyncGroupsAsync()`: carica i collegamenti attivi, esclude quelli con attivatore non più autorizzato e invia l'elenco al gateway;
- `DeactivateGroupAsync(...)`: disattiva un solo collegamento e sincronizza il gateway;
- `DisconnectDeviceAsync(...)`: azione globale, riservata all'amministratore globale, disattiva tutti i collegamenti e chiude la socket.

`SelectGroupAsync` non verifica più "un altro evento ha un gruppo attivo". Verifica che il gruppo non sia già attivo per un altro evento, disattiva l'eventuale gruppo precedente dello stesso evento, sincronizza il gateway e attiva il nuovo collegamento. Se la sincronizzazione fallisce, il collegamento resta disattivato e il recovery successivo riallinea il gateway.

### Feature gate

| Comando | Funzione |
| --- | --- |
| `!presenza`, risposte del dialogo presenze | `FeatureAttendance` |
| `!aiuto`, `!menu`, `!todo`, `!riepilogo` | `FeatureInformation` |
| `!porto`, `!prendo`, `!fatto`, `!conferma <codice>` | `FeatureOperations` |
| `!proponi` | `FeatureMealProposals` |
| `!preferenze` | `FeatureMealPreferences` |
| frase ironica su sintassi errata | `FeatureFunReplies` |

La sintassi corretta e il messaggio di aiuto della funzione restano disponibili anche con `FeatureFunReplies` disattivata. Se la funzione principale è disattivata, il comando viene ignorato.

`!conferma` è ambiguo solo a livello lessicale: nel gruppo è operativo, in privato appartiene al dialogo pasti o preferenze. Il controllo avviene dopo aver risolto il contesto.

### Messaggi privati

Un DM non contiene il gruppo di origine. `!proponi` e `!preferenze` continuano a usare il dialogo privato, ma il routing deve scegliere l'evento senza permettere crossover.

Regole:

1. Il gateway inoltra un DM senza gruppo di origine: `groupId = null`, `isPrivate = true`. Non mantiene mappe o stato di routing privato.
2. L'app calcola gli hash del mittente e cerca le sessioni aperte di `MealProposalSession` e `MealPreferenceSession` tra i collegamenti attivi, verificando per ciascuna collegamento, attivatore, identità, revisione, scadenza e funzione abilitata.
3. Se esiste esattamente una sessione valida, il DM viene instradato a quella sessione.
4. Se non esiste alcuna sessione valida, il DM viene ignorato: nessun dialogo viene aperto da un messaggio privato.
5. Se esistono più sessioni valide, il DM viene ignorato senza risposta e senza scritture. Non si sceglie l'evento più recente, il primo o un evento arbitrario. L'utente deve attendere la scadenza di una sessione o ripartire quando ne resta una sola.
6. Se la funzione della sessione è stata disattivata, la sessione viene chiusa e il messaggio ignorato.

Il contratto `WhatsAppIncoming.GroupId` diventa nullable. Per un messaggio di gruppo resta obbligatorio; per un DM è null. Il controller continua a richiedere loopback, bearer e rate limit.

### Stato e privacy

`WhatsAppService.HealthAsync(actorId, eventId)` non restituisce l'elenco completo dei gruppi dell'istanza. Filtra la risposta sul gruppo dell'evento richiesto: `activeGroupIds` contiene solo quel gruppo, se attivo. Un organizzatore di A non deve vedere gli identificativi dei gruppi di B.

`MessageDispatchService.StatusAsync` segue la stessa regola e continua a esporre un solo `ActiveGroupId`, quello dell'evento. Il confronto diventa: il gruppo è nell'insieme attivo e non è nell'elenco degli indisponibili.

`GroupsAsync` continua a elencare tutti i gruppi del dispositivo per la scelta del gruppo, perché è un'operazione riservata a un amministratore globale assegnato all'evento. `MembersAsync` resta limitato al gruppo dell'evento.

## Pannello

### Pagina WhatsApp dell'evento

- Mostra il gruppo dell'evento, come oggi.
- Aggiunge la sezione "Funzioni abilitate" con sei checkbox, `FeaturesRevision` nascosta e conferma esplicita.
- Il salvataggio richiede amministratore globale, assegnazione all'evento e connessione esistente.
- "Disattiva questo gruppo" disattiva solo il collegamento dell'evento, conservando associazioni, approvazioni, sessioni storiche e funzioni.
- "Disconnetti dispositivo" diventa un'azione globale con avviso esplicito: disattiva tutti i gruppi e chiude la socket. Richiede amministratore globale e conferma.
- Il JavaScript di stato usa `activeGroupIds` e verifica la presenza del gruppo dell'evento. Non presume più `activeGroupId`.

### Pagina Messaggi

- Resta per evento.
- `ActiveGroupId` continua a indicare il gruppo dell'evento.
- `GroupMatches` significa che il gruppo è nell'insieme attivo del gateway.
- "Elabora ora" e il worker non cambiano interfaccia.
- I messaggi manuali restano disponibili anche con tutte le funzioni WhatsApp disattivate, perché i flag non riguardano la consegna.

### Aiuto

Le voci di aiuto usano i flag del collegamento. L'indice e le singole voci mostrano solo funzioni attive. `!aiuto presenza` senza `FeatureAttendance` risponde "voce non trovata o non attiva"; `!aiuto pasti` distingue proposte e preferenze; `!aiuto elenchi` considera `FeatureInformation` e `FeatureOperations`.

## Dispatcher e scheduler

`MessageDispatchWorker` scopre già gli eventi con messaggi dovuti e li elabora uno per uno. Non serve una modifica strutturale.

Modifiche:

- `DispatchEventAsync` esegue un preflight `StatusAsync`: se il gateway non è connesso o il gruppo dell'evento non è attivo, non fa claim e restituisce `Empty`. I messaggi restano `Queued`.
- Il claim continua a verificare `TargetGroupId == group` e l'autorizzazione dell'organizzatore che ha confermato.
- Un invio verso un gruppo non più attivo resta un fallimento noto o un esito incerto secondo la risposta del gateway; non viene mai reindirizzato a un altro gruppo.
- Il limite di 20 invii per evento per ciclo e il recupero dei claim scaduti restano invariati.
- Pausa, silenzio, limite giornaliero e scheduler simulato restano per evento. Non diventano per gruppo, perché un evento ha un solo gruppo.

## Sicurezza

- Un gruppo attivo appartiene a un solo evento, garantito dall'indice unico filtrato.
- Ogni comando ripete autorizzazione, appartenenza all'evento, alias e timestamp dentro la transazione.
- Gli alias restano per evento; nessuna identità di A autorizza B.
- Le approvazioni restano legate a `GroupId` e `ConnectionRevision`; i flag non le invalidano.
- Il routing privato non sceglie mai un evento in caso di ambiguità: con zero o più sessioni valide il messaggio viene ignorato.
- Il pannello non espone gruppi di altri eventi.
- I log riportano conteggi e identificativi evento, mai testo, JID, nomi o QR.
- Disconnettere il dispositivo è un'azione globale riservata all'amministratore globale, con conferma e audit per ogni evento coinvolto.
- La migrazione non attiva il worker M35 e non invia messaggi.

## Test

### Isolamento tra due eventi

Scenario base: eventi A e B, gruppi `group-a@g.us` e `group-b@g.us`, stesso numero, due righe attive, lo stesso mittente associato a persone diverse nei due eventi.

| Caso | Verifica |
| --- | --- |
| Presenza in A | cambia solo il partecipante di A; B resta `NoResponse` |
| Presenza in B | cambia solo il partecipante di B |
| Riepilogo in A | conteggi e persone solo di A |
| Menu/todo in A | usa solo approvazioni e contenuti di A |
| Approvazione in A | `!menu` in B non la vede |
| Feature Information off in A | `!menu` in A ignorato; B continua |
| Feature Attendance off in B | `!presenza` in B ignorato; A continua |
| Gruppo A disattivato | messaggi A ignorati; B riceve e invia |
| Attivatore A revocato | A ignorato; B continua |
| Messaggio manuale A | `TargetGroupId` A, invio solo ad A |
| Messaggio manuale B | `TargetGroupId` B, invio solo a B |
| Gruppo già attivo per A | attivazione per B rifiutata dal vincolo |
| Gateway con due gruppi | invio e membri consentiti per entrambi; terzo gruppo rifiutato |
| Recovery con due gruppi | entrambi ripristinati; uno indisponibile non blocca l'altro |

### Dialoghi privati

- `!proponi` in A crea una sessione aperta in A; con A come unica sessione il DM successivo usa A e non B.
- `!preferenze` in B crea una seconda sessione; con due sessioni aperte il DM viene ignorato e non scrive su nessun evento.
- Dopo la scadenza o la chiusura di una delle due sessioni, il DM viene instradato alla sola sessione rimasta.
- Zero sessioni: il DM viene ignorato e non ne apre una nuova.
- Feature pasti disattivata: la sessione viene chiusa e il DM ignorato.
- Il gateway non conserva stato di routing privato; il comportamento dipende solo dalle sessioni persistenti e verificabili.

### Migrazione e modello

- Migrazione su SQLite con una riga attiva e una disattivata.
- Backfill di tutte le funzioni a `true`.
- Indice unico su `EventId`.
- Indice unico filtrato su `GroupId`.
- Inserimento di due righe attive con lo stesso gruppo rifiutato dal DB.
- `Down` con più righe attive fallisce senza perdere dati.
- `dotnet ef migrations has-pending-model-changes` pulito.

### Gateway Node

- `PUT /groups` valida elenco, duplicati, formato e limite.
- Un gruppo non disponibile finisce in `unavailableGroupIds` senza bloccare gli altri.
- `members` e `send` accettano solo gruppi attivi.
- `incoming` scarta gruppi non attivi e accetta entrambi i gruppi attivi.
- Il journal resta per operazione e gruppo.
- Il rate limit è separato per gruppo.
- I DM vengono inoltrati con `groupId = null` e non esiste stato di routing privato nel gateway.
- La disconnessione svuota l'insieme attivo.
- Autenticazione, loopback, callback HTTPS e filtri M33/M34 restano invariati.

### Web

- La pagina WhatsApp mostra i sei flag e salva con revisione corretta.
- Un aggiornamento concorrente dei flag restituisce conflitto.
- Modificare i flag non cambia `ConnectionRevision`.
- "Disattiva questo gruppo" non tocca gli altri eventi.
- "Disconnetti dispositivo" richiede conferma e amministratore globale.
- Lo stato Messaggi usa il gruppo dell'evento e non espone altri gruppi.
- Le pagine restano protette da login, CSRF e autorizzazione per evento.

### Comandi di verifica previsti

```text
dotnet build TRobot.sln --no-restore
dotnet test TRobot.sln --no-build --no-restore
dotnet ef migrations has-pending-model-changes --project src/TRobot.Infrastructure --startup-project src/TRobot.Infrastructure --no-build
```

Dalla cartella `gateway/TRobot.WhatsApp`:

```text
npm ci --no-audit --no-fund
npm test
```

I test del primo incremento coprono dominio, backfill delle righe esistenti, indici e downgrade. I conteggi completi vanno aggiornati dopo l'esecuzione dell'intera suite.

## Ordine di implementazione

1. Dominio: campi, enum, `Allows`, `UpdateFeatures`, test di dominio. Fatto.
2. Infrastruttura: configurazione EF, migrazione, test di migrazione. Fatto.
3. Gateway: insieme attivo e `PUT /groups`, test Node.
4. Application: store per gruppo, routing, feature gate, DM, sincronizzazione.
5. Web: pannello funzioni, disattivazione per gruppo, disconnessione globale, stato.
6. Messaggi: preflight, stato per evento, test con due eventi.
7. Recovery e script operativi: query multipla, `PUT /groups`, test Python.
8. Suite completa, controllo EF e review indipendente.

## Rilascio e rollback

Rilascio atomico, con la procedura Azure esistente:

1. backup coerente di SQLite, WAL e configurazione;
2. stop del Web e del gateway;
3. migrazione sulla copia corrente e rehearsal;
4. pubblicazione di gateway e Web M36 insieme;
5. avvio, `PUT /groups` con tutti i gruppi attivi, verifica di due eventi;
6. controllo che il worker M35 resti nello stato precedente e che nessun messaggio di prova parta.

Rollback:

- con una sola riga attiva, `Down` ripristina lo schema precedente;
- con più righe attive, ripristinare il backup coerente e poi i binari precedenti, oppure disattivare le righe eccedenti prima del `Down`;
- gateway e Web devono tornare insieme alla versione precedente;
- non riabilitare copie Oracle o PC e non duplicare la sessione WhatsApp.

## File interessati

- `src/TRobot.Domain/WhatsAppRecords.cs`
- `src/TRobot.Infrastructure/Persistence/WhatsAppConfiguration.cs`
- `src/TRobot.Infrastructure/Persistence/Migrations/20261127072505_MultiEventWhatsApp.cs`
- `src/TRobot.Application/WhatsApp/Contracts.cs`
- `src/TRobot.Application/WhatsApp/WhatsAppService.cs`
- `src/TRobot.Application/WhatsApp/WhatsAppHelp.cs`
- `src/TRobot.Application/WhatsApp/WhatsAppFunReplies.cs`
- `src/TRobot.Application/WhatsApp/WhatsAppInformationService.cs`
- `src/TRobot.Application/WhatsApp/WhatsAppMealProposalService.cs`
- `src/TRobot.Application/WhatsApp/WhatsAppMealPreferencesService.cs`
- `src/TRobot.Application/Messaging/MessageDispatchService.cs`
- `src/TRobot.Application/Messaging/MessageService.cs`
- `src/TRobot.Infrastructure/Persistence/WhatsAppStore.cs`
- `src/TRobot.Infrastructure/WhatsApp/WhatsAppGatewayClient.cs`
- `src/TRobot.Web/Controllers/WhatsAppController.cs`
- `src/TRobot.Web/Controllers/WhatsAppIncomingController.cs`
- `src/TRobot.Web/Controllers/MessagesController.cs`
- `src/TRobot.Web/Models/WhatsAppModels.cs`
- `src/TRobot.Web/Views/WhatsApp/Index.cshtml`
- `src/TRobot.Web/wwwroot/js/whatsapp.js`
- `gateway/TRobot.WhatsApp/src/gateway.js`
- `gateway/TRobot.WhatsApp/src/http.js`
- `deploy/oracle/resume-gateway.py`
- `deploy/oracle/test_gateway_ops.py`
- `tests/TRobot.Tests/WhatsAppIntegrationTests.cs`
- `tests/TRobot.Tests/MessageDeliveryTests.cs`
- `tests/TRobot.Tests/MessageTestSupport.cs`
- nuovi test di migrazione, isolamento multi-evento e routing privato

## Decisioni adottate

- Un gruppo per evento, più eventi attivi sullo stesso numero.
- `WhatsAppConnection` resta l'entità del collegamento; nessuna nuova entità canale.
- Feature flag booleani per gruppo, con revisione separata.
- Righe esistenti tutte attive; nuovi gruppi con Attendance e Information, le altre da abilitare.
- Gateway con insieme di gruppi attivi e `PUT /groups`.
- DM instradati solo con una sessione aperta unica del mittente; ambiguità ignorata, nessun routing privato memorizzato nel gateway.
- Disconnessione del dispositivo globale e riservata all'amministratore globale.
- Pausa, silenzio, limite e scheduler restano per evento.

## Stato dell'implementazione

Implementato il 27 novembre 2026 e unito in `main` (`0cfd2af`). Build della soluzione senza errori o avvisi, suite .NET completa superata da desktop (729 test), 61 test gateway Node superati (Node 26 locale, fuori da `engines`) e test operativi Python del recovery superati. Nessuna modifica di modello pendente.

## Rilascio del 27 novembre 2026

Rilasciata su Google su richiesta esplicita dell'utente come Web e gateway `multi-evento-20261127d-x64` (commit `b333de3`), con migrazione `MultiEventWhatsApp`, tabella `WhatsAppConnections` autorizzata con `--allow-changes`. Backup, rehearsal e applicazione hanno conservato righe, identità e gruppo; `verify` e smoke positivi, gateway riconnesso al gruppo originale. Non testato dal telefono.

È stata la prima esecuzione reale di `activate` sulla VM Google. Tre tentativi (`multi-evento-20261127-x64`, `...b-x64`, `...c-x64`) sono falliti prima di `...d-x64`, per tre difetti corretti nei commit `cde53e6` e `b333de3`:

1. `release.py` assumeva un solo gruppo (`binding[2]`): ora `gateway_health` accetta l'elenco dei gruppi e legge sia `activeGroupIds` (gateway M36) sia `activeGroupId` (gateway precedente).
2. `release.py` creava `ops/migrations` a `700` perché `mkdir(mode=0o750)` è ridotto dall'umask 077, quindi l'utente `trobot` non leggeva lo SQL: ora c'è un `chmod` esplicito.
3. Lo script SQL della migrazione `MultiEventWhatsApp` aveva un `UPDATE` senza `;` finale, fuso con il `CREATE INDEX` successivo (`near "CREATE": syntax error`): aggiunto il terminatore nel `Sql(...)` della migrazione.

In più `deploy/oracle/resume-gateway.py` inviava `POST /groups` mentre il gateway accetta solo `PUT /groups`: ora usa `PUT`, con ripiego su `POST /group` se il gateway è quello precedente (necessario al rollback). Il recovery sulla VM non fa parte dei pacchetti di `publish.py`: va installato a mano in `/opt/trobot/ops/resume-gateway.py` (root, `700`) prima di `activate`. Il secondo tentativo ha lasciato per qualche minuto il gateway fermo e il timer di backup disattivo, perché il rollback si è appoggiato a un recovery incompatibile: ripristinati a mano, gruppo riconnesso da solo.
