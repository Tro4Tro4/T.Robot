# Google Cloud - hosting T.Robot

**Stato al 25 novembre 2026.** Il 24 novembre l'utente ha spostato T.Robot da Azure a Google Cloud. Il trasferimento è stato fatto fuori da questo repository, che ne conserva solo i materiali privati in `.local`. Google è l'unica fonte operativa.

## Infrastruttura

- Progetto `t-robot-515709`, VM `trobot`, zona `us-central1-c`, tipo `e2-micro` x86_64 (circa 1 GB di RAM), disco da 29 GB, IP pubblico `34.29.22.143`. `trobot-capodanno.duckdns.org` punta qui.
- Firewall: `allow-trobot-web` (80/443 da ovunque), più le regole `default-*` del progetto (SSH 22, RDP 3389, ICMP, traffico interno).
- Release attive: Web `/opt/trobot/app/releases/md-20261121-x64` (M35), gateway `/opt/trobot/gateway/releases/m34-x64`. Il DB è aggiornato all'ultima migrazione, `20261119084028_MessageDelivery`.
- Gli script operativi in `/opt/trobot/ops` (`backup.py`, `backup-with-gateway.sh`, `resume-gateway.py`) e le unit systemd sono gli stessi di Azure.

## Accesso amministrativo

- CLI `gcloud` installata con scoop (bucket `extras`) e autenticata con l'account Google del progetto. Progetto predefinito: `t-robot-515709`.
- Chiave SSH `~/.ssh/google_compute_engine` registrata nei metadati del progetto da `gcloud compute ssh`. L'utente remoto ha `sudo` senza password.
- La chiave host ED25519 (`SHA256:oT+xRu5pCYgZ9tq3NlJ/dQ4HNwnMIvI7nyz2yQGjnu0`) è stata confrontata con l'output di cloud-init nella console seriale e fissata in `.local/gcp/known_hosts`.
- Accesso con OpenSSH: `ssh -i ~/.ssh/google_compute_engine -o UserKnownHostsFile=.local/gcp/known_hosts -o StrictHostKeyChecking=yes <utente>@34.29.22.143`.

## Dati

- Il DB operativo deriva dal backup del 23 ottobre 2026, cioè dall'export Oracle verso Azure, portato all'ultima migrazione. Le modifiche fatte su Azure fra il 23 ottobre e il 24 novembre non sono presenti: per esempio, nessuna proposta pasti.
- Il 25 novembre la VM Azure non era più raggiungibile (SSH e HTTPS in timeout). L'utente ha deciso di non recuperarla: i pochi dati mancanti verranno reinseriti a mano dal portale.
- L'evento di simulazione rimosso il 23 ottobre non è presente nel DB operativo.

## Backup

- Il 25 novembre `trobot-backup.timer` era disabilitato e non era mai partito. Prima di attivarlo è stato eseguito un backup di prova: esito positivo, 8,1 MB con DB, chiavi, configurazione e sessione WhatsApp, integrità e foreign key verificate. Il gateway, fermato durante la copia, si è riconnesso da solo al gruppo autorizzato in circa due minuti.
- Il timer è abilitato e attivo: ogni giorno alle 03:30 UTC, con un ritardo casuale fino a 15 minuti. I backup restano sulla VM in `/var/backups/trobot`, senza rotazione automatica né copia esterna.

## Avvio automatico

Il 25 novembre `trobot.service`, `trobot-whatsapp.service` e `caddy.service` risultavano `disabled`: dopo un riavvio della VM non sarebbero ripartiti da soli. Su richiesta dell'utente sono stati abilitati all'avvio (`systemctl enable`, senza riavviarli). Il gateway richiama `trobot-whatsapp-resume.service` tramite `Wants=`. Dopo la modifica tutti i servizi risultano attivi, con NRestarts=0, e il sito risponde 200. Il comportamento dopo un riavvio reale non è ancora stato collaudato.

## Procedura di rilascio

Script versionati in `deploy/google/`. Generalizzano le procedure Azure già usate con successo, adattate a x64.

- `publish.py`: driver locale, da eseguire sul PC dalla root del repository.
- `release.py`: script remoto, eseguito come root e installato in `/opt/trobot/ops/release.py`.
- `smoke.py`: collaudo pubblico.
- `test_release.py`: test offline (`python -m unittest deploy/google/test_release.py`).

La configurazione dell'host è in `.local/gcp/host.json`, che non è versionato e contiene host, utente, chiave e `known_hosts`. Il materiale di ogni release finisce in `.local/gcp/releases/<nome>`.

