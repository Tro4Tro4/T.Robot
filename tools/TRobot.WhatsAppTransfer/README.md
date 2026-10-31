# Trasferimento WhatsApp circoscritto

Strumento manutentivo per lo spostamento autorizzato del 23 ottobre 2026 dallo scenario sintetico a Capodanno 2027. Non e una API generale e non aggiunge comandi nel portale.

Esegue solo letture e operazioni dei servizi WhatsApp esistenti: Disconnect, Connect, SelectGroup e Map. Non chiama Send. UUID di eventi e persone sono fissati all'operazione confermata dall'utente. Prima della disconnessione verifica amministratore/assegnazioni, nomi, gruppo attivo, quattro alias sorgente, identita dei membri attuali e destinazione vuota oppure con quattro alias esattamente coincidenti per le due persone confermate.

Pubblicazione:

```powershell
dotnet publish tools/TRobot.WhatsAppTransfer/TRobot.WhatsAppTransfer.csproj -c Release -r linux-arm64 --self-contained false -p:UseAppHost=false -o .local/azure/whatsapp-transfer-publish
```

Esecuzione sulla VM come proprietario `trobot`, con `LoadCredential=gateway.json:/etc/trobot-whatsapp/gateway.json`. Il token viene letto esclusivamente da `CREDENTIALS_DIRECTORY`, senza argomenti o output sensibili.

```text
dotnet TRobot.WhatsAppTransfer.dll /var/lib/trobot/trobot.db admin
dotnet TRobot.WhatsAppTransfer.dll /var/lib/trobot/trobot.db admin --apply --confirmed-dan-tro SHA256_BACKUP_VERIFICATO
```

La prima forma verifica soltanto. Prima dell'apply creare/verificare un backup e fermare Web e timer backup per serializzare le operazioni; il gateway deve essere attivo sul gruppo sorgente. Il valore SHA e attestazione della verifica operativa, non verifica automaticamente un file. Su errore tenere Web e gateway fermi e ispezionare la fase riportata; non ripetere automaticamente una sequenza gia parzialmente applicata.

L'operazione e conclusa e lo scenario sorgente e stato rimosso: il tool non va rilanciato. Dettagli in `docs/SCENARIO_REMOVAL.md`.
