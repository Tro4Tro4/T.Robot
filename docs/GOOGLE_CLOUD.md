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

## Punti aperti

- `trobot.service` e `trobot-whatsapp.service` risultano `disabled`: dopo un riavvio della VM (per esempio per manutenzione Google) non ripartirebbero da soli. Su Azure erano abilitati. Abilitarli richiede una decisione esplicita.
- Manca una copia dei backup fuori dalla VM.
- La procedura di rilascio va adattata dagli script Azure a x64 e `gcloud`/SSH: upload con hash, staging, backup coerente, rehearsal delle migrazioni, cambio atomico di `current`, recovery del gateway e smoke pubblico.

Le procedure e la cronologia Azure restano in [AZURE_CLOUD.md](AZURE_CLOUD.md) come riferimento storico.
