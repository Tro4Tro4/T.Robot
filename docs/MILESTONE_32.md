# M32 - Rinomina nuclei e disponibilita del portale

Richiesta del 23 ottobre 2026: implementare i due appunti registrati dopo Alloggi. Elenco chiarito dall'utente: sospendere dal portale **Sondaggi, Giochi, Menu e Attivita**. Gli altri moduli restano disponibili. Disegno circoscritto presentato in chat con la skill brainstorming; dettagli scelti in autonomia secondo AGENTS e registrati in DECISIONS.

## Rinomina nuclei

La pagina Nuclei offre Rinomina per ogni nucleo. Form GET/POST riservato agli organizzatori assegnati, antiforgery, revisione obbligatoria e conferma della modifica. Nome normalizzato con limite di 150 caratteri; errori mostrati mantenendo l'input. Salvataggi invariati non modificano revisione o audit.

Rinomina transazionale con audit e controllo di concorrenza: ID, membri attivi/storici, classificazioni, accompagnatori, presenze e assegnazioni delle camere rimangono invariati. Il nuovo nome aggiorna i riepiloghi; una rinomina vera rende obsoleti il piano Alloggi definitivo e le conferme familiari pendenti, senza annullare presenze individuali gia registrate.

`Household.CreationNameFingerprint` conserva SHA-256 del nome originale normalizzato: i reinvii della creazione con lo stesso identificativo e payload originario restituiscono il nucleo esistente senza ripristinarne il vecchio nome. Payload diversi sono conflitti. Per le righe legacy il fingerprint resta null dopo migrazione e viene fissato prima della prima vera rinomina, nella stessa transazione. Nessuna copia in chiaro del nome precedente nell'audit.

La migrazione `20261023092207_AddHouseholdRenameFingerprint` aggiunge soltanto una colonna nullable. Il fingerprint degli Alloggi continua a serializzare Id/EventId/Name/Revision del nucleo, escludendo il metadato tecnico: l'introduzione della colonna non invalida i piani M31 gia definitivi.

## Moduli sospesi dal portale

Configurazione versionata `PortalModules:Enabled` con bool validati per Tasks/Menu/Polls/Games/Messages/Simulator. Default effettivi: Tasks, Menu, Polls e Games false; Messages e Simulator true. Chiavi non riconosciute o valori non booleani impediscono l'avvio. La configurazione definitiva dell'host viene caricata tramite DI e validata all'avvio; una modifica delle impostazioni richiede il riavvio dell'applicazione.

I tab sospesi e i collegamenti secondari diventano testo grigio senza href, con aria-disabled e indicazione di indisponibilita temporanea. Il filtro MVC nega con 404 tutte le azioni GET/POST dei quattro controller, anche da URL diretto, alias convenzionale o percorso con differenze di maiuscole. La verifica precede antiforgery/model binding e servizi; non basta nascondere i link.

Il simulatore resta disponibile per le presenze. I comandi dei moduli sospesi (`!menu`, `!porto`, `!todo`, `!prendo`, `!fatto`, `!sondaggi`, `!sondaggio`, `!vota`, `!quiz`, `!rispondi`) vengono respinti prima dell'esecuzione Application. Istruzioni e aiuto mostrano solo comandi disponibili. La classificazione usa gli stessi token e regole di whitespace/case del parser esistente.

Disponibilita limitata al portale: servizi Application e comandi WhatsApp operativi non vengono disattivati. Gateway, callback, associazioni, ricevute e journal rimangono invariati. Contabilita, nuclei, Alloggi, presenze/soggiorni, Messaggi, WhatsApp, eventi e storico restano accessibili agli organizzatori autorizzati. Nessuna cancellazione di dati o storico.

## File e verifiche

Rinomina: Domain/Household, Application/Households, mapping HouseholdConfiguration, controller/input/viste Households, proiezione Alloggi e migrazione. Portale: Security/PortalModules e PortalModuleLinkTagHelper, Program, appsettings, import Razor, navigazione, Home e CSS. Test nuovi: HouseholdRenameTests, HouseholdRenameMigrationTests, PortalModuleTests. La factory dei test precedenti abilita esplicitamente i moduli del prototipo; i test dei blocchi usano sia override dedicati sia gli effettivi default versionati.

