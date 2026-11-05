# Membri WhatsApp gia associati - rilascio Azure 5 novembre 2026

Incremento del commit `f90a536`: nell'elenco dei membri del gruppo il portale distingue gli identificativi con almeno un alias attivo nell'evento. I membri da associare restano in alto, quelli gia associati sono raggruppati in fondo ed evidenziati in verde quando selezionati. Flag calcolato in Application dagli hash esistenti; nessuna modifica a autorizzazioni, gateway, modello o regole di salvataggio. Motivazioni in `docs/DECISIONS.md`.

## Verifiche locali

- `dotnet build TRobot.sln --no-restore`: zero errori e avvisi.
- `dotnet test TRobot.sln --no-build --no-restore`: 663/663 positivi.
- Publish Release framework-dependent `linux-arm64` successivo all'ultima modifica dei sorgenti; `whatsapp.js` e `site.css` pubblicati identici ai sorgenti.

## Rilascio Azure

Web attivo in `releases/member-associations-20261105-arm64`; gateway conservato in `releases/m34-20261031-arm64`. Procedura identica al rilascio precedente solo Web (`docs/MENU_PROPOSAL_STAY.md`): upload con verifica hash, staging con controllo dei membri dell'archivio, arresto di timer/gateway/Web, backup privato coerente sulla VM, cambio atomico del link `current`, confronto di tutte le righe delle 56 tabelle, integrita e foreign key, configurazione, chiavi, identita e associazione al gruppo invariate, recovery della sessione e gateway `connected` sul gruppo autorizzato. Rollback automatico previsto in caso di errore, non necessario. Nessuna migrazione, ripristino di DB storici o invio reale di prova.

Pacchetto Web: 224 file, 9.131.094 byte, SHA-256 `84ad85e4f59f50a122a37815fa169a431a068985318cd035699e8eea9762592c`. Esclusi database, chiavi e sessioni. Contesto e backup restano sulla VM: `/root/trobot-provisioning/member-associations-20261105-arm64-context.json`. Script privati `.local/azure/prepare-member-associations-release.py`, `member-associations-release.py`, `run-member-associations-release.py`, `smoke-member-associations-release.py`.

Collaudo pubblico con TLS verificato dopo l'attivazione: login 200 dall'IP Azure, redirect HTTP 308, NRestarts=0 per Web e gateway, 12 pagine riservate, otto GET/POST ai moduli sospesi 404, dieci POST senza antiforgery rifiutati 400, asset corrispondenti ai sorgenti, 48 controlli browser desktop/mobile e quattro senza JavaScript positivi, nessun errore JavaScript o HTTP, logout verificato. Report privato `.local/azure/member-associations-release-smoke-report.json`. L'evidenziazione dei membri associati e coperta dai test automatici; la verifica visiva sull'elenco reale del gruppo resta all'utente.
