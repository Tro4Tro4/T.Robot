# Milestone 19 — Comandi del simulatore e rifinitura UI

## Obiettivo e perimetro

Richiesta del 17 ottobre 2026: collaudare il prototipo, correggere i problemi rilevati, consolidare con commit e ampliare i comandi nel simulatore. L'utente richiede inoltre Impeccable per migliorare la UI. Sito solo organizzatori, Development e loopback; nessun gateway, AI esterna, deploy o trasferimento reale.

## Comandi locali

Il nuovo `SimulationCommandService` in Application instrada il modulo esistente della dashboard:

| Comando | Esito |
| --- | --- |
| `!presenza si\|forse\|no` | Regola e ricevute presenze esistenti |
| `!menu` | Conteggi delle voci attive, pronte, assegnate e da organizzare |
| `!todo` | Conteggi delle attività da iniziare, in corso e completate |
| `!vota <GUID sondaggio> <numero opzione>` | Voto della persona selezionata, tramite PollService |
| `!aiuto` | Sintassi supportate |

Il voto richiede la nuova casella di conferma esplicita nel simulatore, non preselezionata. La scheda sondaggio mostra il GUID nel comando da copiare. Il voto è possibile solo per adulti confermati, dopo pubblicazione simulata Sent e prima della scadenza/chiusura. Un nuovo messaggio può cambiare il voto; un reinvio identico non ripristina voti precedenti.

Attore, assegnazione evento, account e mittente vengono verificati anche sui reinvii. L'identificativo della ricevuta voto deriva deterministicamente da evento e ID messaggio; payload diversi con lo stesso ID sono conflitti. Si controllano anche collisioni con le ricevute presenze esistenti. Un gate condiviso serializza le scritture del router nel singolo processo locale: questa garanzia non è un protocollo distribuito né un vincolo comune del database. Un futuro gateway richiederà un disegno dedicato. Nessuna migrazione nuova.

Le risposte `!menu` e `!todo` sono volutamente aggregate: niente nomi, titoli liberi, note, saldi o messaggi di chat. I dettagli rimangono nei moduli riservati. Nessun risultato viene accodato o inviato realmente.

## UI e debug

Rifinitura circoscritta del sistema MVC/Bootstrap esistente secondo Impeccable ufficiale, installata in `.local/skills/impeccable` (versione 4.6.0). Modalità Operate: navigazione, gerarchia tipografica, focus, contrasto, spaziatura dei form, tabelle e riepiloghi responsive. Nessuna dipendenza frontend o fonte esterna a runtime.

La configurazione VS Code avvia ora il progetto C# in Debug su localhost:5238, con task di compilazione preliminare e ambiente Development.

## Verifiche

- Baseline del prototipo: 296/296 test superati il 17 ottobre; smoke HTTP autenticato in sola lettura di dashboard, eventi e tutti i moduli, CSS e logout riuscito.
- Review indipendenti positive dei moduli 11–18, del nuovo router e della UI. Un rilievo di accessibilità sulle etichette delle regioni tabella è stato corretto con nomi contestuali distinti. Test mirati del router: 14/14 superati in Release.
- `dotnet build TRobot.sln --no-restore`: build Debug senza errori o avvisi, ripetuta dopo l'ultimo aggiustamento Razor.
- `dotnet test TRobot.sln --no-build --no-restore`: 310/310 test superati, nessuno ignorato. Report `.local/test-results/milestone19-final.trx`.
- Chromium headless su copia consistente SQLite: 10 flussi principali riusciti — login, evento e tre partecipanti, presenze, nucleo, attività, menu con decimali, sondaggio con pubblicazione/voto/chiusura/risultato, spesa approvata 100 EUR con quote 50/50 e rimborso 50 EUR che azzera i saldi, quiz volontario con un punto e storico/logout.
- Secondo collaudo browser: comandi consultivi senza nomi o titoli, voto con conferma, reinvio senza duplicato anche dopo chiusura, conflitto a payload diverso, rifiuto senza conferma; presenza e reinvio senza doppio conteggio. Trenta combinazioni di dieci pagine a 1440/768/390 px senza overflow orizzontale della pagina, navigazione attiva coerente, regioni contabili distinte e focus skip link visibile da tastiera (outline 3 px). Nessun errore JavaScript o HTTP 500 nei due collaudi. Screenshot esaminati di dashboard desktop/mobile, contabilità desktop/mobile e login mobile.
- Detector Impeccable sui cinque componenti condivisi e CSS: nessuna segnalazione (`.local/ui/impeccable-detect.json`). La verifica resta limitata al prototipo e a Chromium emulato: non equivale a collaudo su dispositivi fisici o certificazione di accessibilità.
- EF CLI 10.0.6 ripristinata dal manifest; `dotnet ef migrations has-pending-model-changes --project src/TRobot.Infrastructure --no-build`: nessuna variazione pendente. Nessuna migrazione aggiunta.
- Baseline di tutte le 32 tabelle del database originale e hash del file credenziali invariati dopo i collaudi. Originale e copia con `integrity_check = ok` e nessuna violazione FK. Database di collaudo e dati sintetici rimangono esclusi da Git.

Gli artefatti con screenshot, log e database di collaudo rimangono in `.local`, esclusa da Git. Le credenziali non vengono stampate né modificate.
