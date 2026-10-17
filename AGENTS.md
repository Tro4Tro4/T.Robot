# Istruzioni per Codex — T.Robot

Leggi `docs/PROJECT_CONTEXT.md` prima di progettare o modificare funzionalità. Il nome pubblico definitivo è **T.Robot** (non CapoDannoBot, tRobot o T-Robot). Usa `TRobot` per namespace e nomi dei progetti C#, `TRobot.sln` per la soluzione, `T.Robot` per il repository e `trobot.db` per SQLite. L'utente è esperto in C#/.NET, EF Core e MVC; preferisce iterazioni piccole, verificabili, guidate in italiano.

## Regole di lavoro
- Per tutti i prossimi sviluppi usa obbligatoriamente la skill `brainstorming` prima dell'implementazione: chiarisci obiettivo e vincoli e presenta un disegno proporzionato al lavoro.
- Modalità autonoma richiesta dall'utente: scegli i default ragionevoli, documentali in `docs/DECISIONS.md` e prosegui con implementazione e verifiche. Chiedi conferma solo quando un dubbio concreto impedisce di decidere; non introdurre approvazioni intermedie obbligatorie per disegno o piano. Questa preferenza prevale sui passaggi di approvazione della skill `brainstorming`.
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
L'utente ha autorizzato il completamento di tutti i moduli del prototipo locale secondo `docs/ROADMAP_LOCAL.md`, con un agente sviluppatore dedicato e un revisore diverso per ogni task. Questa richiesta supera il precedente limite ai soli moduli già implementati: sono autorizzati attività, nuclei, menu, outbox simulata/configurazione, sondaggi, contabilità locale e giochi. Restano esclusi gateway e servizi reali, AI esterna, trasferimenti di denaro e deploy. Conservare identità, storico, reinvii e regole Identity/accessi; non cancellare riferimenti storici. Sito solo Development e loopback; credenziali in `.local`, mai versionate. Migrazioni e suite integrate sequenzialmente dal coordinatore. Vedere roadmap e decisioni per stato e verifiche.

Incrementi 20–22 autorizzati il 17 ottobre: impegni menu/attività dal simulatore, sondaggi/quiz con riferimenti brevi e scheduler automatico esclusivamente locale. Lo scheduler richiede attivazione esplicita per evento, resta vincolato all'accesso dell'attivatore e non deve essere attivato implicitamente sui dati esistenti. Non installare servizi di sistema o trasporti reali. Stato e limiti in `docs/MILESTONE_22.md`.

Eccezione esplicita successiva, milestone 23 del 17 ottobre: l'utente richiede l'integrazione WhatsApp e conferma numero dedicato e gruppo di prova. È autorizzato il gateway sperimentale Baileys esclusivamente locale, con pairing QR dalla pagina amministrativa, un solo gruppo selezionato, associazioni esplicite e primo comando reale `!presenza`. Invii operativi reali solo da nuova bozza con anteprima e conferma; scheduler e vecchie code restano simulati. Nessun deploy o servizio di sistema. Disegno in `docs/MILESTONE_23.md`, istruzioni in `docs/WHATSAPP_LOCAL.md`. Pairing e prova con telefono non vanno dichiarati riusciti senza verifica effettiva.

Orchestrazione aggiornata su istruzione dell'utente: consultare solo i file pertinenti, usare di norma 2–3 subagenti e il modello selezionabile meno costoso sufficientemente affidabile, con ragionamento proporzionato al rischio. Presentare una tabella breve per i lavori non banali con task, complessità, nomi reali dei modelli e dipendenze. Modifiche significative: implementatore → build/test → revisore diverso in sola lettura → APPROVED; per modifiche banali senza rischio concreto bastano intervento diretto e verifica automatica. Non duplicare analisi o documentazione; non sovrapporre scritture sugli stessi file. Dopo due cicli di correzione falliti rivalutare la strategia. Non inventare costi, token o modelli effettivamente utilizzati. Questa regola aggiorna il precedente obbligo di delega/review separata per ogni modifica, conservandolo per il codice significativo.

Milestone 24 autorizzata dopo la conferma dell'invio manuale reale: `!aiuto`, `!menu [pagina]` e `!todo [pagina]` nel gruppo ammesso, per soli mittenti associati. Elenchi con titoli liberi approvati dall'organizzatore prima della condivisione, fingerprint di tutte le pagine legato a gruppo/revisione; cambi dei contenuti richiedono nuova approvazione. Risposte con journal persistente e ricevute senza testo chat; nessuna attivazione degli altri comandi simulati. Vedere `docs/MILESTONE_24.md`.

Milestone 25: richiesta di continuare gli incrementi seguendo stabilmente la nuova orchestrazione. Impegni reali `!porto`, `!prendo`, `!fatto` con proposta e `!conferma` personale entro cinque minuti, nessuna mutazione prima della conferma e nessun incarico altrui sostituibile. Riferimenti stabili negli elenchi approvati; massimo tre proposte attive per persona/evento. Disegno in `docs/MILESTONE_25.md`; restano esclusi altri moduli reali e invii di prova automatici.
