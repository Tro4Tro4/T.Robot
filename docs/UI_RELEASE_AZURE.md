# Rilascio grafica e usabilita su Azure

Completato il 23 ottobre 2026 su richiesta esplicita dell'utente, dopo il raffinamento descritto in [UI_REFINEMENT.md](UI_REFINEMENT.md).

## Release

- Host operativo Azure ARM64 gia esistente, senza modifiche a risorse o costi.
- Nuova release `/opt/trobot/app/releases/ui-20261023-arm64`, attivata tramite sostituzione atomica del collegamento `current`.
- Precedente release `releases/m32-arm64` conservata per il rollback dei soli binari. Non ripristinare automaticamente vecchie copie del database.
- Publish Release `linux-arm64`, framework-dependent, `UseAppHost=false`, senza errori o avvisi.
- Archivio da 8.975.568 byte, 220 file, senza database, chiavi private o secret. SHA-256: `922d55a6bfd685f40829205496de1d21b6dcb73fe7ff0adea42644b66f866129`.

## Conservazione e attivazione

Preflight con verifica della chiave host SSH gia fissata: Web, Caddy, gateway e timer attivi; release precedente attesa. Checksum, piattaforma e contenuto del pacchetto verificati anche sulla VM prima dell'attivazione.

Breve sospensione coordinata del timer, gateway e Web, quindi backup consistente sulla VM con procedura esistente. Conservati database, chiavi Data Protection, stato e configurazione WhatsApp e copia della configurazione Web, con permessi privati. Backup rimasto esclusivamente sulla VM in `/var/backups/trobot`; nessun download di dati, sessioni o chiavi.

Nessuna migrazione, reinizializzazione o sostituzione del DB operativo. Dopo l'avvio della nuova release, confrontati conteggio e SHA-256 di tutte le righe di ciascuna tabella con il baseline precedente, prima di riattivare il gateway: tutti identici. Verificati integrita SQLite, assenza di violazioni FK e migrazione M32 gia presente. Scenario precedentemente eliminato ancora assente.

Configurazione Web/gateway e chiavi Data Protection confrontate per file e rimaste identiche. Identita WhatsApp e associazione autorizzata conservate. Recovery esistente concluso con successo: gateway connesso al gruppo originale, timer ripristinato. Nessun pairing, invio di prova o reinvio automatico. Oracle e PC non riattivati.

## Collaudo effettivo

- HTTPS pubblico con verifica TLS, senza override DNS o eccezioni ai certificati.
- Login originale verificato prima del rilascio; stessa sessione ancora valida dopo il cambio release e logout finale riuscito.
- Dieci pagine riservate HTTP 200 e `no-store`; CSS servito identico al sorgente rilasciato.
- Quattro moduli sospesi: otto richieste GET/POST rifiutate con 404; cinque POST Alloggi senza antiforgery rifiutati con 400. Nessuna modifica applicativa usata per il collaudo.
- Chromium su tutte le dieci pagine a 1440 e 390 px: 20 controlli superati, nessun overflow della pagina, errore JavaScript o risposta HTTP di errore. Guide, navigazione corrente, moduli sospesi, dati evento e assegnazioni espandibili verificati. Catture private in `.local/ui/azure-refinement/`.
- Web, gateway, Caddy e timer attivi al controllo finale. Recovery `Result=success`, `ExecMainStatus=0`; `NRestarts=0` per Web e gateway. Circa 451 MiB disponibili, swap inutilizzata al controllo.

Suite locale prima del rilascio: 622/622 test .NET, 40 controlli browser su dati sintetici e detector Impeccable senza segnalazioni. Revisione indipendente dello script di rilascio: **APPROVED** sul sorgente testuale fornito; il revisore non ha eseguito comandi sulla VM. Attivazione e collaudo effettivi eseguiti dal coordinatore.

Materiale e report privati: `.local/azure/ui-release-material.json`, `ui-release.py`, `smoke-ui-release.py`, `ui-release-smoke-report.json`; contesto con baseline e percorso del backup sulla VM in `/root/trobot-provisioning/ui-20261023-context.json`.