```powershell
python deploy/google/publish.py prepare  <nome> [--gateway]   # build linux-x64, pacchetti e hash, SQL dalla migrazione presente sulla VM
python deploy/google/publish.py release  <nome>               # upload + stage + activate + verify + smoke
# oppure un passo alla volta: upload, stage, activate [--allow-changes T1,T2], verify; poi python deploy/google/smoke.py
```

Nome consigliato: `<argomento>-AAAAMMGG-x64`. Ogni nome si usa una sola volta: un tentativo fallito richiede un nome nuovo.

Cosa fa:

1. **prepare.** Pretende che `src/` e `gateway/` non abbiano modifiche non committate, poi registra il commit. Esegue `dotnet publish` in Release `linux-x64` framework-dependent e crea il pacchetto del Web, escludendo DB, chiavi, sessioni e configurazioni di produzione. Con `--gateway` crea anche il pacchetto del gateway: solo `src/*.js`, `package.json` e `package-lock.json`. Legge l'ultima migrazione applicata sul DB della VM e, se il codice ne ha di più recenti, genera lo script SQL con `dotnet ef migrations script`. Rifiuta di procedere se la VM è più avanti del codice.
2. **upload.** Copia i pacchetti e `release.py` in `~/trobot-staging/<nome>` dell'utente SSH, installa `release.py` e ne verifica l'hash sulla VM.
3. **stage.** Estrae i pacchetti in `releases/<nome>`, dopo aver verificato hash e file ammessi. Controlla che il runtime sia `linux-x64`. Per il gateway pretende dipendenze identiche alla release attiva: copia `node_modules`, poi verifica sintassi e import come utente del servizio. Copia l'SQL in `/opt/trobot/ops/migrations`. Non tocca i servizi.
4. **activate.**
   - Richiede servizi attivi e gateway connesso al gruppo autorizzato.
   - Ferma timer, recovery, gateway e Web.
   - Fa un backup coerente, con DB, chiavi, sessione e configurazione Web.
   - Se c'è una migrazione, la prova prima su una copia del backup e poi la applica in transazione, con foreign key verificate prima del commit. Ogni tabella e colonna preesistente deve restare, con lo stesso numero di righe e righe identiche sulle colonne vecchie. Fanno eccezione le tabelle passate con `--allow-changes`: per le migrazioni che aggiornano dati va deciso esplicitamente.
   - Cambia `current` in modo atomico e avvia il Web, verificando che DB, configurazione, chiavi, identità WhatsApp e associazione al gruppo siano invariati.
   - Avvia il gateway, che richiama il recovery, e aspetta che sia `connected` sul gruppo autorizzato. Poi riattiva il timer.
   - In caso di errore torna da solo ai binari precedenti e lascia lo schema migrato, che resta additivo. Non ripristina mai un DB vecchio. Se nemmeno il rollback si verifica, ferma gateway e timer.
   - Il contesto della release resta in `/root/trobot-provisioning/<nome>-context.json`.
5. **verify.** Controlla:
   - release correnti;
   - servizi attivi e abilitati all'avvio;
   - gateway connesso al gruppo;
   - integrità, foreign key e history delle migrazioni;
   - NRestarts.
6. **smoke.** Controlla:
   - TLS e login;
   - 12 pagine riservate;
   - stato live del gateway;
   - asset identici ai sorgenti;
   - moduli sospesi 404;
   - POST senza antiforgery rifiutati;
   - logout.

Non modifica dati.

Verifiche del 25 novembre 2026:

- 17 test offline positivi.
- Script EF reale da `AddHouseholdRenameFingerprint` a `MessageDelivery` applicato con successo a una copia locale del backup del 23 ottobre: 56 tabelle, 133 tra indici e altri oggetti, foreign key preservate.
- `prepare`, `upload` e `stage` eseguiti sulla VM con la release di prova `check-20261125-x64`, poi rimossa. La release attiva e i servizi non sono cambiati.
- Smoke pubblico positivo sul sito attuale.
- `activate` non è ancora stato eseguito in produzione: la prima esecuzione reale avverrà con il prossimo rilascio.
- Revisione indipendente: prima CHANGES REQUESTED (falsi fallimenti dovuti a scritture del Web avviato e a rotazione delle chiavi, segnali, stage parziale, controlli dello schema), poi APPROVED dopo le correzioni.

Limiti: le migrazioni che eliminano colonne, tabelle o righe vengono rifiutate e richiedono una procedura dedicata. Le vecchie release e i backup non vengono rimossi in automatico.

## Punti aperti

- Manca una copia dei backup fuori dalla VM.

Le procedure e la cronologia Azure restano in [AZURE_CLOUD.md](AZURE_CLOUD.md) come riferimento storico.
