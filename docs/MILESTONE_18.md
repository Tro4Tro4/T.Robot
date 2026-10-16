# Milestone 18 — Dashboard, storico e verifica integrata

Disegno approvato in modalità autonoma: completamento del prototipo locale senza redesign, nuovi moduli o servizi reali. La Home conserva presenze e simulatore e aggiunge riepilogo aggregato, avvisi operativi, countdown nel fuso evento e navigazione per tutti i moduli. La scheda evento usa la stessa navigazione.

LocalDashboardService applica i permessi correnti per evento/account; LocalDashboardStore conta in SQL nuclei/membri, menu, attività, sondaggi, outbox, contabilità e giochi. Il totale approvato usa somma esatta di centesimi interi. Sondaggi e quiz da chiudere richiedono pubblicazione Sent e deadline raggiunta; gli invii in coda rimangono tali. TimeProvider rende riproducibili confini temporali e countdown.

Lo storico esistente include LocalAudit tramite una categoria aggiuntiva e campi facoltativi. UNION ALL, filtro evento, ordinamento e paginazione rimangono in SQL; la UI traduce modulo/azione. Nessun testo libero, identificativo messaggio, contenuto contabile o voto individuale aggiunto allo storico. Nessuna duplicazione di righe o modifica al modello EF.

LocalCompletionTests attraversa servizi e ricevute di tutti i moduli su dati sintetici, verifica privacy dell'outbox, quadratura contabile e punteggi, poi esegue GET autenticati. Altri casi verificano centesimi esatti, confini scadenza, pubblicazioni Sent rispetto a Queued, countdown al cambio dell'ora, paginazione/stabilità dello storico Local, isolamento evento e accessi revocati/disabilitati.

## Stato delle verifiche

Milestone 18 completata e verificata dal coordinatore: build senza errori o avvisi, review indipendente positiva, 13 test mirati LocalCompletionTests/HistoryTests e 296/296 test della suite completa superati. Smoke autenticato sul vero host con copia consistente del database: login, tutte le pagine dei moduli, no-store, CSS e logout verificati. Listener del processo creato solo 127.0.0.1 e ::1 sulla porta 5238; host poi arrestato e nessun listener residuo. Modello EF senza modifiche pendenti; nessuna migrazione aggiunta e aggiornamento ripetuto senza operazioni. Confronto di 13 tabelle precedenti invariato, integrity_check e foreign_key_check OK. Nessuna mutazione business o modifica alle credenziali originali. Git conferma database e .local/credenziali ignorati e non tracciati; diff --check senza errori.

## Consultazione locale

Dopo avvio Development e login locale, GET /?eventId=<evento>, /Events/Details/<evento>, /Households, /Tasks, /Menu, /Messages, /Polls, /Accounting, /Games e /History con eventId. Il filtro /History?eventId=<evento>&category=Local consulta i soli audit dei moduli. I controlli conclusivi sul database esistente sono in sola lettura: nessuna mutazione business o seed durante lo smoke.

Limiti: form simulati solo per organizzatori; nessun gateway, messaggio reale, DM, AI esterna, trasferimento di denaro o deploy. Contabilità EUR con quote dinamiche e sole persone partecipanti. Quiz solo con adesione volontaria; classifica riservata al sito. Il prototipo non implementa tutti i comandi WhatsApp elencati nelle specifiche iniziali.
