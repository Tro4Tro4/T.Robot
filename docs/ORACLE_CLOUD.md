# T.Robot — Preparazione Oracle Cloud

**Stato successivo, 23 ottobre 2026:** hosting operativo trasferito ad Azure con DB corrente, chiavi e sessione originali verificati. Oracle Web/Caddy/gateway/timer fermi e disabilitati; non riattivare copie obsolete. Dominio pubblico aggiornato, login e recovery verificati anche dopo reboot completo Azure. Questa pagina conserva la configurazione Oracle storica. Vedere [Azure](AZURE_CLOUD.md).

## Stato al 19 ottobre 2026

Aggiornamento del 23 ottobre 2026: dopo il reboot effettuato dall'utente, rilascio M31 completato sulla VM (`current` -> `releases/m31`). Backup completo privato e copia fuori VM verificati, migrazione additiva applicata con righe precedenti conservate. HTTPS pubblico, login, Alloggi/stampa, desktop/mobile, riavvio Web e backup live con recovery gateway positivi; gateway collegato al gruppo già autorizzato. Causa dei precedenti blocchi ancora sconosciuta e journal del boot precedente assente: diagnosi M30 da completare. Dettagli in `MILESTONE_31.md`.

Aggiornamento M30 successivo al rilascio pubblico: installato gateway sul server con sessione originale, token/journal conservati e DB remoto unica fonte operativa; gateway PC fermato/disabilitato. Verificati connessione e ripristino gruppo, ma la VM diventa poi irraggiungibile a HTTPS/SSH. Collaudo ancora incompleto; necessario recupero console e diagnosi prima di attestare stabilità. Dettagli in `MILESTONE_30.md`; le descrizioni M29 sotto documentano lo stato precedente.

- VM Oracle Linux 9.8 x86_64, shape VM.Standard.E2.1.Micro, circa 945 MiB RAM, swap da circa 945 MiB e filesystem root da 30 GiB (24 GiB liberi alla prima verifica).
- Accesso SSH con utente `opc` e sudo senza password verificato dopo la configurazione della route Internet Gateway nella subnet. Chiave privata e known_hosts rimangono nella directory locale ignorata `.local/oci`.
- La prima interrogazione `dnf list --available aspnetcore-runtime-10.0 sqlite`, con tutti i repository abilitati, ha causato forte pressione sulla memoria e sulla swap. Il riavvio da console ha impiegato oltre venti minuti; ulteriori tentativi sono stati rifiutati da OCI perché l'istanza era già in modifica. Accesso SSH successivamente recuperato, vecchio processo DNF terminato.
- Al ritorno, il kernel UEK riservava 448 MiB a Kdump (`crashkernel=1G-64G:448M,64G-:512M`), lasciando circa 498 MiB a Linux. Fermato Kdump, verificata l'assenza di immagine crash caricata e liberata la riserva tramite `/sys/kernel/kexec_crash_size`: RAM tornata a circa 946 MiB, 695 MiB disponibili alla verifica.
- Disabilitato Kdump e impostato `crashkernel=0` con `grubby` su tutte le voci esistenti, verificate singolarmente. Questo rinuncia alla raccolta del dump del kernel per recuperare memoria sulla micro VM. Configurazione precedente salvata in `/root/trobot-provisioning` (accesso solo root); non serve un altro riavvio per la liberazione attuale. Verificare nuovamente la riserva dopo futuri aggiornamenti del kernel.
- Creato utente di sistema `trobot`, shell `/sbin/nologin`; directory `/opt/trobot/app` root:trobot 0750, `/var/lib/trobot` trobot:trobot 0700, `/var/backups/trobot` root:root 0700. Directory dati verificata vuota, nessun database trasferito, nessun servizio applicativo avviato o pubblicato.
- Il secondo tentativo DNF, limitato a due repository e contenuto con systemd (`MemoryMax=400M`, `MemorySwapMax=400M`, `CPUQuota=50%`), ha mantenuto SSH accessibile ma richiesto diversi minuti solo per gli indici. Fermato prima di iniziare transazioni RPM, verificata l'assenza del processo. Nessun pacchetto RPM applicativo installato.
- Installato invece il runtime ufficiale Microsoft **10.0.13** da archivio Linux x64 in `/opt/dotnet`, con hash SHA-512 verificato contro i metadati ufficiali del canale 10.0. Manifest e archivio conservati in `/root/trobot-provisioning`. Ownership root:root e nessuna scrittura per gruppo/altri sul runtime. Questo metodo richiede aggiornamenti espliciti del runtime, non gestiti da DNF. Nessun SDK installato; i futuri servizi devono usare `/opt/dotnet/dotnet` come percorso esplicito.
- Verificati `Microsoft.NETCore.App` e `Microsoft.AspNetCore.App` 10.0.13 con `--list-runtimes` e `--info` anche come utente `trobot`. Librerie native di sistema presenti; smoke SQLite Python in memoria riuscito (creazione, inserimento, lettura, integrity_check), versione di sistema 3.34.1. Il provider SQLite incluso nel futuro rilascio .NET richiederà un proprio smoke Linux. Nessuna CLI sqlite3 installata e nessun DB applicativo ancora creato o trasferito.
- Ultima verifica: circa 946 MiB RAM totale e 645 MiB disponibili, Kdump disabilitato, riserva crash a zero, nessun listener Web/WhatsApp avviato da questa preparazione. Le verifiche dopo un prossimo riavvio e il collaudo dell'applicazione restano da eseguire.

