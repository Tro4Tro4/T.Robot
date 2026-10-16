# Milestone 5 — Assegnazioni e revoca degli accessi

## Disegno e decisioni

L'amministratore globale può assegnare un account esistente a un evento, revocarne l'assegnazione, disabilitare e riattivare un account normale. Queste funzioni estendono la pagina Organizzatori senza introdurre altri moduli. Domain mantiene le assegnazioni, Application autorizza/valida, Infrastructure salva stato e audit atomicamente, Web gestisce i moduli protetti da anti-CSRF.

Le assegnazioni sono disattivate con `IsActive`, senza cancellazioni: le ricevute pregresse continuano a riferirsi all'organizzatore originale. Riabilitare un'assegnazione riusa la stessa riga e il ruolo già presente. Una nuova assegnazione usa il ruolo Organizer, senza promozioni dal modulo. I controlli delle presenze rileggono le assegnazioni attive nel DB; una revoca vale dalla richiesta successiva e non interrompe l'accesso agli altri eventi.

Gli account hanno `IsEnabled`, inizialmente true anche dopo la migrazione. La disabilitazione blocca il login, invalida i cookie tramite security stamp e non cancella le assegnazioni. Riattivare l'account mantiene le assegnazioni ancora attive, ma non rende nuovamente validi i vecchi cookie. Non si possono assegnare eventi a un account disabilitato; si possono revocare sue assegnazioni. Nessun amministratore globale può essere disabilitato tramite Web, per preservare l'accesso amministrativo iniziale.

Revoche e disabilitazioni richiedono una casella di conferma esplicita, controllata anche da Application. ActorId proviene dal cookie, mai dal modulo. Ripetere una configurazione già presente è un no-op senza duplicare l'audit. Identificativi non validi o inesistenti e chiamanti non amministratori non modificano dati.

L'audit aggiunge EventId facoltativo e azioni EventGranted/EventRevoked/AccountEnabled/AccountDisabled. Assegnazione o stato account, eventuale security stamp e audit sono salvati nella stessa transazione. L'audit non contiene credenziali. Un evento può temporaneamente non avere organizzatori attivi: l'amministratore globale conserva la gestione delle assegnazioni e può ripristinarne l'accesso.

## Piano autonomo

1. Introdurre flag e audit per evento; estendere query, servizi e controlli di login.
2. Aggiungere i moduli di gestione, con conferme esplicite e messaggi di esito.
3. Generare la migrazione incrementale e verificarne l'upgrade con account/storico esistenti.
4. Testare isolamento degli eventi, revoca immediata con cookie esistente, account disabilitato/riattivato, protezione amministratore, idempotenza e rollback.
5. Eseguire build/test, applicare la migrazione e verificare la Web locale senza revocare l'account reale della demo.

Restano esclusi eliminazione account, promozioni globali, reset password, MFA, deploy e integrazioni WhatsApp.

## Verifiche del 16 ottobre 2026

Build con 0 errori e 0 avvisi, 98 test passati. Migrazione `OrganizerAccessLifecycle` applicata e ripetuta senza modifiche, modello EF allineato. Test di upgrade con account abilitati, ruoli/assegnazioni e audit/ricevute precedenti conservati. Sul database fisico: conteggi uguali alla copia pre-migrazione, riferimenti validi e integrità SQLite OK. Il provider segnala il rebuild dei vincoli audit con PRAGMA fuori transazione; copia pre-migrazione disponibile solo in `.local/trobot-before-access-lifecycle.db`.

Smoke test sull'app avviata: login Identity, presenza dimostrativa ancora confermata, moduli di revoca con conferma esplicita, amministratore protetto e logout. Listener verificati solo su loopback; processo poi arrestato. Nessuna revoca o disabilitazione eseguita sull'account reale della demo.
