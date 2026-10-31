# Azure - preparazione hosting T.Robot

**Stato attuale: trasferimento completato il 23 ottobre 2026.** [T.Robot](https://trobot-capodanno.duckdns.org) risolve 4.223.75.149 e risponde da Azure con TLS/login verificati senza override. Azure e l'unica fonte operativa; Oracle e PC rimangono disabilitati. Le sezioni seguenti conservano la sequenza e le difficolta incontrate. FreeTrial spendingLimit On conservato; l'IP pubblico resta nel credito iniziale, non nel beneficio gratuito della VM.

## Richiesta e vincoli, 23 ottobre 2026

Rifinitura mobile successiva autorizzata e rilasciata lo stesso giorno: current `mobile-20261023-arm64`. Menu compatti, schede partecipanti/nuclei e ricerca, con fallback senza JavaScript. Backup privato sulla VM, confronto completo delle righe riuscito, nessuna migrazione e gruppo WhatsApp originale connesso. Dieci pagine, 40 controlli pubblici e tre percorsi senza JavaScript superati. Dettagli in [UI_MOBILE.md](UI_MOBILE.md).

Aggiornamento successivo dello stesso giorno: rilasciata la grafica Impeccable nella release `ui-20261023-arm64`, senza migrazioni e con tutte le righe operative conservate. Backup privato sulla VM, sessione login originale ancora valida, gateway connesso al gruppo originale e collaudo pubblico desktop/mobile riusciti. Dettagli in [UI_RELEASE_AZURE.md](UI_RELEASE_AZURE.md).

L'utente ha creato un account Azure e autorizza la configurazione come alternativa alla VM Oracle instabile. Obiettivo: ospitare portale .NET, SQLite e gateway Node conservando identita, storico, chiavi e sessione WhatsApp. Preferenza economica: offerta gratuita, nessuna risorsa a pagamento implicita.

Preparazione infrastrutturale con brainstorming; autonomia secondo AGENTS. Prima della creazione verificare sottoscrizione effettiva, beneficio gratuito, SKU, quota regionale, disco e rete/IP. Il credito iniziale non equivale a gratuita permanente. Non rimuovere limiti di spesa e non convertire la sottoscrizione a consumo implicitamente.

## Disegno e sequenza

1. Accesso Azure CLI isolato in `.local/azure/cli`, con ACL private; nessun token versionato. Sessioni Azure preesistenti non riutilizzate per creare risorse.
2. Verifica offerta e disponibilita. Preferenza Linux x64 per riusare i pacchetti M32; nessuna selezione automatica di una taglia non coperta dal beneficio. La memoria va misurata: non assumere che lo SKU gratuito sia piu capiente di Oracle.
3. Un gruppo di risorse dedicato, VM con disco persistente, SSH ristretto alla sorgente amministrativa e soli HTTP/HTTPS pubblici. Nessun database gestito, Bastion, NAT Gateway o workspace di logging implicito. Verificare il costo di IPv4 e disco prima di creare.
4. Installazione nativa riusando le separazioni e le protezioni dei servizi esistenti. Log persistenti con retention limitata e metriche per diagnosticare blocchi. Scheduler Production disabilitato.
5. Preparare prima host e release, con gateway disattivato. Recuperare il DB remoto corrente e la sessione Oracle dopo arresto coordinato e backup consistente: la copia privata pre-M32 e una salvaguardia, non la fonte operativa aggiornata.
6. Collaudare HTTPS, login, dati, backup e ripristino; solo dopo cambiare destinazione pubblica e attivare la sessione originale su Azure. Nessun gateway concorrente e nessun messaggio di prova automatico. Oracle conservata come fallback fermo, senza distruzione di risorse implicita.

## Stato

CLI Azure disponibile e nuovo account autenticato tramite device-code in configurazione separata. Sottoscrizione attiva Azure subscription 1, offerta FreeTrial_2014-09-01 e spendingLimit On, da non rimuovere. Gruppo dedicato rg-trobot creato e provider Compute/Network/Storage registrati. L'utente sceglie esplicitamente la VM da 1 GB inclusa nell'offerta anziche una taglia piu grande a credito.

Configurazione candidata Standard_B2ats_v2 (2 vCPU, 1 GiB RAM), Ubuntu 24.04 x64 Gen2, disco Premium_LRS 64 GiB P6 incluso nel beneficio nei limiti dell'offerta. IPv4 Standard a costo separato coperto dal credito iniziale: non e hosting interamente gratuito permanente. Nessuna conversione a consumo autorizzata implicitamente.

Europa occidentale, Nord Europa B1s, Italia e Francia hanno rifiutato gli SKU verificati con NotAvailableForSubscription/SkuNotAvailable. Validazione ARM riuscita in swedencentral per Standard_B2ats_v2: avviata creazione in quella regione, senza fallback a taglie a pagamento. Template host.json e cloud-init.yaml revisionati in sola lettura da gpt-6.1-sol medium: APPROVED. Parametro cloudInit codificato Base64; cloud-init e swap da verificare dopo provisioning.

Oracle recuperata dopo il reboot, gateway fermato e Web/Caddy attivi. Nessun trasferimento operativo o seconda sessione WhatsApp avviati.

## Host ARM creato

Il tentativo AMD in Svezia ha superato il preflight ma e fallito con AllocationFailed. Risorse vuote del tentativo in rg-trobot eliminate (group exists=false), nessun dato operativo era stato trasferito. Rifiutati anche gli SKU x64 gratuiti verificati in altre regioni europee e USA, senza fallback a pagamento.

Template aggiornato e nuovamente revisionato APPROVED per Standard_B2pts_v2 (2 vCPU, 1 GiB ARM64) e immagine Ubuntu24.04 server-arm64. Creazione riuscita in swedencentral, gruppo rg-trobot-host, VM trobot-vm, IP 4.223.75.149. cloud-init status done senza errori, architettura aarch64, swap 2 GiB attiva, journal persistente, SSH con sola chiave funzionante. Chiave host acquisita dal canale autenticato Azure Run Command e fissata in known_hosts privato prima della connessione.

Publish M32 linux-arm64 framework-dependent riuscito. Runtime ASP.NET Core10.0.13, Node24.22.0 e Caddy2.11.7 ARM64 scaricati dalle fonti ufficiali e verificati con i checksum pubblicati. Gateway mantiene il pacchetto M30 originale; dipendenze da installare sulla piattaforma ARM. Upload e preparazione servizi in corso, nessuna attivazione operativa ancora.

## Runtime pronto e trasferimento interrotto

Upload completato, cinque checksum sul server corretti. prepare-runtime.sh eseguito con successo: runtime ARM installati, M32 in /opt/trobot/app/releases/m32-arm64, gateway originale in /opt/trobot/gateway/releases/m30-arm64, npm ci concluso con 103 dipendenze e nessuna connessione WhatsApp. Script runtime/export/import revisionati indipendentemente da gpt-6.1-sol medium: APPROVED.

Prima del freeze collaudo reale isolato: schema EF generato e applicato solo in /var/lib/trobot-preflight/test.db, bootstrap sintetico via .NET ARM con SQLite e Identity riuscito, integrity OK e GET login HTTP 200 su loopback. Il servizio Development sintetico e stato fermato, nessun dato o sessione operativi impiegati. Nessuna migration sul DB operativo per il passaggio di architettura.

Tentativo di trasferire export-oracle.py verso Oracle non completato; successivo SSH in timeout durante banner e HTTPS in timeout di connessione. Nessun export freddo, import dati o cambio DNS completato. Il gateway era stato fermato al precedente recupero: non attribuire il nuovo blocco al solo gateway. Richiesto all'utente un nuovo reboot per acquisire il DB remoto corrente e la sessione originale; non usare copie storiche come fonte operativa aggiornata senza una decisione esplicita sulla perdita di modifiche.

Stato finale di questa fase: infrastruttura e runtime Azure pronti, attivazione operativa in attesa del recupero Oracle. Nessun servizio applicativo Production abilitato su Azure, nessun secondo gateway avviato, dominio pubblico ancora su Oracle. Controllare l'eventuale directory /root/trobot-provisioning/azure-cutover prima di ripetere l'export: lo script rifiuta tentativi esistenti anziche sovrascriverli. Dopo il recupero: export verificato, trasferimento privato/hash, import, verifica TLS/login con destinazione Azure, passaggio dominio, recovery unica sessione e backup live.

## Passaggio operativo ad Azure verificato, DNS in attesa

Dopo il reboot comunicato dall'utente, Oracle ha inizialmente rifiutato SSH, poi ha accettato la chiave e completato il cold export. Archivio privato SHA-256 `6420B0FEB2E38F15FD76E03BF7AC4BF241B9F4E4DEB11397E94AA8F7B6123CDA`, verificato anche dopo download nella cartella con ACL ristrette `.local/azure/private-cutover` e upload Azure. Esportati insieme DB remoto corrente, chiavi Data Protection, configurazione Web/TLS, stato/configurazione WhatsApp, stato ACME Caddy, unit e script operativi. Manifest di ogni file verificato prima dell'import. SQLite controllato prima dell'avvio e dopo il collaudo: integrita OK, nessuna violazione FK, migrazione M32 presente. Hash del DB installato identico allo snapshot prima dell'avvio; nessuna migration o reinizializzazione operativa.

Oracle: Web, Caddy, gateway e timer backup **inactive e disabled**, verificati dopo il passaggio. Azure e ora l'unica fonte operativa; non riabilitare Oracle o PC con copie obsolete. Per un eventuale rollback serve fermare prima Azure e acquisirne una nuova copia consistente: il precedente snapshot Oracle non include i comandi ricevuti successivamente su Azure.

Web e Caddy Azure attivi e abilitati. HTTPS interno con CA/hostname verificati, HTTPS pubblico con certificato originale verificato tramite risoluzione diretta del dominio all'IP Azure, login originale e cookie sicuri. Smoke con browser: Eventi, Alloggi/stampa, Soggiorni e Nuclei200, form rinomina200 e antiforgery400; GET/POST effettivi dei quattro moduli sospesi404; favicon e link versionati; desktop1440/mobile390 senza overflow o errori JS. Sessione valida dopo restart Web e logout verificato. Report privati `.local/azure/azure-smoke-report.json`, screenshot `.local/ui/azure-server-*`.

Recovery WhatsApp: la prima esecuzione su Ubuntu ARM fallisce nella lettura SQLite con `runuser: cannot set user id: Operation not permitted`. Diagnosi comparativa nel sandbox completo Python: fallisce con LockPersonality=true, riesce variando solo LockPersonality=false. Applicato esclusivamente al recovery il drop-in versionato `deploy/azure/trobot-whatsapp-resume.conf`, revisionato APPROVED; NoNewPrivileges, protezioni filesystem e lettura come proprietario DB conservate. Web e gateway mantengono le loro protezioni. Non confondere questa incompatibilita riprodotta con la causa dei vecchi blocchi Oracle.

Gateway Azure **connected**, gruppo autorizzato originale ripristinato, identita e configurazione byte per byte verificate e chiavi Data Protection conservate. Primo backup e backup live riusciti, recovery post-backup riuscito; servizi/timer attivi e abilitati, NRestarts=0 per Web/gateway al controllo finale, circa 475 MiB disponibili e swap inutilizzata. Nessun messaggio di prova, nuovo pairing o reinvio di vecchie code. Report privato `/root/trobot-provisioning/azure-preservation-report.json`. Un avvio iniziale del proxy aveva dato502 prima della readiness Web; verifiche successive HTTPS 200 e login positivo.

Resta il DNS: richiesto all'utente di aggiornare DuckDNS `trobot-capodanno` da129.152.13.90 a4.223.75.149; al controllo successivo risolve ancora Oracle. Non dichiarare l'indirizzo abituale raggiungibile prima della propagazione e del collaudo senza override. Il credito trial resta protetto da spendingLimit On; IP pubblico a costo separato nel credito, nessuna conversione a consumo effettuata.

Diagnosi Oracle: dopo il reboot load average 8.61 e primo campione vmstat cumulativo con 72% steal, successivi campioni idle; circa 499 MiB disponibili e swap 1 MiB dopo arresto servizi. Evidenza utile ma insufficiente per attribuire definitivamente la causa all'hypervisor o escludere tutte le altre cause. Stabilita Azure nel tempo ancora da osservare.

## DNS e riavvio completo verificati

L'utente conferma l'aggiornamento DuckDNS. DNS risolve 4.223.75.149, HTTPS pubblico200 con IP remoto Azure e redirect HTTP 308 verso HTTPS, senza override o disattivazione della verifica TLS. Smoke completo ripetuto sul dominio pubblico: login originale, quattro pagine private, stampa, cinque rifiuti antiforgery Alloggi, form rinomina, quattro moduli GET/POST404, favicon e desktop/mobile positivi. Report privato `.local/azure/azure-public-smoke-report.json`.

Riavvio controllato della VM tramite Azure CLI completato. Web/Caddy/gateway/timer tornati active ed enabled automaticamente, recovery Result=success/ExecMainStatus=0, gateway connected sul gruppo originale, nessun nuovo pairing o messaggio di prova. Sessione pubblica antecedente al reboot ancora valida e logout verificato. Nuovo controllo di integrita/FK, configurazione, chiavi e identita positivo; NRestarts=0 al controllo corrente, circa 526 MiB disponibili e swap0. Journal conserva entrambi i boot, quindi la diagnostica sopravvive ai riavvii. Nessuna promessa di stabilita a lungo termine basata sul solo collaudo.

I default del template versionato sono allineati all'host effettivamente riuscito: swedencentral e Standard_B2pts_v2 ARM64. Nessuna modifica alle dimensioni a pagamento o al limite di spesa. Il piccolo drop-in del recovery va mantenuto nell'installazione Azure.

Riferimenti ufficiali verificati:

- [Offerta account Azure](https://azure.microsoft.com/en-us/pricing/purchase-options/azure-account)
- [Evitare addebiti](https://learn.microsoft.com/en-us/azure/cost-management-billing/manage/avoid-charges-free-account)
- [Limite di spesa](https://learn.microsoft.com/en-us/azure/cost-management-billing/manage/spending-limit)
