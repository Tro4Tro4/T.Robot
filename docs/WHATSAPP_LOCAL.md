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
5. Dal telefono del membro associato inviare nel gruppo `!presenza si`, poi eventualmente `!presenza forse` e `!presenza no`. Il numero collegato al gateway non può collaudare se stesso: i messaggi propri vengono ignorati. La presenza deve aggiornarsi sul sito e il messaggio ricevere una reazione generica.
6. Per provare un invio, preparare un nuovo avviso operativo dalla pagina WhatsApp e controllare nell'anteprima **Nome attuale del gruppo**, ID del gruppo e testo. Il nome viene risolto per il destinatario esatto della bozza; se il collegamento è cambiato o il destinatario non è verificabile, la conferma non è disponibile e la pagina ne spiega il motivo. La preparazione della bozza non invia nulla. Confermare solo dopo il controllo e verificare il messaggio nel gruppo prima di ripetere qualunque operazione con esito incerto.

DM, altri gruppi, messaggi vecchi, allegati, identità non associate e comandi diversi da `!presenza si|forse|no` sono ignorati. I test automatici usano trasporti finti e non attestano il funzionamento del proprio account: la prova con telefono resta necessaria.

## Riavvio e anomalie

**Disconnetti dispositivo** conserva la sessione e lo storico, disattivando il gruppo. Dopo un riavvio o una disconnessione occorre richiedere il collegamento dalla pagina e riattivare esplicitamente il gruppo. Un gruppo ancora indicato nel database non basta ad abilitare un gateway appena avviato.

`Sending` o `Uncertain` significa che il messaggio potrebbe essere già arrivato: controllare il gruppo. Non esiste un reinvio automatico per questi stati. Non cancellare il journal di invio per riprovare: impedisce duplicazioni dopo un arresto. Una bozza preparata prima di cambiare collegamento/gruppo non è più inviabile; preparare una nuova anteprima.

Se compare **Gateway non disponibile**, verificare che il processo Node sia avviato, ricaricare la pagina e verificare l'accesso dell'organizzatore. Il QR non compare nei log né su endpoint anonimi. Non pubblicare screenshot del QR o dati di autenticazione.

La coda e lo scheduler delle pagine **Messaggi e configurazione** restano simulati: non inviano vecchie bozze, sondaggi, quiz o riepiloghi al gruppo reale. Il gateway non importa la cronologia della chat e conserva solo sessione e journal tecnico; il database conserva fingerprint dei mittenti e ricevute senza testo dei messaggi ricevuti. Le bozze operative scritte dagli organizzatori restano nello storico dei messaggi reali.

Dettagli del disegno e delle verifiche: [Milestone 23](MILESTONE_23.md).