## Preparazione prevista

Installazione nativa senza Docker, runtime ASP.NET Core 10 e SQLite; compilazione sul PC di sviluppo. Prima di ripetere operazioni DNF, verificare memoria, processi e swap e contenere le risorse; per questo runtime è stato scelto l'archivio ufficiale verificato, evitando gli indici DNF. Non avviare più gestori pacchetti contemporaneamente.

Directory predisposte: `/opt/trobot/app` per il rilascio, `/var/lib/trobot` per i dati persistenti e `/var/backups/trobot` per le copie locali. Il database futuro è `/var/lib/trobot/trobot.db`: trasferire una copia SQLite consistente dei dati esistenti, senza creare un database sostitutivo vuoto. I backup locali root-only non sostituiscono una copia esterna alla VM.

## Rilascio M29 configurato

### Estensione HTTPS pubblico — operativa

L'utente ha creato `trobot-capodanno.duckdns.org`, risolto e verificato verso la VM il 19/10/2026. Installato Caddy **2.11.7** da archivio ufficiale Linux amd64 con SHA-512 verificato, senza DNF. Configurazione in `deploy/oracle/Caddyfile`, unità in `deploy/oracle/caddy.service`: Let’s Encrypt con rinnovo automatico, HTTP solo redirect/challenge, HTTPS pubblico e upstream HTTPS verificato verso `127.0.0.1:5443` usando `/etc/caddy/upstream.crt` e nome TLS localhost. Host pubblico preservato e aggiunto a AllowedHosts; DB, identità, chiavi e gateway invariati.

Utente di servizio `caddy`, dati ACME persistenti in `/var/lib/caddy`, nessun accesso al DB o ai segreti dell'app, admin API e log delle richieste disabilitati, limiti 128 MiB RAM/64 MiB swap/25% CPU. `caddy validate` e `systemd-analyze verify` positivi; revisione indipendente APPROVED. Firewall Oracle Linux aggiornato per TCP 80/443, regola SSH conservata.

**Pubblico e verificato:** dopo l'apertura delle ingress rule Oracle confermata dall'utente, Caddy riavviato e certificato Let’s Encrypt emesso. Accesso **https://trobot-capodanno.duckdns.org**, senza tunnel e con le credenziali Identity originali. HTTP risponde 308 verso HTTPS; TLS 1.3 verificato dal PC con trust di sistema e hostname, certificato per il solo nome richiesto, issuer Let’s Encrypt YE2, scadenza iniziale 17/01/2027 e rinnovo pubblico gestito automaticamente da Caddy.

Collaudo pubblico positivo: anonimi reindirizzati al login, autenticazione originale e sei pagine (dashboard, eventi, soggiorni, nuclei, menu, WhatsApp) HTTP 200, browser desktop/mobile con verifica certificato abilitata, nessun errore JavaScript o overflow. Verificati cookie Secure/HttpOnly, HSTS e header di sicurezza, POST senza CSRF rifiutato 400, sessione mantenuta dopo riavvio Caddy e logout. Servizi Caddy/Web e timer backup abilitati al boot, nessun riavvio automatico inatteso; backup live riuscito. Nessuna modifica C#, migrazione, risemina o sincronizzazione dei dati per questa estensione. I 552 test .NET restano quelli del rilascio M29 invariato; qui eseguite validazioni configurazione e collaudo del servizio effettivo.

