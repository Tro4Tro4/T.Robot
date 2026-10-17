# Milestone 24 — Aiuto, menu e attività nel gruppo reale

## Obiettivo e disegno

L'utente autorizza il prossimo incremento dopo avere verificato presenze di due mittenti indipendenti e un invio dal sito arrivato una sola volta nel gruppo corretto. Si aggiungono esclusivamente `!aiuto`, `!menu [pagina]`, `!todo [pagina]`, conservando tutte le protezioni della milestone 23. Incremento circoscritto nel gateway esistente, disegno brainstorming proporzionato e prosecuzione autonoma secondo AGENTS.

Solo nuovi messaggi testuali nel gruppo attivato, da identità già associate, con attivatore attualmente autorizzato. Stessi limiti di età, dimensione, frequenza, coda, epoch e isolamento. Presenza continua a usare reazione ✅; i tre comandi informativi rispondono con un testo breve nel medesimo gruppo. Nessun DM, altro comando reale o collegamento della vecchia outbox allo scheduler.

## Contenuti pubblicabili

Aiuto statico con i soli comandi realmente attivi, senza riferimenti ai comandi futuri del simulatore. Menu/attività mostrano conteggi, titoli, quantità/unità/stato e pagine di cinque elementi, senza responsabili, note, saldi, nomi di persone o dettagli individuali estratti dalle anagrafiche. Si riusa la formattazione deterministica se praticabile, senza dipendere dalle identità sintetiche del simulatore.

I titoli sono testo libero: la pubblicazione richiede anteprima e conferma esplicita dell'organizzatore nella pagina WhatsApp. Il servizio calcola fingerprint dell'esatto contenuto di tutte le pagine, insieme a gruppo e revisione del collegamento. La conferma verifica di nuovo il fingerprint attuale e registra un'approvazione dedicata; nessuna approvazione implicita sui dati esistenti. Il nome del gruppo è risolto per ID esatto e mostrato nell'anteprima. Cambi ai contenuti pubblicabili o al collegamento rendono l'approvazione non valida. Prima dell'approvazione o dopo una modifica, il comando risponde con un breve avviso generico, senza titoli. Approvare non invia messaggi.

Nuove entità additive: approvazioni degli elenchi (senza corpo chat) e ricevute persistenti dei comandi informativi. Nessuna modifica a ricevute storiche, persone o code precedenti. Autorizzazione, ricevuta e audit atomici; il testo ricevuto non viene salvato. Stesso ID messaggio riutilizzato per comando/payload/persona/attore diversi è conflitto, anche fra presenza e nuovi comandi. Reinvio identico non pubblica di nuovo.

## Contratto callback e invio risposta

Risposta callback estesa in modo compatibile: `{ handled, duplicate, reaction, replyText, replyOperationId }`. Per presenza nuovi campi null; per risposta testuale `reaction=null`, `replyText` massimo 2000 caratteri e `replyOperationId` GUID persistente della ricevuta. Risposte valide solo se handled e non duplicate. Nessun campo groupId/destinatario viene accettato dalla callback: il gateway usa sempre il gruppo della richiesta originale e la whitelist attuale.

Il gateway verifica il tipo e la lunghezza dei campi, poi invia tramite lo stesso journal persistente degli invii manuali, con ID operazione della ricevuta. Fencing socket/gruppo/pin prima dell'invio; se la connessione cambia durante l'attesa del journal il messaggio non parte. Duplicati, errori callback, esiti incerti e riavvii non causano replay automatici. Reazioni e risposte testuali sono mutuamente esclusive. Limite risposta HTTP callback aumentato a 16 KiB per il testo UTF-8; nessun ampliamento del body messaggi in ingresso. Il trasporto invia testo con link preview disabilitata e senza quoted payload contenenti chat privata.

## Coordinamento e verifiche

Sviluppatore .NET dedicato a servizi/entità/UI/test, sviluppatore Node separato per router/callback/reply/test; revisore indipendente diverso da entrambi. Coordinatore integra la migrazione sequenzialmente, prima su copia consistente; build e suite complete, test Node e smoke senza account reale prima del riavvio. Sessione autenticata preservata; nessun invio di prova reale dal coordinatore. La verifica finale nel gruppo richiederà nuovi comandi dell'utente.

## Esito tecnico

Durante lo sviluppo l'utente ha aggiornato l'orchestrazione per ridurre contesto e numero di agenti. Dopo il limite di utilizzo incontrato dai primi implementatori, il completamento è passato a un unico `gpt-6.1-sol` con ragionamento medium e la review a un diverso `gpt-6.1-sol` con ragionamento high. Review finale **APPROVED**, senza rilievi materiali; nessuna stima non misurata di token o costo.

- Build senza errori o avvisi; **421/421 test .NET** e **28/28 Node** superati. I test .NET WhatsApp mirati sono 41, di cui dieci nuovi; Node aggiunge undici casi, inclusi otto casi di cambio gruppo/riconnessione durante callback, attesa invio e operazioni del journal.
- Migrazione `AddWhatsAppInformationCommands` additiva, due tabelle. Applicata prima sulla copia e poi sull'originale; conservate le 35 tabelle precedenti e le credenziali, integrità e chiavi esterne OK. Modello EF senza differenze pendenti.
- Sessione WhatsApp conservata e riconnessa, stesso gruppo già autorizzato ripristinato. Nessuna approvazione dei contenuti esistenti e nessun messaggio di prova reale inviato dal coordinatore.
- Collaudo browser sul sito reale riuscito: collegamenti menu/attività, nome e ID esatti del gruppo, fingerprint/revisione, token CSRF e conferma obbligatoria, stato non approvato, desktop/mobile senza overflow. Nessun errore JavaScript e conteggi di approvazioni/ricevute/messaggi invariati. Report `.local/ui/m24-report.json`; screenshot locali, senza dati personali versionati.
- Comandi per riprodurre i controlli: `dotnet test TRobot.sln --no-build --no-restore`, `node --test gateway/TRobot.WhatsApp/test/*.test.js`; risultati completi in `.local/test-results/milestone24-final.trx` e report locale del collaudo UI.

Restano da provare sul telefono `!aiuto` e, dopo l'approvazione dal sito, `!menu`/`!todo`. Gli altri comandi sono ancora esclusivamente simulati. Procedura operativa in [WHATSAPP_LOCAL.md](WHATSAPP_LOCAL.md).
