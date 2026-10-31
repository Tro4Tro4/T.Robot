# M30 — Server come ambiente operativo unico

Autorizzazione esplicita del 19 ottobre 2026: l'utente conferma il trasferimento anche del gateway WhatsApp sulla VM Oracle. Development locale rimane disponibile per sviluppo.

## Disegno

- SQLite remoto unica fonte operativa. Confronto coerente prima del passaggio; nessuna sostituzione se identico, nessuna sovrascrittura di modifiche divergenti.
- Production con opt-in esplicito. Gateway HTTP solo 127.0.0.1:5240; callback HTTPS localhost:5443 con CA e hostname verificati. Scheduler Production escluso.
- Trasferire dopo arresto locale tutta la sessione e il journal mantenendo il token esatto per conservare associazioni HMAC e protezione dai reinvii.
- Node con utente dedicato, codice protetto e stato privato; configurazione fuori Git. Avvio disconnesso; recovery riapre solo sessione già registrata e gruppo ancora autorizzato nel DB, senza mutazioni o invii.
- Backup SQLite/chiavi e, con gateway fermo, sessione, journal e configurazione. Riavvio anche in caso di errore; backup conservati.
- Build/test e review indipendente APPROVED prima del deploy. Gateway PC fermato prima di collegare il remoto; configurazione operativa locale disabilitata per evitare doppie istanze.

## Verifiche

Suite .NET/Node, test recovery/backup senza rete reale, systemd, TLS pubblico e callback, confronto DB e hash del trasferimento, stato collegato/gruppo senza identificativi nei log. Riavvio e backup con recupero del collegamento. Nessun messaggio di prova; collaudo dei comandi dal telefono a cura dell'utente.

## Ripristino

Fermare prima il gateway remoto; conservare i dati correnti e ripristinare insieme release, configurazione, sessione/journal e DB dal backup verificato. Non accendere il PC con DB obsoleto o due copie della sessione. Rinnovare il certificato interno prima del 17/01/2027 anche per Node.

## Stato

Build Release senza errori/avvisi, 565 test .NET e 42 Node superati. Test operativi: 24 superati e uno non eseguibile su Windows, tutti e 25 superati su Linux. Review indipendente APPROVED, incluso il correttivo per riconoscere le credenziali Baileys rc14: il login dipende da `me` e dall'identità account, mentre il vecchio flag `registered` può essere falso.

Confronto finale delle 42 tabelle operative identico fra PC e server: DB remoto conservato senza sostituzione. PC fermato, configurazione Development con gateway disabilitato; sessione/token/journal trasferiti via SSH e manifest byte per byte verificato. Snapshot locale privato in `.local/oci/cutover-m30`; backup remoto pre-migrazione e primo backup esteso creati prima della connessione. Rilascio m30 e servizi installati; gateway collegato e gruppo già autorizzato ripristinato effettivamente, recovery concluso con successo e login pubblico HTTPS 200.

**Collaudo interrotto:** subito dopo il collegamento, prima del test di riavvio/backup con gateway attivo, la VM smette di rispondere a HTTPS e all'handshake SSH. Causa non ancora verificata; nessuna prova che sia memoria, CPU o rete. Tentativi SSH con timeout anche per fermare il gateway non riusciti. Console da tool non disponibile e nessuna configurazione OCI API locale; richiesto all'utente il riavvio dalla console Oracle. Non dichiarare l'ambiente stabile o la milestone completata. Il PC resta spento come ambiente operativo per evitare sessioni concorrenti. Alla ripresa: fermare subito `trobot-whatsapp`, leggere metriche/log kernel e cgroup, correggere sulla base delle misure, quindi verificare TLS/callback, riavvio e backup completo. Nessun messaggio di prova inviato.

## Aggiornamento del 23 ottobre 2026

Dopo un nuovo blocco SSH/HTTPS e il reboot effettuato dall'utente, gateway fermato subito e rilascio M31 completato. Riavvio Web, sessione di accesso persistente, backup completo con gateway fermo e successivo backup live con recovery del gruppo autorizzato verificati. Gateway connected, quattro servizi/timer attivi e abilitati, NRestarts=0 e cgroup senza OOM rilevati dal boot corrente. Nessun gateway PC riattivato e nessun messaggio di prova inviato. Dettagli del rilascio in MILESTONE_31.md.

La causa dei blocchi precedenti rimane non verificata: journal del boot precedente assente e nessuna evidenza sufficiente per attribuirli a memoria, CPU o rete. Non e stato eseguito un ulteriore reboot completo dopo M31; la diagnosi di stabilita resta aperta e M30 non viene dichiarata completata.


## Nuova indisponibilita dopo M32 - 2026-10-23 13:58:03 +02:00

Utente segnala portale irraggiungibile. Verifica dal PC: DNS 129.152.13.90, HTTPS timeout di connessione dopo 10 secondi e SSH timeout durante banner exchange. Nessun comando remoto di diagnosi eseguito e nessun servizio modificato. Il collaudo positivo del rilascio non attesta stabilita nel tempo. Necessario accesso console per raccolta evidenze e arresto temporaneo gateway; causa ancora non verificata. Evitare reboot prima dei log se la console risponde.
