# Collegamento WhatsApp locale sperimentale

Il numero dedicato e il gruppo di prova sono richiesti per questo incremento. Il gateway Baileys è non ufficiale e può subire disconnessioni o restrizioni dell'account. Non è previsto un deploy. Il sito continua a essere riservato agli organizzatori e ad ascoltare soltanto su loopback.

## Preparazione e avvio

Da PowerShell, nella cartella del repository:

```powershell
./scripts/Prepare-WhatsApp.ps1
dotnet build TRobot.sln --no-restore
dotnet ef database update --project src/TRobot.Infrastructure
```

Prima della migrazione conservare una copia consistente di `src/TRobot.Web/trobot.db`. Lo script scarica Node 24.22.0 Windows x64 dal sito ufficiale, verifica SHA256 quando lo installa, esegue `npm ci` e genera il token locale solo se la configurazione non esiste. Non sovrascrive configurazione, token o sessioni. Runtime, configurazione e autenticazione restano sotto `.local`, esclusa da Git. Non condividere `gateway.json` o la cartella di stato.

Avviare due processi locali (se si usa VS Code, il debug Web sostituisce il secondo comando):

```powershell
./gateway/TRobot.WhatsApp/start-gateway.ps1
```

```powershell
dotnet run --project src/TRobot.Web --no-build
```

Il gateway ascolta su `127.0.0.1:5240`; il sito su `http://localhost:5238`. L'avvio del gateway non collega automaticamente il dispositivo. Nessun servizio Windows viene installato. I due processi devono restare aperti durante la prova.

## Prima prova con il telefono

1. Accedere al sito con l'amministratore globale, scegliere un evento assegnato e aprire **WhatsApp**.
2. Confermare il collegamento del numero dedicato e premere **Collega dispositivo e mostra QR**. Sul telefono aprire WhatsApp → **Dispositivi collegati** → **Collega un dispositivo**, quindi scansionare il QR della pagina riservata.
3. Selezionare il gruppo di prova e premere **Attiva questo gruppo**, spuntando la conferma. Per un gateway è ammesso un solo gruppo/evento attivo.
4. Associare un membro a un partecipante già registrato nell'evento. Non vengono importate persone e non vengono alterati i mittenti sintetici del simulatore.
5. Dal telefono del membro associato inviare nel gruppo `!presenza si` e seguire le domande su arrivo e durata descritte sotto; `!presenza forse` e `!presenza no` aggiornano il solo mittente con reazione ✅. Il numero collegato al gateway non può collaudare se stesso: i messaggi propri vengono ignorati.
6. Per provare un invio, preparare un nuovo avviso operativo dalla pagina WhatsApp e controllare nell'anteprima **Nome attuale del gruppo**, ID del gruppo e testo. Il nome viene risolto per il destinatario esatto della bozza; se il collegamento è cambiato o il destinatario non è verificabile, la conferma non è disponibile e la pagina ne spiega il motivo. La preparazione della bozza non invia nulla. Confermare solo dopo il controllo e verificare il messaggio nel gruppo prima di ripetere qualunque operazione con esito incerto.

DM, altri gruppi, messaggi vecchi, allegati e identità non associate sono ignorati. I test automatici usano trasporti finti e non attestano il funzionamento del proprio account: la prova con telefono resta necessaria.

## Presenza, arrivo e giorni

Inviare `!presenza si`: la presenza personale viene confermata e T.Robot chiede quando si arriva. Rispondere nel gruppo con una data numerica, per esempio `30/12` oppure `30/12/2026`, poi con il solo numero di giorni, per esempio `4`. Non aggiungere parole come «giorni» alla risposta. Il riepilogo mostrerà arrivo, ultimo giorno e notti: **30/12 + 4 giorni = fino al 02/01, tre notti**. Le date sono riferite al fuso dell'evento; senza anno viene scelta la data più vicina all'inizio dell'evento.

Per un adulto appartenente a un nucleo attivo, il bot chiede se applicare lo stesso soggiorno a tutta la famiglia. Rispondere `sì` per confermare presenza e date dei membri attivi del nucleo, oppure `no` per registrare le date per sé e per i bambini collegati al proprio riferimento. Gli altri adulti possono rispondere per sé, scegliendo a loro volta di non includere tutto il nucleo. I bambini seguono le stesse date del genitore collegato; non compilano un soggiorno indipendente. Non vengono aggiunti accompagnatori nuovi e non vengono elencati i nomi dei familiari nel gruppo.

Per configurare il collegamento: dal sito **Nuclei → Modifica** sul bambino, scegliere il genitore nel campo **Accompagnatore di** e confermare il salvataggio. Deve essere un adulto primario attivo dello stesso nucleo. Il bot non sceglie un genitore automaticamente quando il riferimento manca.