Il rinnovo pubblico gestito da Caddy non rinnova il certificato interno localhost: prima del 17/01/2027 rinnovare anche questo e aggiornare `/etc/caddy/upstream.crt`, poi riavviare i servizi e verificarli.

La richiesta successiva dell'utente autorizza esplicitamente la configurazione completa, DB incluso. M29 abilita Production con prerequisiti obbligatori e senza esporre Development. Stato verificato e test in `MILESTONE_29.md`.

- Applicazione: `/opt/trobot/app/current` punta a `releases/m29`, root-only in scrittura. `trobot.service` abilitato al boot, processo utente `trobot`, limiti 350 MiB RAM, 256 MiB swap e 75% CPU. Runtime esplicito `/opt/dotnet/dotnet`.
- Dati: `/var/lib/trobot/trobot.db`, snapshot consistente del DB locale con tutte le 45 tabelle verificate; chiavi persistenti in `/var/lib/trobot/keys`. Credenziali Identity originali conservate, nessuna reinizializzazione. I due database sono indipendenti: nessuna sincronizzazione con il PC.
- Configurazione privata: `/etc/trobot/trobot.env` root 0600; PFX in `/etc/trobot/tls`, lettura limitata al servizio. Nessun segreto nel rilascio o nel repository.
- HTTPS interno: solo `127.0.0.1:5443`, proxy Caddy pubblico sulle porte TCP 80/443. Il tunnel SSH verso `https://localhost:5443` rimane disponibile come accesso amministrativo alternativo tramite `deploy/oracle/Open-Tunnel.ps1 -ServerAddress <IP_VM>`; non serve per il dominio pubblico.
- Certificato provvisorio autofirmato `localhost`, valido fino al **17/01/2027**. Copia pubblica locale `.local/oci/localhost.crt`, nessuna modifica al trust di Windows: il browser mostrerà un avviso. I controlli HTTP hanno verificato certificato e nome con trustfile esplicito. Prima della scadenza sostituirlo con certificato pubblico oppure rinnovare il certificato privato mantenendo SAN localhost/127.0.0.1, ricreare il PFX con la password nel file root riservato, verificarlo e riavviare il servizio; distribuire la nuova copia pubblica ai client. Non cancellare le chiavi Data Protection durante il rinnovo.
- Backup: `/var/backups/trobot/<timestamp>/trobot.db` e `keys/`, root-only; timer giornaliero verso le 03:30 UTC (ritardo fino a 15 minuti), nessuna cancellazione automatica. Integrità/FK e ripristino con confronto delle 45 tabelle verificati. Prima copia esportata sul PC in `.local/oci/oracle-backup-m29.tar.gz`; backup futuri fuori VM non automatici.

Comandi amministrativi sulla VM:

```bash
sudo systemctl status trobot --no-pager
sudo systemctl restart trobot
sudo systemctl start trobot-backup
sudo systemctl list-timers trobot-backup.timer
```

Per un ripristino, fermare il servizio, conservare una copia dei dati attuali, verificare il backup su una destinazione distinta, poi ripristinare insieme database e chiavi con proprietario `trobot` e permessi 0600/0700; riavviare e verificare accessi e pagine. Non copiare un file SQLite vivo con WAL usando una semplice copia filesystem. Il gateway WhatsApp locale rimane invariato: il trasferimento e collegamento sul server richiedono un intervento esplicito separato.

Riferimenti: [runtime ASP.NET Core sui sistemi RHEL](https://learn.microsoft.com/en-us/dotnet/core/install/linux-rhel), [pacchetti Oracle Linux 9 x86_64](https://yum.oracle.com/repo/OracleLinux/OL9/appstream/x86_64/index.html).

## Aggiornamento M32, 23 ottobre 2026

Dopo il nuovo reboot dell'utente, release `m32` attiva: rinomina nuclei, quattro moduli del portale sospesi e favicon T.Robot. Backup completo privato su VM/PC, rehearsal e migrazione additiva con valori precedenti conservati, HTTPS/login e sessione dopo restart verificati. Gateway sul gruppo originale anche dopo backup live, identita/configurazione/chiavi preservate. Nessun invio di prova. Dettagli in `MILESTONE_32.md`; causa dei blocchi precedenti ancora da verificare.
