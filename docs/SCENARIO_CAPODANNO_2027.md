# Scenario simulato — Capodanno 2027, 20 persone

## Disegno e perimetro

L'utente richiede un'app popolata realisticamente in tutti i moduli per quattro giorni e tre notti con venti persone. Con brainstorming si sceglie un nuovo evento chiaramente identificato come simulazione, creato mediante i servizi applicativi già autorizzati. Nessuna sostituzione di dati preesistenti, telefono reale o collegamento del nuovo evento a WhatsApp. Non occorrono nuove funzioni Web o migrazioni.

Un caricatore CLI locale ripetibile, senza HTTP o servizi esterni, usa Application per tutte le scritture e Infrastructure per lo store. Può leggere tramite EF per identificare l'amministratore e verificare i dati. Non usa SaveChanges, ExecuteSql o scritture dirette per costruire lo scenario. Prima prova su copia SQLite; solo dopo validazione e review caricamento nel database locale corrente. Il caricatore deve richiedere un percorso DB esplicito e `--apply`, senza migrazioni automatiche; il riavvio sullo scenario completato deve essere di sola verifica, senza resettare modifiche dell'utente o duplicare dati. Nessuna nuova credenziale: l'organizzatore è l'amministratore esistente, assegnato tramite il servizio di creazione evento.

## Dati coerenti

- Evento: **SIMULAZIONE — Capodanno al Casale, 20 persone**. Località inventata, Europe/Rome. Arrivo 30/12/2026 ore 15:00, partenza 02/01/2027 ore 11:00. Tre notti, tre colazioni, due pranzi e tre cene: 160 coperti complessivi. Il programma descrive arrivo e sistemazione, pasti, cenone del 31 alle 20:30, brindisi, passeggiata/giochi e pulizie finali.
- Venti anagrafiche inventate e confermate, sedici adulti e quattro bambini: quattro nuclei di due adulti e un bambino, più quattro coppie. Mittenti esclusivamente sintetici, nessun account aggiuntivo. Quattro adulti anticipatari; gli organizzatori non partecipanti non entrano nelle quote.
- Circa 30 voci menu con categorie, quantità/unità, note operative e responsabilità adulte, distribuite sugli otto pasti, bevande e materiali. Stati misti pianificato/assegnato/pronto e almeno alcune voci libere per provare `!porto`; alternative vegetariane generiche, nessun dato sanitario personale.
- Circa 20 attività con responsabili adulti, priorità, scadenze nel fuso evento e stati misti: prenotazione, camere, spesa, cucina, cenone, giochi, riordino e riconsegna. Restano attività libere, in corso e completate per provare i comandi.
- Undici spese approvate, esattamente **3.600 €**: alloggio 1.800; cena 30 dicembre 260; cenone 480; colazioni 156; pranzi 220; cena 1 gennaio 180; analcolici 96; vino/birra 200; spumante 72; materiali 86; giochi 50. Almeno una spesa in attesa (120 €) e una respinta/duplicata (45 €), escluse dai totali. Quattro anticipatari: primo 1.800, secondo 752, terzo 502, quarto 546 €.
- Peso adulti 1 e bambini 0,5: 18 quote equivalenti, **200 € per adulto e 100 € per bambino**, somma 3.600 €. Quattro pagamenti fittizi da 200 € confermati da debitori adulti verso anticipatari (800 € in totale), due dichiarazioni da 100 € ancora da confermare e una annullata da 50 €. Saldi e riconciliazione coerenti; nessun trasferimento di denaro.
- Tre sondaggi: dolce del cenone e attività del 1 gennaio aperti con voti sintetici, orario del cenone chiuso con prevalenza delle 20:30 e decisione coerente con il programma. Pubblicazione e risultato esclusivamente nella coda simulata.
- Giochi volontari per almeno otto adulti: un quiz di riscaldamento chiuso con risposte e classifica, uno aperto per ulteriori prove. Bambini e altri partecipanti senza adesione implicita.
- Messaggi simulati realistici già inviati, una bozza e promemoria futuri in coda; configurazione timezone/quiet hours/limiti e scheduler automatico disattivato per l'evento. Nessun WhatsAppConnection, identità reale o messaggio live nello scenario.

## Accettazione

Un implementatore `gpt-6.1-sol` medium prepara il tool e lo prova su copia, poi revisore distinto `gpt-6.1-sol` high verifica isolamento, idempotenza, scritture solo da servizi e conteggi contabili. Il coordinatore conserva backup e confronta le righe preesistenti, esegue il caricamento autorizzato e verifica dal browser tutti i moduli. Report locale con ID evento, conteggi, totali, saldi e riepilogo dei test; nessun numero reale, password o token negli artefatti versionati.

## Esito del caricamento

- Caricatore in `tools/TRobot.ScenarioSeeder`, build senza errori o avvisi; nessuna modifica ai moduli applicativi o allo schema. Review indipendente **APPROVED**.
- Prova su copia riuscita, inclusa ripresa dopo interruzione e seconda esecuzione `--apply` con SQLite aperto in sola lettura. Ulteriore verifica senza `--apply`: hash del DB invariato. Il clock del solo caricatore è 17/10/2026 ore 12:00 Europe/Rome, riportato come `ScenarioAsOfUtc`; il clock del sito non cambia.
- Scenario caricato anche nel database locale corrente e disponibile all'amministratore nella selezione eventi. Verificati 20 confermati, 16 adulti/4 bambini, 8 nuclei, 30 voci menu, 20 attività, 3 sondaggi, 2 quiz e 8 adesioni ai giochi. Messaggi esclusivamente simulati: 8 Sent, 2 Queued e una bozza.
- Contabilità verificata tramite il servizio e nel browser: 3.600 € approvati, 16 quote da 200 € e 4 da 100 €, anticipi complessivi 3.600 €, rimborsi confermati 800 €, somme pagate/ricevute coincidenti e saldi complessivi zero. Spese non approvate e pagamenti ancora dichiarati/annullati esclusi dai totali.
- Backup consistente prima dell'originale; righe preesistenti di tutte le 40 tabelle e credenziali conservate, integrity/FK OK. Nessuna connessione o messaggio WhatsApp reale nel nuovo evento; scheduler automatico disattivato.
- Controllo browser riuscito su dashboard, venti anagrafiche, nuclei, menu, attività, sondaggi, giochi, messaggi, storico e contabilità, anche mobile senza overflow. Nessun errore JavaScript. Report e screenshot in `.local/ui/scenario-capodanno-*`; report del caricatore in `.local/capodanno-original-report.json`, che contiene anche l'ID effettivo dell'evento.

Per una verifica successiva usare il comando senza `--apply` descritto nel [README del caricatore](../tools/TRobot.ScenarioSeeder/README.md). Non ripristina né sovrascrive dati modificati manualmente.
