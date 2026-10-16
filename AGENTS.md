# Istruzioni per Codex — T.Robot

Leggi `docs/PROJECT_CONTEXT.md` prima di progettare o modificare funzionalità. Il nome pubblico definitivo è **T.Robot** (non CapoDannoBot, tRobot o T-Robot). Usa `TRobot` per namespace e nomi dei progetti C#, `TRobot.sln` per la soluzione, `T.Robot` per il repository e `trobot.db` per SQLite. L'utente è esperto in C#/.NET, EF Core e MVC; preferisce iterazioni piccole, verificabili, guidate in italiano.

## Regole di lavoro
- Prima di intervenire, ispeziona repository, SDK, file e stato Git; non assumere che il codice esista.
- Procedi per milestone; non implementare tutti i moduli contemporaneamente.
- Mantieni Domain indipendente; usa Application per le regole; Infrastructure per persistenza/integrations; Web per UI e API.
- I partecipanti usano solo il **gruppo WhatsApp**; il sito è **solo per organizzatori**. Nessun DM.
- Il gateway WhatsApp è sperimentale e **non ufficiale**: non aggiungerlo né collegarlo in produzione senza richiesta esplicita.
- Tutte le azioni che cambiano dati passano da servizi validati e autorizzati; l'AI non modifica il DB direttamente.
- Preferisci EF Core SQLite inizialmente, migration versionate, `decimal` per importi, UTC per timestamp e timezone evento configurabile.
- Non pubblicare saldi o dati personali individuali nel gruppo. Minimizza retention dei messaggi.
- Non introdurre credenziali, numeri telefonici reali, token o secret nei file versionati.
- Scrivi test per le regole nuove; esegui build e test dopo ogni milestone, segnalando eventuali impedimenti.
- Se devi scegliere un default non ancora approvato, documentalo in `docs/DECISIONS.md`.
- Rispondi in italiano: breve piano iniziale, poi implementazione, riepilogo finale con file, comandi, test e prossimo passo.

## Milestone corrente
Scaffolding soluzione .NET + EF Core SQLite + prime entità Event, Person, EventParticipant, EventOrganizer + migrazione iniziale. **Non** implementare ancora gateway WhatsApp, AI, pagamenti o intero pannello. Obiettivo milestone successiva: simulare `!presenza si` -> persistenza -> dashboard.