Build/test coordinati sequenzialmente. Due implementatori distinti e revisore indipendente gpt-6.1-sol medium. Review complessiva **APPROVED**. Test mirati: **16 rinomina/migrazione e 13 portale**, tutti superati. Il test di upgrade confronta ogni colonna preesistente di ogni tabella, inclusi nuclei legacy, membri, camere e piano definitivo, con integrita/FK verificate.

Comandi:

```powershell
dotnet build TRobot.sln --no-restore
dotnet test tests/TRobot.Tests/TRobot.Tests.csproj --no-restore --filter "FullyQualifiedName~HouseholdRename|FullyQualifiedName~PortalModuleTests"
dotnet test TRobot.sln --no-build --no-restore
dotnet ef migrations has-pending-model-changes --project src/TRobot.Infrastructure --no-build
```

Nessun DB operativo modificato, servizio remoto avviato/fermato o rilascio eseguito per M32. La VM rimane alla release M31 fino al prossimo rilascio autorizzato.

## Esito del 23 ottobre 2026

Build senza errori o avvisi, **29/29 test mirati** e **608/608 test .NET** superati; nessun test ignorato. Modello EF allineato alla migrazione e controllo whitespace positivo. Review complessiva indipendente **APPROVED**.

I test hanno verificato anche il caricamento della configurazione definitiva dell'host: risolto il singleton dal provider DI dopo builder.Build, mantenendo la validazione immediata all'avvio. I test dedicati controllano sia i default versionati dei quattro moduli sia un host esplicitamente tutto sospeso, senza allentare i blocchi di GET/POST o i controlli antiforgery.

Rinomina: verificati replay originari anche dopo piu rinomine, nuclei legacy, mancata conferma/revisione, account revocati, concorrenza EF e rollback dell'audit; mantenute tutte le colonne precedenti durante l'upgrade da M31. Test reali del dialogo WhatsApp confermano che una rinomina invalida la conferma familiare pendente, mentre il no-op la conserva; il rifiuto della conferma obsoleta non annulla presenze individuali precedentemente registrate.

Nessuna modifica al codice del gateway o test Node necessari per questo incremento. Verifiche UI tramite rendering Razor e pipeline HTTP con cookie e antiforgery reali su SQLite temporaneo, senza browser esterno o accesso al sito operativo. Prossimo passo: rilascio M32 sulla VM con backup, migrazione additiva e collaudo della rinomina e dei quattro tab sospesi.

## Rilascio autorizzato, in attesa del recupero VM

L'utente autorizza esplicitamente il rilascio M32 con favicon. Publish Release linux-x64 framework-dependent con UseAppHost=false completato senza errori o avvisi. Archivio privato `.local/oci/release-m32.tar.gz`, 8.943.372 byte, SHA-256 `3D519B7F9C6DD9F25290CE0341176D2298CFB52F919A3F40AF98B92B9A1E0833`. SQL privato `.local/oci/m32-migration.sql`, SHA-256 `3C67D2BF7EADDCC1E9ABDDA6EDD7A7384802D474FE2037DA2491B399DF37784E`. Pacchetto senza database o segreti, comprensivo di favicon SVG/ICO e default dei quattro moduli sospesi.

Script di staging, backup/rehearsal, migrazione e attivazione con rollback predisposti per current m31 -> m32. Review indipendente gpt-6.1-sol medium: APPROVED per il materiale e gli script, non per un deploy ancora da eseguire. Il validatore SQL usa BEGIN IMMEDIATE e confronta tutte le vecchie colonne di ogni tabella prima del commit; richiede nuova colonna nullable e null su tutti i nuclei legacy. Prova su copia storica M31 positiva: 47 tabelle preesistenti conservate, integrity/FK OK. Errore sintetico sull'INSERT nella cronologia prova il rollback completo della colonna e della voce di migrazione. Report privato `.local/oci/m32-validator-success-report.json`; lo snapshot storico non attesta lo schema remoto corrente.

