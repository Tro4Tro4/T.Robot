# Proposte pasti vincolate al soggiorno - 1 novembre 2026

Su richiesta dell'utente, `!proponi` accetta soltanto giorni del soggiorno personale con presenza confermata. Arrivo e ultimo giorno sono inclusi, come nelle preferenze M34. Il controllo avviene alla scelta della data e nuovamente nella transazione di conferma: un cambio di presenza o date durante il dialogo impedisce il salvataggio e chiude la sessione con un messaggio privato esplicativo.

Conservati limite temporale M33 rispetto alla data locale evento, conferma personale, scadenza assoluta di cinque minuti, deduplicazione e proposte storiche. Nessuna modifica del modello o migrazione.

## Verifiche locali

- `dotnet build TRobot.sln --no-restore`: zero errori e avvisi.
- `dotnet test TRobot.sln --no-build --no-restore`: 662/662 positivi, inclusi 11 nuovi casi.
- Test mirati: 22/22 positivi. Revisione indipendente del codice GPT-6.1 Sol APPROVED.
- Publish Release framework-dependent `linux-arm64` riuscito dopo restore degli asset della piattaforma.

## Rilascio Azure autorizzato

Web attivo in `releases/proposal-stay-20261101-arm64`; gateway conservato in `releases/m34-20261031-arm64`. Backup coerente privato sulla VM con Web e gateway fermi, prima del cambio dei binari. Confrontate tutte le righe del DB prima/dopo avvio Web, integrita e foreign key verificate; configurazione, chiavi, identita e gruppo originale conservati. Recovery della stessa sessione riuscita, servizi e timer backup attivi. Nessuna migrazione, ripristino di DB storici o invio reale di prova.

Procedura revisionata indipendentemente GPT-6.1 Sol APPROVED. Controlli offline positivi su sintassi, DB sintetico, staging ARM64 e rifiuto di quattro archivi non ammessi. Script privati: `.local/azure/proposal-stay-release.py`, `run-proposal-stay-remote.py`, `prepare-proposal-stay-release.py`. Contesto e backup restano sulla VM: `/root/trobot-provisioning/proposal-stay-20261101-context.json`.

Pacchetto Web: 224 file, 9.122.548 byte, SHA-256 `34f53b513516fcc5ef241c91cae4e0d28ebfce509702236d317fcca0061e63a1`. Esclusi database, chiavi e sessioni. Gateway locale, Oracle e scheduler Production invariati.

Collaudo pubblico concluso con TLS verificato e sessione originale conservata: 12 pagine riservate, otto GET/POST ai moduli sospesi 404, dieci POST senza antiforgery rifiutati 400, asset corrispondenti ai sorgenti, 48 controlli browser desktop/mobile e quattro senza JavaScript positivi. Nessun errore JavaScript o HTTP, logout verificato. Report privato `.local/azure/proposal-stay-release-smoke-report.json`. Il nuovo controllo e verificato con test sintetici; nessun messaggio reale dimostrativo inviato.
