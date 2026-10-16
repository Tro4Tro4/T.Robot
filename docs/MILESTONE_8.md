# Milestone 8 — Storico per evento

Estensione circoscritta degli audit e dei moduli esistenti. Lo storico riunisce ricevute delle presenze, audit evento/partecipanti e soli audit di gestione accessi riferiti all'evento. Non richiede modifiche allo schema.

Application autorizza l'organizzatore a ogni richiesta e valida filtri e pagina. Infrastructure esegue join, filtro e paginazione in SQLite. Web mostra orari nel fuso dell'evento con offset esplicito, nomi attuali, attore, persona e transizioni di stato. Collegamenti da dashboard e scheda evento.

Limiti scelti: 25 righe per pagina, categorie Tutte/Presenze/Evento/Accessi. Ordinamento stabile anche con timestamp uguali. Non vengono esposti dati tecnici dei messaggi o audit globali degli account. Lo storico riporta solo gli audit raccolti dalle milestone che li hanno introdotti.

Verifiche previste: filtri e paginazione fra sorgenti, orari estate/inverno, nomi corretti, operazioni invariate, esclusione di dati tecnici/altri eventi/audit globali, account disabilitati e revoche con cookie già aperto.

Esiti: build con 0 errori/avvisi, **146 test superati**, modello EF allineato e nessuna migrazione necessaria. Test con timestamp uguali fra tutte le sorgenti e paginazione senza duplicati su dati stabili. La proiezione SQL usa valori testuali per preservare gli enum convertiti dalle diverse tabelle. Smoke test sull'app locale: login, storico della presenza precedente, filtro evento vuoto, dati tecnici esclusi e logout; processo arrestato.
