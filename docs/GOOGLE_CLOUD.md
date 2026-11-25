# Google Cloud - hosting T.Robot

**Stato al 25 novembre 2026.** Il 24 novembre l'utente ha spostato T.Robot da Azure a Google Cloud. Il trasferimento è stato fatto fuori da questo repository, che ne conserva solo i materiali privati in `.local`. Google è la nuova fonte operativa.

## Fatti verificati

- `trobot-capodanno.duckdns.org` risolve `34.29.22.143`; `GET /Access/Login` risponde 200 con TLS valido. Le pagine riservate reindirizzano al login.
- Materiale privato del 24 novembre, non versionato: `.local/publish-x64` (Web .NET framework-dependent x64), `.local/gcp-bundle.tar.gz`, `.local/gcp-staging` (DB, chiavi, configurazione Web/WhatsApp, stato Caddy, unit systemd) e `.local/restore-20261124` (backup Azure con manifest e `migrate.sql`).
- La build x64 è successiva all'ultima modifica di `src`/`gateway` (commit `415a79b`, M35). Molto probabilmente quindi la VM esegue già il codice corrente; va confermato sulla VM.

## Da completare

- Progetto, zona, nome e tipo della VM; utente e chiave SSH o accesso tramite `gcloud`; regole firewall.
- Percorsi delle release, unit attive, timer di backup, recovery del gateway e connessione al gruppo originale.
- Una procedura di rilascio equivalente a quella Azure, adattata a x64: upload con hash, staging, backup coerente, rehearsal delle migrazioni, cambio atomico di `current`, recovery del gateway e smoke pubblico.
- Stato di Azure: VM ferma, deallocata o eliminata. Non riattivarla senza richiesta esplicita.

Le procedure e la cronologia Azure restano in [AZURE_CLOUD.md](AZURE_CLOUD.md) come riferimento storico.
