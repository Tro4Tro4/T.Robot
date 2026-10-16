# Primo prompt da incollare in Codex

Stiamo iniziando lo sviluppo di **T.Robot**, un bot WhatsApp per organizzare Capodanno 2027, con mini sito ASP.NET Core MVC riservato agli organizzatori. Il repository contiene `AGENTS.md` e `docs/PROJECT_CONTEXT.md`: leggili integralmente prima di procedere.

Voglio lavorare per milestone piccole, con istruzioni e spiegazioni in italiano. **Milestone 1: crea la soluzione .NET e il database SQLite**.

1. Controlla prima i file presenti e `dotnet --info`; non sovrascrivere lavoro esistente.
2. Se il repository è vuoto, crea la soluzione `TRobot.sln` (oppure `.slnx` se il tooling lo richiede) e i progetti `TRobot.Web` (MVC), `TRobot.Domain`, `TRobot.Application`, `TRobot.Infrastructure`, `TRobot.Tests` (xUnit), con riferimenti coerenti con il contesto. Usa .NET 10 LTS se disponibile, altrimenti chiedimi prima di cambiare target.
3. Configura EF Core SQLite e le entità minime Event, Person, EventParticipant, EventOrganizer con configurazioni EF, vincoli e indici essenziali. Mantieni il modello estendibile a famiglie e accompagnatori senza WhatsApp.
4. Aggiungi `DbContext`, connection string di sviluppo per `trobot.db`, migrazione iniziale, istruzioni per creare/aggiornare il DB e test di base.
5. Esegui build e test; segnala precisamente cosa è stato verificato e cosa no.
6. NON implementare ancora gateway WhatsApp, AI, tutti i moduli o autenticazione fittizia esposta su rete. Per ora il sito è locale e non pubblicato.

Prima di modificare file, dammi un piano sintetico; poi procedi. Alla fine mostrami come avviare l'app in locale e proponi la Milestone 2 (`!presenza si` simulato -> database -> dashboard).
