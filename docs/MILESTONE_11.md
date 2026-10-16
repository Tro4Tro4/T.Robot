# Milestone 11 — Attività organizzative per evento

Il prototipo locale consente agli organizzatori abilitati e assegnati all'evento di creare, consultare e modificare attività: titolo obbligatorio (160 caratteri), descrizione facoltativa (2000 caratteri, righe normalizzate), priorità bassa/normale/alta, stato da iniziare/in corso/completata/annullata, responsabile facoltativo e scadenza facoltativa.

Il responsabile appartiene alle persone dell'evento (partecipanti oppure organizzatori attivi). La scadenza è inserita e mostrata nel fuso dell'evento, conservata UTC; ore inesistenti e ambigue al cambio dell'ora vengono respinte. Le attività nascono da iniziare; l'ingresso nello stato annullata richiede conferma esplicita. È possibile riprendere o riaprire un'attività, senza cancellare alcun record. Una modifica identica non crea revisioni o audit aggiuntivi. Le modifiche di nome persona non alterano gli identificativi delle assegnazioni.

Il modulo di creazione genera un identificativo stabile di operazione: il reinvio con dati identici restituisce la stessa attività senza duplicati. Il riuso dello stesso identificativo con dati diversi o dopo successive modifiche produce conflitto.

## Disegno e integrazione

- `OrganizationTask` mantiene regole e setter privati nel Domain.
- `TaskService` usa `ILocalStore` e `LocalOperations` per autorizzazione rivalidata, controlli responsabile, conversione fuso, revisione e transazione comprendente audit.
- `TaskConfiguration` configura tabella, limiti, enum, concorrenza e foreign key Restrict.
- `TasksController` usa i claim Identity del cookie, mai un actorId del form; POST con anti-CSRF e revisione. Testo visualizzato con codifica Razor ordinaria.
- Rotte `/Tasks?eventId=...`, `/Tasks/Create?eventId=...`, `/Tasks/Edit?eventId=...&id=...`, POST `/Tasks/Save`.
- Registrazione DI richiesta: `services.AddScoped<TRobot.Application.Tasks.TaskService>();`. La configurazione EF viene scoperta da `ApplyConfigurationsFromAssembly`; migrazione e navigazione sono integrate dal coordinatore.

## Verifica

`TaskTests` copre normalizzazione, enum, atomicità della validazione, revisione e no-op, annullamento confermato, riferimenti di evento, revoche, scadenze DST/UTC, autenticazione HTTP, CSRF, codifica HTML, conflitto su pagina obsoleta e rollback di modifica/audit in caso di errore. Build, migrazione e suite vengono eseguite dal coordinatore dopo l'integrazione, evitando processi concorrenti sul repository.

Nessun invio o promemoria automatico è attivato in questa milestone. Il prototipo resta Development e loopback, senza servizi reali o deploy.
# Verifica coordinata

Review indipendente conclusa senza rilievi aperti dopo la correzione dei reinvii di creazione. Build con 0 errori/avvisi e **195 test superati** (12 nuovi casi attività). Migrazione `OrganizationTasks` generata dal coordinatore, con audit comune e tabella attività.