I dettagli del soggiorno vengono salvati alla conferma finale. Ogni persona ha un dialogo separato, valido 15 minuti nello stesso gruppo e collegamento; risposte brevi fuori da un dialogo attivo sono ignorate. Durante la compilazione, date, numeri e sì/no inviati dal medesimo mittente sono interpretati come risposte alla domanda corrente. Per evitare equivoci, completare la sequenza oppure interromperla con `!annulla`. Questo comando interrompe soltanto la compilazione: non revoca la presenza e non cancella date già registrate. Per correggere, ripartire da `!presenza si`.

`!presenza no` e `!presenza forse` interrompono la compilazione e aggiornano soltanto il mittente. Un eventuale soggiorno precedente resta nello storico, ma non contribuisce al quadro giornaliero finché la persona non è confermata. Se cambiano nucleo, presenze o soggiorni dopo il riepilogo, la conferma obsoleta viene rifiutata: ricominciare senza sovrascrivere le modifiche altrui.

Sono ammessi da 1 a 31 giorni e un arrivo entro 31 giorni prima/dopo la data iniziale dell'evento. Il sito riservato mostra soggiorni e conteggi giornalieri; per i confermati privi di date segnala i dettagli mancanti senza inventare un soggiorno. Queste informazioni non cambiano automaticamente le quote economiche.

### Modificare le date

Inviare `!presenza modifica` per rivedere il soggiorno registrato. Rispondere `1` per cambiare l'arrivo, `2` per cambiare i giorni oppure `3` per cambiare entrambi. Il campo non modificato viene conservato. Seguire le domande e controllare il riepilogo prima della conferma; nel percorso individuale anche i bambini collegati ricevono le stesse date. Annullamento, scadenza o modifiche concorrenti non sovrascrivono i dati precedenti. Chi è confermato ma non ha ancora date può completarle; chi non è confermato deve prima usare `!presenza si`.

### Scadenza e promemoria

In **Evento e partecipanti → Soggiorni**, l'organizzatore può impostare o rimuovere il termine delle adesioni, usando data e ora nel fuso dell'evento e confermando il salvataggio. Gli eventi esistenti partono senza termine. Il bot mostra la scadenza nella guida e all'avvio della compilazione; dopo il termine avvisa ma continua ad accettare risposte e correzioni.

La stessa pagina elenca chi deve rispondere o completare le date. **Prepara promemoria WhatsApp** genera soltanto una bozza generica, senza nomi o dati personali: nell'anteprima WhatsApp controllare il nome del gruppo e confermare l'invio manuale. Nessun invio automatico, messaggio privato o timer. Non sono inclusi fra gli incompleti gli assenti già registrati.

## Riepilogo delle presenze

Inviare `!riepilogo` nel gruppo dal numero associato a un partecipante dell'evento. Il bot mostra i presenti **confermati**, la ripartizione adulti/bambini e il conteggio dei confermati per nucleo, includendo i bambini registrati e confermati anche se non hanno un numero WhatsApp. Ogni persona conta una sola volta. I familiari ancora indecisi, assenti o senza risposta non diventano presenti soltanto perché un adulto del nucleo ha confermato per sé.

Chi non ha una classificazione attiva nel nucleo viene incluso nel totale con età non specificata. Il riepilogo distingue inoltre gli stati generali e segnala in forma aggregata i soggiorni ancora da completare: una presenza confermata senza date conta comunque fra i confermati. Vengono mostrati i nomi dei nuclei e i relativi conteggi, senza nomi individuali, date personali, note o saldi.

Con molti nuclei, proseguire con `!riepilogo 2`: massimo cinque nuclei per pagina, totale generale ripetuto in ogni pagina. Il comando consulta le presenze senza modificarle e può essere usato anche durante una compilazione aperta. Non richiede l'approvazione degli elenchi menu/attività. La guida `!aiuto` resta dedicata alla presenza.

Per un giorno specifico usare `!riepilogo 31/12` oppure `!riepilogo 31/12/2026`; per le altre pagine, per esempio, `!riepilogo 31/12/2026 2`. Contano soltanto i confermati con soggiorno completo che comprende quel giorno, inclusi arrivo e ultimo giorno. I bambini sono inclusi; i confermati senza date sono segnalati separatamente e non assegnati a un giorno inventato. Senza data il comando mantiene il totale generale dell'evento.

## Aiuto, menu e attività

Dal numero privato o dell'amico associato nel gruppo:

```text
!aiuto
!menu
!todo
```

