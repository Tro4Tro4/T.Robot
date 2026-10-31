# Adattamento mobile del portale

Richiesta del 23 ottobre 2026, con autorizzazione esplicita successiva al rilascio Azure. Impeccable 4.6.0, percorso adapt in modalita Operate, e brainstorming proporzionato al portale esistente. Conservata l'identita visiva gia rilasciata.

## Scelte autonome

- Sotto 768 px, Account e navigazione evento diventano menu espandibili nativi. La sezione corrente resta visibile; Escape richiude il menu e riporta il focus al suo comando. Toccare fuori dal pannello richiude il menu. Sopra il breakpoint la navigazione estesa torna visibile.
- Senza JavaScript, i menu sono aperti nel markup e restano utilizzabili nativamente. Nessuna funzione operativa dipende dall'esecuzione dello script.
- Con un solo evento assegnato, il nome sostituisce il selettore ridondante; con piu eventi il selettore GET rimane disponibile.
- Il simulatore e raccolto in una sezione dedicata, con istruzione circoscritta al simulatore e apertura automatica in presenza di errori di validazione. Nessuna attivazione o modifica dei trasporti.
- Partecipanti e nuclei usano una sola tabella: su mobile, con JavaScript, le righe diventano schede con etichette; ruoli e intestazioni tabellari restano disponibili alle tecnologie assistive. I controlli vengono spostati nel DOM senza duplicarli. Senza script restano tabelle scorrevoli.
- Ricerca partecipanti per nome o presenza, insensibile a maiuscole e accenti, con conteggio dei risultati, stato vuoto e ripristino dell'elenco completo. Filtro solo nel browser: nessuna richiesta di rete, memorizzazione o scrittura dei dati.
- Tabelle dense ancora scorrevoli, prima colonna ferma su mobile, eliminata la larghezza minima globale che allargava anche le tabelle brevi. Controlli touch da almeno 44 px, campi da 16 px, gestione delle aree sicure e zoom lasciato disponibile.

## File

Layout e navigazione condivisi, Home/Index, Events/Details, Households/Index, `wwwroot/css/site.css`, nuovo `wwwroot/js/mobile-portal.js` e test `PortalUsabilityTests`. Nessuna modifica a Domain, Application, controller, persistenza, autorizzazioni, conferme, operazioni o revisioni. Nessuna migrazione.

## Verifiche locali effettive

- Build senza errori o avvisi; suite completa **624/624 test .NET**, inclusi i due nuovi test su selettore eventi e apertura degli errori del simulatore; 23 test mirati superati.
- Chromium con viewport 320x568, 360x800, 390x844, 430x932, 667x375, 768x1024, 1024x768, 1440x1000: **80 controlli** su dieci pagine, senza overflow della pagina, errori JavaScript o risposte HTTP di errore.
- Verificati tap sintetici, cambio sezione, ricerca e ripristino, stati vuoti, Escape, focus, ridimensionamento tra mobile e desktop, salto al contenuto e tre percorsi senza JavaScript.
- Dati esclusivamente sintetici su SQLite temporaneo locale: 36 partecipanti e un nucleo popolato; gateway e scheduler disabilitati. Catture e report in `.local/ui/mobile-refinement/`.
- A 390 px, conteggi da circa 334 px e azione principale da circa 523 px: entrambi disponibili nella prima schermata da 844 px. La prova non equivale a una misura universale per nomi o testi di lunghezza diversa.
- Detector Impeccable sui sette file UI modificati: nessuna segnalazione (`[]`). Revisione indipendente gpt-6.1-sol medium: **APPROVED**, compresa la regola esplicita che esclude dal layout i pannelli chiusi. Review del diff testuale trasmesso dal coordinatore per indisponibilita degli strumenti degli agenti; verifiche eseguite dal coordinatore.

Il collaudo usa Chromium e touch emulato; non sono stati verificati dispositivi fisici, tastiera iOS o Safari. Nessuna certificazione di accessibilita dichiarata. Lo stile `:has()` migliora il contenitore delle schede; in sua assenza resta lo scorrimento Bootstrap.

## Rilascio

Rilascio completato su Azure il 23 ottobre 2026 nella release `mobile-20261023-arm64`, precedente `ui-20261023-arm64` conservata per rollback dei soli binari. Publish ARM64 Release senza errori o avvisi; archivio da 8.979.564 byte e 223 file, senza database o secret. SHA-256: `4b3f73b9ee38b343e4b40ec98aa9631877532becb9fe32bbbdebb09253eee4f2`.

Procedura gia collaudata riusata con soli percorsi e verifiche degli asset adattati al nuovo pacchetto, delta revisionato indipendentemente **APPROVED**. Nuova directory, checksum verificato sulla VM, backup privato dopo arresto coordinato e confronto di conteggi/hash di tutte le righe di ogni tabella dopo l'avvio: tutti identici. Integrita/FK OK e scenario eliminato ancora assente. Nessuna migrazione, sostituzione del DB, modifica alla configurazione Azure o ripristino di dati obsoleti. Backup conservato esclusivamente sulla VM.

Configurazione Web/gateway, chiavi Data Protection e identita WhatsApp conservate. Recovery riuscito sul gruppo originale; Web, Caddy, gateway e timer attivi, `NRestarts=0` per Web e gateway. Circa 475 MiB disponibili e swap inutilizzata al controllo finale. Nessun pairing o messaggio di prova; Oracle e PC restano disabilitati.

Collaudo pubblico con TLS verificato e senza override: stessa sessione autenticata antecedente al rilascio ancora valida e logout finale riuscito. Dieci pagine private HTTP 200 con `no-store`, CSS e JavaScript serviti identici ai sorgenti. Otto richieste GET/POST ai quattro moduli sospesi restano 404 e cinque POST Alloggi senza antiforgery restano 400, senza mutazioni applicative.

**40/40 controlli browser pubblici** su dieci pagine a 320x568, 390x844, 667x375 e 1440x1000, piu tre percorsi senza JavaScript: menu, focus, schede, ricerca/zero risultati/ripristino e navigazione corrente corretti; nessun overflow della pagina, errore JavaScript o risposta HTTP di errore. A 390 px conteggi e azione principale confermati nella prima schermata anche con l'evento reale.

Report e strumenti privati in `.local/azure/mobile-release-material.json`, `mobile-release.py`, `mobile-release-smoke-report.json`, `smoke-mobile-release.py`; contesto remoto con baseline e percorso backup in `/root/trobot-provisioning/mobile-20261023-context.json`. Screenshot privati in `.local/ui/azure-mobile/`.

Comandi principali: `dotnet build TRobot.sln --no-restore --nologo`, `dotnet test TRobot.sln --no-build --no-restore --nologo`, `dotnet publish src/TRobot.Web/TRobot.Web.csproj -c Release -r linux-arm64 --self-contained false -p:UseAppHost=false`. Attivazione e collaudo effettivi a cura del coordinatore; review del delta testuale, senza accesso autonomo del revisore alla VM.
