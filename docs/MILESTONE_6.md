# Milestone 6 — Eventi e partecipanti oltre la demo

## Disegno

Estensione circoscritta dei servizi, delle entità e dei moduli MVC esistenti, seguendo la skill brainstorming e la modalità autonoma richiesta dall'utente. L'obiettivo è creare un evento e registrare persone dal sito per poi usare il simulatore delle presenze, senza interventi manuali nel database.

L'amministratore globale crea l'evento con nome, data/ora locale e fuso; riceve un'assegnazione attiva nella stessa transazione. Gli organizzatori assegnati possono modificare questi dati e aggiungere singole persone. Nessun privilegio globale implicito per la lettura di un evento non assegnato.

Si estendono i flussi esistenti anziché introdurre un'anagrafica globale: questa richiederebbe regole aggiuntive di visibilità, riutilizzo e aggiornamento di persone condivise. Ogni registrazione crea quindi una persona distinta, senza account. Famiglie, accompagnatori, eliminazioni e modifica delle identità condivise restano fuori da questo incremento.

Application valida dati e permessi nella transazione. Infrastructure persiste evento, persone, assegnazioni e audit con EF SQLite. Domain valida le proprie invarianti e mantiene la revisione dell'evento. Web gestisce cookie, anti-CSRF, binding dei soli input e messaggi di errore.

La data locale viene convertita usando il fuso dell'evento; orari ambigui o inesistenti vengono rifiutati. La revisione protegge da sovrascritture provenienti da pagine obsolete. Il reinvio dello stesso modulo partecipante riusa l'identificativo operazione e non aggiunge altre persone; dati differenti con lo stesso ID sono un conflitto.

I nuovi partecipanti partono da Senza risposta. Il simulatore è facoltativo e usa un identificativo sintetico generato dal servizio. Non si raccolgono numeri telefonici e non si collega un gateway.

## Verifiche previste

- Flusso HTTP con cookie e anti-CSRF: creazione, modifica, aggiunta partecipante, simulazione e aggiornamento conteggi.
- Privilegi globali separati dalle assegnazioni per evento; blocco di account disabilitati e assegnazioni revocate; campi contraffatti senza effetto.
- Conversione inverno/estate/UTC, errori al cambio dell'ora, dati e identificativi non validi.
- Revisioni obsolete rifiutate; salvataggi invariati senza ulteriore audit.
- Partecipanti senza simulatore, omonimi distinti e reinvii idempotenti.
- Rollback dopo scrittura SQL per evento, modifica e persona; successivo tentativo valido.
- Upgrade dagli schemi precedenti con dati e ricevute conservati; revisione iniziale zero.
- Build, suite completa, controllo allineamento modello e smoke test locale.

## Esiti del 16 ottobre 2026

- `dotnet build TRobot.sln --no-restore`: 0 errori, 0 avvisi.
- `dotnet test TRobot.sln --no-build --no-restore`: **116 test superati**, 0 falliti, 0 ignorati. I test HTTP verificano anche la sequenza completa evento → partecipante → presenza confermata, con cookie e anti-CSRF reali e database isolato.
- Generata e applicata `20261016124840_EventManagement`. Controllo `has-pending-model-changes` senza differenze.
- Copia preventiva in `.local/trobot-before-event-management.db`. Confrontate tutte le colonne precedenti di tutte le tabelle applicative: righe identiche prima/dopo. `integrity_check` OK, `foreign_key_check` vuoto, revisione iniziale zero e nuovo audit vuoto.
- Smoke test sull'app avviata: anonimo reindirizzato, login Identity, presenza precedente conservata, elenco eventi, scheda evento con dati/partecipanti, modulo creazione e logout. Le nuove scritture sono state provate nei test isolati; lo smoke non ha aggiunto persone o eventi alla demo.
- Listener esclusivamente `127.0.0.1` e `::1`, porta 5238; processo di prova poi arrestato. Nessun reset delle credenziali, nessun deploy e nessun invio esterno.

Dettagli operativi in README, scelte e limiti in `docs/DECISIONS.md`. Prossimo incremento proposto: correzione delle anagrafiche con regole per identità condivise e storico.
