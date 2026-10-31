# Rimozione circoscritta dello scenario sintetico

Tool manutentivo monouso, senza endpoint Web. Non offre eliminazione generale degli eventi.
Consente soltanto l'evento `ce014f6d-1a90-4388-b187-771235bcb918` con il nome originale esatto.

Prima dell'uso:

- Spostare o disabilitare il collegamento WhatsApp tramite i servizi applicativi esistenti.
- Fermare Web, gateway e timer backup per ottenere un database quiescente.
- Creare e verificare un backup privato coerente di database, chiavi, configurazione e sessione.
- Eseguire prima dry-run e apply su una copia privata del backup, sullo stesso server.

Pubblicazione framework-dependent ARM64:

```powershell
dotnet publish tools/TRobot.ScenarioCleanup/TRobot.ScenarioCleanup.csproj -c Release -r linux-arm64 --self-contained false -p:UseAppHost=false -o .local/scenario-cleanup-publish
```

Dry-run (nessuna cancellazione):

```text
dotnet TRobot.ScenarioCleanup.dll /percorso/trobot.db admin 0
```

Usare la revisione effettiva dell'evento al posto di `0`. Il risultato contiene fingerprint completo del database,
hash delle righe da conservare e conteggi per tabella delle righe dello scenario.

Apply:

```text
dotnet TRobot.ScenarioCleanup.dll /percorso/trobot.db admin 0 FINGERPRINT DELETE:ce014f6d-1a90-4388-b187-771235bcb918 SHA256_BACKUP_VERIFICATO
```

Il valore SHA256 e una ricevuta della verifica esterna del backup, non sostituisce quella verifica.
La conferma esplicita CLI deriva dall'autorizzazione dell'operatore; non introdurre prompt interattivi.

Il servizio richiede amministratore di sistema abilitato, non bloccato e assegnato all'evento.
Rifiuta revisione/nome errati, collegamento WhatsApp ancora abilitato e qualsiasi scrittura intervenuta dopo il dry-run.
Cancella soltanto le righe con EventId corrispondente e la riga Events, preservando People, Identity e altri eventi.
Rifiuta tabelle sconosciute al modello EF. Ogni modifica avviene in una transazione con FK ON e vincoli differiti
per gestire il ciclo Poll/DecisionOption; integrita, FK e hash di tutte le righe esterne sono verificati prima del commit.
Un errore causa rollback. L'esecuzione riuscita non e ripetibile sul database gia ripulito.

Conservare output JSON e backup in posizione privata. Il ripristino dopo un apply riuscito richiede una decisione
operativa esplicita, per evitare di sovrascrivere modifiche reali successive.