`!aiuto` è temporaneamente dedicato soltanto alla presenza, con istruzioni su arrivo, giorni inclusivi e conferma familiare. Menu e attività richiedono prima l'approvazione dei contenuti dal sito: **WhatsApp → Elenchi condivisibili nel gruppo → Anteprima e approvazione menu/attività**. Controllare il nome del gruppo e tutte le pagine, spuntare la conferma e premere **Approva condivisione elenco**. Approvare non invia messaggi.

La risposta contiene al massimo cinque elementi per pagina, senza responsabili, note o dati delle anagrafiche. Per continuare usare `!menu 2` oppure `!todo 2`. Prima dell'approvazione, o dopo una modifica ai contenuti o al collegamento, il comando restituisce soltanto un avviso generico: occorre una nuova anteprima e approvazione. I titoli sono testo libero: verificare che non contengano dati personali prima di approvarli.

I messaggi sono divisi in blocchi: titolo, riepilogo, singole voci con quantità e stato, poi istruzioni e pagina successiva. Le emoji aiutano a distinguere cosa è pronto, assegnato o da organizzare. Sotto una voce menu libera compare direttamente il comando `!porto` con il riferimento corretto: copiare la riga intera, senza aggiungere il nome del piatto. Le attività riportano il riferimento da usare con `!prendo` o `!fatto`. I comandi da copiare sono su righe separate; emoji e battute restano fuori dal comando.

Dopo l'aggiornamento della formattazione occorre riapprovare **entrambi** gli elenchi: anche il testo presentato fa parte dei contenuti approvati. L'anteprima mostra esattamente le pagine che arriveranno nel gruppo. Non serve ricollegare il dispositivo.

Le risposte informative arrivano come testo nel gruppo; la presenza positiva avvia il dialogo descritto sopra, mentre `!presenza forse|no` mantiene la reazione ✅. Gli impegni sono descritti di seguito; sondaggi e quiz restano nel simulatore. Non sono previsti reinvii automatici delle risposte incerte.

## Assumere e completare impegni

Dal gruppo, un partecipante adulto confermato può usare il riferimento breve mostrato accanto alla voce richiesta:

```text
!porto <idvoce>
!prendo <idattività>
!fatto <idattività>
```

La prima richiesta propone l'operazione e restituisce `!conferma <codice>`: non cambia ancora assegnazioni o attività. Inviare il comando di conferma dallo stesso numero associato entro cinque minuti; una reazione ✅ conferma la registrazione. Il codice visibile nel gruppo funziona solo per il partecipante che ha fatto la richiesta. Ignorandolo la proposta scade. Sono ammesse al massimo tre proposte attive per persona/evento.

Non si possono sostituire incaricati altrui o assumere voci pronte/annullate. `!fatto` completa soltanto un'attività aperta già assegnata al mittente. Se cambia il target dopo la proposta, oppure il collegamento o i permessi, la conferma viene rifiutata: controllare l'elenco e fare una nuova richiesta.

Le modifiche agli stati pubblicabili possono invalidare l'approvazione degli elenchi: l'organizzatore dovrà controllare nuovamente l'anteprima dal sito. I comandi non pubblicano titoli non approvati né nomi degli incaricati. Non inviare i segnaposto `<idvoce>` e `<codice>` letteralmente: usare i valori ricevuti da T.Robot.

## Riavvio e anomalie

**Disconnetti dispositivo** conserva la sessione e lo storico, disattivando il gruppo. Dopo un riavvio o una disconnessione occorre richiedere il collegamento dalla pagina e riattivare esplicitamente il gruppo. Un gruppo ancora indicato nel database non basta ad abilitare un gateway appena avviato.

`Sending` o `Uncertain` significa che il messaggio potrebbe essere già arrivato: controllare il gruppo. Non esiste un reinvio automatico per questi stati. Non cancellare il journal di invio per riprovare: impedisce duplicazioni dopo un arresto. Una bozza preparata prima di cambiare collegamento/gruppo non è più inviabile; preparare una nuova anteprima.

Se compare **Gateway non disponibile**, verificare che il processo Node sia avviato, ricaricare la pagina e verificare l'accesso dell'organizzatore. Il QR non compare nei log né su endpoint anonimi. Non pubblicare screenshot del QR o dati di autenticazione.

La coda e lo scheduler delle pagine **Messaggi e configurazione** restano simulati: non inviano vecchie bozze, sondaggi, quiz o riepiloghi al gruppo reale. Il gateway non importa la cronologia della chat e conserva solo sessione e journal tecnico; il database conserva fingerprint dei mittenti e ricevute senza testo dei messaggi ricevuti. Le bozze operative scritte dagli organizzatori restano nello storico dei messaggi reali.

Dettagli del disegno e delle verifiche: [Milestone 23](MILESTONE_23.md).
