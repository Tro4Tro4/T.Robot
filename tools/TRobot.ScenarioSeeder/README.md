# Caricatore scenario locale T.Robot

Eseguire dalla radice repository, con .NET 10 e database già migrato. Non crea account, migrazioni, connessioni WhatsApp o processi in background. Tutte le modifiche passano dai servizi Application e dall'amministratore esistente abilitato.

```powershell
dotnet build tools/TRobot.ScenarioSeeder/TRobot.ScenarioSeeder.csproj
dotnet run --no-build --project tools/TRobot.ScenarioSeeder -- --database .local/trobot-capodanno-scenario-copy.db --apply --report-path .local/capodanno-copy-report.json
dotnet run --no-build --project tools/TRobot.ScenarioSeeder -- --database .local/trobot-capodanno-scenario-copy.db --report-path .local/capodanno-copy-verification.json
```

Il percorso DB è obbligatorio; senza `--apply` esegue solo verifica. Il report è ammesso esclusivamente sotto `.local`. Il journal `capodanno-seeder-<hash-percorso-db>.json` conserva evento e passi completati: mantenerlo insieme al DB per riprendere un caricamento interrotto. Le operazioni hanno identificativi deterministici. Il marker nel programma impedisce di creare un secondo evento dopo un'interruzione; se il journal manca o non corrisponde il tool rifiuta modifiche all'evento esistente.

Dopo il completamento, anche con `--apply`, apre SQLite in sola lettura e verifica senza correggere o resettare i dati. Modifiche manuali che alterano i criteri della specifica vengono segnalate. Il tool non serve a ripristinare uno scenario modificato.

Il clock del solo caricatore è fissato al 17 ottobre 2026 ore 12:00 Europe/Rome per pubblicazioni e votazioni simulate ripetibili; `ScenarioAsOfUtc` nel report lo dichiara. Le scadenze sono quelle della vacanza 30 dicembre–2 gennaio. Il clock del sito e lo scheduler non cambiano; lo scheduler del nuovo evento resta disattivato. Gli stati Sent appartengono unicamente alla coda simulata.

Specifica: `docs/SCENARIO_CAPODANNO_2027.md`. Provare prima su copia e conservare backup del database corrente prima del caricamento autorizzato.
