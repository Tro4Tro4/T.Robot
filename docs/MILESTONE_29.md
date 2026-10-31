# M29 — Primo hosting Oracle riservato

L'utente autorizza esplicitamente la configurazione autonoma del server e del database, superando il precedente limite al solo prototipo locale per questo sito. Il gateway WhatsApp reale resta locale: nessun collegamento di produzione o trasferimento della sessione è incluso.

## Disegno e piano

1. Abilitare Production solo con opt-in e configurazione completa: TLS esplicito, host ammessi, database SQLite esistente con percorso assoluto e chiavi Data Protection persistenti. Conservare il comportamento Development. Escludere bootstrap demo/Identity, scheduler simulato e gateway in Production. Implementatore dedicato, test e revisione indipendente prima del rilascio.
2. Pubblicare `linux-x64` framework-dependent dal PC, fuori dalla VM da 1 GB. Pacchetto privo di database, file locali e credenziali. Rilascio root-only in scrittura, servizio `trobot` senza login, limiti di risorse systemd.
3. Ottenere una snapshot consistente con l'API SQLite backup, verificare integrità, foreign key e impronte delle righe di tutte le tabelle. Trasferire via SSH in staging riservato e installare senza sovrascrivere database server esistenti. Dati, Identity, storico e associazioni rimangono invariati. Il DB server è una copia indipendente: nessuna sincronizzazione con quello locale, e il gateway locale continua a scrivere solo sul PC.
4. Avviare in HTTPS e verificare autenticazione, accessi anonimi negati, pagine dello scenario, assenza di gateway/scheduler e persistenza al riavvio del servizio. Se non viene fornito un dominio, predefinire accesso riservato tramite tunnel SSH e certificato localhost esplicito: nessuna esposizione pubblica in HTTP, nessuna modifica implicita al trust del PC. Pubblicazione con dominio/certificato pubblico richiede disponibilità del nome e relativa configurazione DNS.
5. Predisporre backup SQLite e chiavi, timer giornaliero e prova di ripristino su copia distinta. Backup solo locali alla VM, nessuna cancellazione automatica e nessuna pretesa di protezione dalla perdita dell'intera VM.

## Verifiche previste

Build e suite .NET dopo la modifica; test mirati alle configurazioni Production valide/invalide e ai cookie HTTPS. Nessuna migrazione business prevista. Collaudo Linux sul rilascio effettivo con provider SQLite .NET, controlli browser e autenticazione esistente senza registrare nuove presenze o inviare messaggi reali. Nessuna credenziale o dato personale nei file versionati o nell'output.

## Esito verificato

- Build Release senza errori o avvisi, 28 test nuovi e **552/552** nella suite completa. Debug non ricompilato perché il processo locale mantiene le DLL aperte; nessuna interruzione del gateway o dell'app locale. Implementatore `gpt-6.1-sol` medium e revisore indipendente `gpt-6.1-sol` high, APPROVED.
- Pubblicazione framework-dependent portabile con `UseAppHost=false`, provider nativo SQLite Linux x64 incluso. Nessun database, PFX, chiave o credenziale nel pacchetto. Archivio verificato SHA-256 prima dell'estrazione. Rilascio `/opt/trobot/app/releases/m29`, collegamento `current`, servizio abilitato al boot, runtime `/opt/dotnet/dotnet` 10.0.13. Consumo osservato dopo il collaudo circa 100 MB, nessun riavvio automatico imprevisto.
- Snapshot consistente, trasferimento e ripristino su copia verificati con integrità, foreign key e impronte di tutte le righe delle **45 tabelle**. Identità e storico preservati, nessuna migrazione business o reinizializzazione.
- Listener effettivo solo `127.0.0.1:5443` HTTPS, anche con tentativi di configurare endpoint HTTP tramite `Kestrel__Endpoints__Unexpected__Url` e `ASPNETCORE_URLS`; porte alternative assenti, configurazione di prova rimossa. Certificato esplicito verificato dal client, nessuna modifica al trust globale di Windows. Certificato locale con scadenza **17 gennaio 2027**, da rinnovare o sostituire con certificato pubblico.
- Autenticazione originale, rifiuto anonimi e sei pagine verificate su Linux: dashboard, eventi, soggiorni, nuclei, menu e WhatsApp. Browser desktop/mobile senza errori JavaScript o overflow pagina; nessun invio reale o modifica alle presenze. Sessione mantenuta dopo riavvio del servizio e logout verificato.
- Backup systemd verificato sia ad app ferma sia attiva; sidecar WAL/SHM appartenenti a `trobot`, modalità 0600. Timer giornaliero circa 03:30 UTC con ritardo casuale fino a 15 minuti, backup root-only senza cancellazioni automatiche. Copia iniziale esportata sul PC, trasferimenti futuri fuori VM non automatici.

## Accesso e limiti

Accesso attuale **https://trobot-capodanno.duckdns.org**, certificato pubblico Let’s Encrypt con rinnovo Caddy automatico, credenziali esistenti in `.local/identity-admin.txt`. Non serve tunnel. Dopo l'apertura porte Oracle, verificati HTTPS con trust di sistema, redirect HTTP 308, rifiuto anonimi, sei pagine autenticate, cookie sicuri, CSRF, desktop/mobile, sessione dopo riavvio proxy e logout. Caddy 2.11.7 installato da archivio ufficiale SHA-512 verificato; configurazione validata e review indipendente APPROVED. Nessuna modifica al codice C# o al DB per il proxy.

Accesso alternativo riservato `https://localhost:5443` tramite `deploy/oracle/Open-Tunnel.ps1 -ServerAddress <IP_VM>`. Qui il certificato localhost è autofirmato: nessuna CA installata nel PC, avviso browser previsto solo per questo accesso alternativo. Il certificato interno scade il 17/01/2027 e richiede rinnovo separato con aggiornamento del trust Caddy; dettagli in `ORACLE_CLOUD.md`.

Non collegare il gateway al nuovo sito finché non viene autorizzato il trasferimento: database server e locale sono copie indipendenti e non si sincronizzano. Il proxy non modifica questi confini.
