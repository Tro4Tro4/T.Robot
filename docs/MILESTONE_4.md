# Milestone 4 — Account degli organizzatori

## Disegno

Sostituire il codice condiviso locale con account individuali ASP.NET Core Identity, username/password e cookie. Identity viene preferito a un provider esterno perché non richiede account, credenziali o configurazioni cloud aggiuntive e si integra con .NET ed EF Core già presenti. Domain resta indipendente; Application decide autorizzazioni e operazioni sugli account, Infrastructure implementa lo store Identity.

Ogni account si collega univocamente a una Person. Il cookie contiene un claim separato con PersonId; le presenze continuano a verificare EventOrganizer nel database. Il super amministratore globale può creare normali organizzatori e assegnarli a un evento: non ottiene automaticamente accesso ai dati degli eventi non assegnati. Non è prevista registrazione pubblica né promozione a super amministratore dalla Web.

Il primo amministratore viene creato solo con comando locale esplicito, se non esistono account. Per il database della demo esistente si riusa esclusivamente il PersonId del vecchio file locale; le vecchie credenziali non sono più accettate dal sito. Una password casuale nuova viene salvata fuori da wwwroot in `.local/identity-admin.txt`, escluso da Git. La preparazione non cancella dati né ruota account esistenti.

L'amministratore dispone di un modulo limitato per creare un organizzatore normale e assegnarlo a un evento. Creazione di persona, account, assegnazione e audit sono atomiche. Ogni utente può cambiare la propria password; il cambio invalida gli altri cookie. Password e hash non vengono inclusi in log, audit o elenco account.

Default: username senza email obbligatoria, password di almeno 12 caratteri con maiuscole/minuscole/cifre/simboli, blocco dopo cinque errori per 15 minuti, cookie di 30 minuti e verifica del security stamp a ogni richiesta. Rimangono anti-CSRF, rate limit e restrizione a Development/loopback. Il deploy richiede ancora configurazione HTTPS e chiavi per l'hosting scelto; recupero password e MFA sono fuori perimetro.

## Piano di esecuzione autonomo

1. Aggiungere store Identity e claim, interfacce/servizi Application e audit minimo.
2. Aggiungere migrazione incrementale, bootstrap amministratore e moduli di accesso, creazione organizzatore e cambio password.
3. Adattare i test HTTP all'accesso reale Identity e verificare due account con eventi distinti, permessi, lockout, cambio password, invalidazione cookie e rollback di creazioni non valide.
4. Applicare la migrazione senza perdere presenze/ricevute della demo; preparare l'amministratore locale, verificare build/test e documentare i comandi.

Il piano segue la modalità autonoma richiesta dall'utente: le scelte predefinite vengono documentate e applicate senza ulteriori approvazioni intermedie.

## Esito verificato il 16 ottobre 2026

Build senza errori/avvisi e 86 test passati. Migrazione `OrganizerIdentity` applicata al DB locale, ripetuta senza modifiche e modello EF allineato. Bootstrap dell'amministratore completato mantenendo l'organizzatore, la presenza e la ricevuta già esistenti. Smoke test HTTP su loopback con vecchio codice rifiutato, nuovo login, accesso alle pagine amministrative e logout. Il processo di verifica è stato arrestato. Le credenziali sono solo in `.local/identity-admin.txt`; nessun dato privato è stato aggiunto ai file sorgente.