Primi tentativi SSH bloccati durante il banner, controlli HTTPS diretti IPv4 e tramite configurazione di rete Windows in timeout. L'utente precisa di avere rifatto la login alla console Oracle senza reboot e chiede di riprovare: nuovo tentativo SSH ancora in timeout. Nessun comando remoto eseguito, servizio fermato, backup M32 creato o file trasferito. Database e release remoti non modificati da questo tentativo; M32 NON rilasciata. Causa non verificata, diagnosi M30 ancora aperta.

Alla ripresa dopo recupero della VM, l'autorizzazione al rilascio rimane valida. Fermare gateway, verificare current/schema e risorse, trasferire il materiale approvato e validarne i checksum, backup completo consistente, rehearsal sulla copia del DB remoto, applicazione con confronto dei valori prima del commit e cambio atomico current. Collaudare rinomina senza modificare dati reali inutilmente, quattro tab/URL/comandi simulati sospesi e favicon anonima/versionata, poi recovery del gateway originale e backup live. Non riattivare il gateway PC, non sostituire il DB remoto con copie locali e non inviare messaggi di prova.

## Rilascio completato dopo il reboot, 23 ottobre 2026

Dopo il reboot comunicato dall'utente, accesso SSH ripristinato e gateway fermato temporaneamente. Materiale approvato trasferito con checksum corrispondenti. Backup completo consistente (DB, chiavi, configurazione e stato WhatsApp), rehearsal sulla copia del DB remoto e migrazione effettiva riusciti: 47 tabelle preesistenti confrontate, tutti i valori delle colonne precedenti conservati, unica nuova colonna nullable con valori legacy null, integrity_check OK e nessuna violazione FK. Cambio atomico current da releases/m31 a releases/m32; HTTPS interno e login HTTP 200.

Collaudo HTTPS pubblico con verifica TLS e credenziali originali riuscito: Eventi, Alloggi/stampa, Soggiorni e Nuclei HTTP 200, form Rinomina disponibile e POST senza antiforgery respinto; nessuna rinomina di dati operativi eseguita per il collaudo. Tasks/Menu/Polls/Games GET e POST effettivi HTTP 404, quattro tab aria-disabled senza href. Favicon SVG uguale al sorgente e collegamenti SVG/ICO versionati presenti. Browser desktop 1440 e mobile 390 senza overflow o errori JavaScript; screenshot privati. Sessione originale ancora valida dopo restart Web, logout verificato. Le regole di rinomina e simulatore restano coperte dalla suite locale di 608 test.

Backup precedente esportato nella cartella privata locale con ACL ristrette; SHA-256 locale e remoto coincidenti: `48935D7235038C8E4D3087D2E0E47C259AA3D5B8D94070D04D5639E90CF8F556`. Report privati m32-rehearsal-report e m32-production-report sulla VM; copia di backup in `.local/oci/m32-private-backup`.

Gateway e timer backup ripristinati. Identita WhatsApp, configurazione privata e chiavi Data Protection conservate. Gateway connected con gruppo autorizzato originale, anche dopo backup live concluso con Result=success/ExecMainStatus=0. Il primo controllo immediatamente dopo il backup ha trovato la porta gateway ancora in avvio (ConnectionRefused); il controllo successivo e riuscito senza interventi. NRestarts=0 per Web e gateway al controllo finale, porte applicative solo loopback. Nessun messaggio di prova, pairing o riattivazione del gateway PC. La causa dei precedenti blocchi SSH/HTTPS rimane non verificata; M30 non viene dichiarata conclusa.


## Nuova indisponibilita dopo M32 - 2026-10-23 13:58:03 +02:00

Utente segnala portale irraggiungibile. Verifica dal PC: DNS 129.152.13.90, HTTPS timeout di connessione dopo 10 secondi e SSH timeout durante banner exchange. Nessun comando remoto di diagnosi eseguito e nessun servizio modificato. Il collaudo positivo del rilascio non attesta stabilita nel tempo. Necessario accesso console per raccolta evidenze e arresto temporaneo gateway; causa ancora non verificata. Evitare reboot prima dei log se la console risponde.
