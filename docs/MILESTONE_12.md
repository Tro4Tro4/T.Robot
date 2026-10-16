# Milestone 12 — Nuclei e accompagnatori

## Disegno

Il sito riservato agli organizzatori raggruppa persone già partecipanti dello stesso evento, anche senza mittente WhatsApp. Un nucleo ha nome obbligatorio fino a 150 caratteri. Una persona appartiene al massimo a un nucleo attivo per evento. La classificazione adulto/bambino è relativa all'evento: senza appartenenza attiva il default è adulto.

Un accompagnatore può riferirsi solo a un membro adulto primario attivo dello stesso nucleo. Auto-riferimenti, cicli e catene sono esclusi; prima di rimuovere o riclassificare un primario occorre scollegare i suoi accompagnatori. Nessuna identità, presenza o ricevuta viene modificata.

Le membership vengono disattivate, mai eliminate. Correzioni, rimozioni e riattivazioni richiedono conferma esplicita. Revisioni del nucleo e della membership proteggono dalle pagine obsolete; identificativi stabili del form rendono riconoscibili i reinvii. Servizi Application autorizzati, transazioni, audit atomico, POST anti-CSRF e testo codificato seguono l'infrastruttura comune.

## Verifiche previste

Validazioni Domain; partecipanti dello stesso evento; unicità fra nuclei; riferimenti accompagnatore; conferme e revisioni; account disabilitati/accessi revocati; rollback dell'audit; flussi HTTP e anti-CSRF. Integrazione EF e migrazione sequenziale sono affidate al coordinatore dopo review indipendente.

## Esito dell'integrazione

Review indipendente positiva dopo aver chiuso la riaggiunta che aggirava la riattivazione confermata. Build con zero errori e avvisi, suite completa con 206 test superati. Migrazione `Households` applicata al database locale; confronto delle 13 tabelle precedenti invariato, integrità e chiavi esterne OK. Backup consistente in `.local/trobot-before-prototype-completion.db`. Nessuna modifica a credenziali o dati demo.
