# Milestone 13 — Menu e bevande locali

Obiettivo: consentire agli organizzatori autorizzati di pianificare cibo e bevande per evento, senza servizi esterni o comunicazioni reali.

Il modello conserva nome (150 caratteri), categoria cibo/bevande/altro, quantità decimal positiva fino a 1000000 con tre decimali, unità libera (30 caratteri), responsabile facoltativo dell'evento, disponibilità e note (2000 caratteri). Lo stato assegnato richiede il responsabile; pronto può essere privo di responsabile per acquisti o preparazioni comuni. L'assegnazione non conferma la partecipazione e non altera presenze o ricevute.

Le voci non si eliminano: l'annullamento richiede conferma esplicita e conserva identità e storico. Le modifiche successive usano revisione ottimistica; i reinvii della creazione con stesso identificativo e contenuto sono innocui. Cambiamenti e audit vengono salvati nella stessa transazione con verifica di account abilitato e assegnazione evento corrente. I dati sono testo semplice codificato dalle viste MVC; tutti i POST richiedono token anti-CSRF.

Componenti: `MenuItem`, `MenuService`, configurazione EF dedicata, controller MVC e vista Menu/Index. `MenuService` è registrato nella DI; la configurazione EF è attiva e rilevata da ApplyConfigurationsFromAssembly. Il collegamento Menu e bevande è nella scheda evento. Nessuna cancellazione o modifica ai dati precedenti.

Test attivi: quantità/enum/stringhe, coerenza responsabile e stato, accesso ad eventi estranei, reinvii, revisione obsoleta, annullamento confermato anche in creazione, audit atomico e pipeline HTTP con autenticazione/CSRF. La collisione di un identificativo su un altro evento gestibile non consente di modificare o duplicare la voce.

Review indipendente positiva dopo la correzione del limite note con CRLF. Build con zero errori e avvisi, suite completa con 219 test superati. Migrazione `MenuPlanning` applicata al database locale; dati precedenti invariati, integrità e chiavi esterne OK.
