# Portale organizzatori - grafica e usabilita

Richiesta del 23 ottobre 2026: rendere il portale bello, semplice, intuitivo, completo e armonico usando Impeccable.

## Disegno e perimetro

Raffinamento del portale MVC/Bootstrap esistente in modalita Operate con Impeccable locale 4.6.0 e brainstorming. Conservati identita blu, caratteri di sistema e risorse locali. Nessuna dipendenza frontend aggiuntiva. Per gli organizzatori la priorita e trovare l'azione corretta, capire lo stato dei dati e completare i moduli anche da telefono.

- Componenti condivisi: colori, spaziature, gerarchia, stati dei controlli, focus tastiera e navigazione per gruppi di compiti. Soggiorni raggiungibile dalla navigazione evento.
- Dashboard: presenze prima dei riepiloghi organizzativi, azioni dirette verso partecipanti, soggiorni e alloggi. I moduli sospesi rimangono nella navigazione ma non appaiono come lavoro da completare.
- Aiuto: guida contestuale espandibile in ogni modulo evento, con passaggi e collegamenti alle funzioni pertinenti.
- Evento: partecipanti e aggiunta raggiungibili tramite collegamenti interni; dati dell'evento in sezione espandibile, aperta in presenza di errori di validazione.
- Alloggi: elenco compatto dei nuclei con persone da assegnare, moduli espandibili, collegamenti a camere/assegnazioni/conferma e istruzioni per il piano definitivo.
- Login ed elenchi vuoti: istruzioni per iniziare o recuperare l'accesso. Tabelle soggiorni scorrevoli e raggiungibili da tastiera.

Le sezioni espandibili sono HTML nativo: non richiedono JavaScript. Le conferme, gli identificativi operazione, le revisioni, i permessi e i servizi applicativi esistenti restano in uso.

## Verifiche

Build e suite .NET; test HTTP dedicati ai percorsi principali, alla guida e alla coerenza dei moduli sospesi. Collaudo Chromium con dati sintetici su database in memoria e gateway/scheduler disabilitati, dimensioni desktop, intermedie e mobile. Detector Impeccable sui file modificati e revisione indipendente finale.

Risultati verificati:

- `dotnet build TRobot.sln --no-restore --nologo`: zero errori e avvisi.
- Test mirati PortalUsabilityTests, PortalModuleTests, LodgingTests e WebFlowTests: 34/34 superati.
- Chromium headless: 40/40 percorsi/dimensioni su dieci pagine a 320, 390, 768 e 1440 px; nessun overflow della pagina o errore JavaScript. Verificati navigazione corrente, guide con tastiera, sezioni espandibili, destinazioni dei collegamenti interni, salto al contenuto e logout.
- `dotnet test TRobot.sln --no-build --no-restore --nologo`: 622/622 superati.
- Dati di collaudo: 36 partecipanti sintetici, incluso un nome lungo, e account di test. I test HTTP usano SQLite in memoria; l'anteprima finale usa un file SQLite temporaneo escluso da Git per consentire connessioni distinte nelle richieste concorrenti. Nessun database operativo usato nell'anteprima.
- Ripetizione browser finale con controllo di tutte le risposte HTTP, incluse quelle asincrone: 40/40 percorsi/dimensioni superati, nessun errore HTTP o JavaScript.
- Detector Impeccable eseguito una volta su undici file UI: nessuna segnalazione (`[]`). Catture e report privati in `.local/ui/refinement/`.
- Revisione indipendente: **APPROVED** sul diff testuale fornito dal coordinatore. Nessun difetto concreto rilevato in navigazione, accessibilita, percorsi o conservazione di conferme/input/revisioni. Per i blocchi dell'ambiente degli agenti il revisore non ha verificato direttamente filesystem, screenshot o esecuzione dei test; le verifiche automatiche e visuali sono state eseguite dal coordinatore.

Il collaudo browser copre Chromium emulato; non costituisce verifica su dispositivi fisici o certificazione di accessibilita. Nessun rilascio, migrazione o modifica al DB operativo inclusi in questo intervento. Azure resta l'unica fonte operativa; il gateway PC non viene avviato.

## Rilascio successivo autorizzato

Il 23 ottobre 2026 l'utente richiede esplicitamente il rilascio Azure. Nuova UI attiva in `ui-20261023-arm64`, backup privato e confronto completo dei dati riusciti, nessuna migrazione. Sessione originale, gateway e gruppo conservati; dieci pagine e 20 controlli browser pubblici desktop/mobile superati. Dettagli in [UI_RELEASE_AZURE.md](UI_RELEASE_AZURE.md).
