# Roadmap per completare il prototipo locale

## Perimetro confermato

L'utente ha richiesto tutte le milestone residue e ha scelto esplicitamente il prototipo locale: moduli previsti, simulazioni e test. Servizi reali, gateway WhatsApp, provider AI, trasferimenti di denaro e deploy restano esclusi. La precedente limitazione al solo incremento corrente è superata per i moduli locali. Il sito resta esclusivo degli organizzatori.

Richiesta successiva del 17 ottobre: integrazione WhatsApp sperimentale autorizzata su numero dedicato e gruppo di prova. La milestone 23 supera la sola esclusione del gateway locale, mantenendo separati trasporto reale e simulazione. Nessun deploy o servizio Windows; pairing e comandi di presenza reali verificati dall'utente; prova invio operativo dal sito in corso. Vedere [istruzioni](WHATSAPP_LOCAL.md).

Il lavoro viene suddiviso in task sviluppati da agenti dedicati e verificati da agenti diversi, come richiesto dall'utente. Nessun modulo è completato prima di review indipendente, integrazione, build e test. Le migrazioni sono integrate in sequenza dal coordinatore per evitare snapshot concorrenti.

## Sequenza

| Milestone | Risultato verificabile | Stato |
| --- | --- | --- |
| 11 | Attività con responsabile, priorità, stato e scadenza | Review indipendente OK, 195 test superati |
| 12 | Nuclei e accompagnatori; adulto/bambino per evento | Integrata, review indipendente OK, 206 test superati |
| 13 | Menu/bevande con quantità e assegnazioni | Integrata, review indipendente OK, 219 test superati |
| 14 | Messaggi con anteprima, conferma, coda e trasporto simulato; configurazione e scadenze | Integrata, review indipendente OK, 248 test superati |
| 15 | Sondaggi: bozza, approvazione, pubblicazione simulata, voto e chiusura | Integrata, review indipendente OK, 265 test superati |
| 16 | Spese approvate, quote ponderate, dichiarazioni/registrazioni locali e riconciliazione | Integrata, review indipendente OK, 279 test superati |
| 17 | Giochi volontari, quiz e classifica locale | Integrata, review indipendente OK, 292 test superati |
| 18 | Dashboard aggregata, simulazioni complete, documentazione e verifica finale | Completata: review indipendente OK, build 0 errori/avvisi, 296 test superati, smoke autenticato loopback e verifiche DB OK |
| 19 | Comandi `!aiuto`, `!menu`, `!todo`, `!vota` nel simulatore; UI responsive rifinita con Impeccable e Debug VS Code | Review indipendenti OK, build 0 errori/avvisi, 310 test superati; collaudo browser su copia SQLite |
| 20 | Menu/attività paginati, riferimenti brevi, `!porto`, `!prendo`, `!fatto` con ricevute e conferme | Review indipendente OK, build 0 errori/avvisi, 332 test superati |
| 21 | Sondaggi e quiz pubblicati nel simulatore, riferimenti brevi, voto e risposta unica volontaria | Review indipendente OK, build 0 errori/avvisi, 365 test superati |
| 22 | Scheduler automatico locale opt-in per evento, attivatore autorizzato e worker ogni 30 secondi | Completata: review indipendente OK, build 0 errori/avvisi, 44 test mirati e 380 test completi, collaudo browser dei comandi e timer reale su copia riusciti; dati originali conservati |
| 23 | Gateway locale Baileys, QR riservato, gruppo unico, mapping PN/LID, presenza reale e nuovi avvisi con anteprima | Implementazione e review indipendente OK; build 0 errori/avvisi, 411 test .NET e 17 Node superati; presenze reali e isolamento mittenti verificati, anteprima con nome esatto gruppo e blocco bozze obsolete; prova invio operativo dal sito in corso |
| 24 | Aiuto, menu e attività nel gruppo reale; approvazione esplicita dei titoli, ricevute e journal delle risposte | Review APPROVED, build senza avvisi, 421 test .NET e 28 Node superati; nuovi comandi da verificare sul telefono |
| 25 | Impegni reali con proposta e conferma personale entro cinque minuti, riferimenti stabili negli elenchi | Review APPROVED, build senza avvisi, 441 test .NET e 30 Node superati; prova reale dei nuovi impegni da completare |

## Disegno comune

Si mantiene il monolite modulare. Entità Domain con invarianti e revisione; servizi Application con controlli di accesso e regole; store EF SQLite in Infrastructure; controller e viste MVC in Web. Una piccola infrastruttura comune gestisce autorizzazione per evento, transazioni, concorrenza e audit dei moduli. Le regole dei singoli moduli restano in servizi distinti.

Alternative considerate: progetti separati per ogni modulo aggiungerebbero dipendenze e integrazioni non necessarie al prototipo; un controller generico con modifiche arbitrarie renderebbe fragili validazione e permessi. Si scelgono moduli tipizzati nello schema esistente, con input dedicati e POST anti-CSRF.

Tutte le scritture verificano account abilitato, assegnazione attiva all'evento e riferimenti nello stesso evento; operazione e audit sono atomici. Nessuna cancellazione di righe storiche. Dati sintetici solo nei test; nessun riempimento automatico della demo. Le date sono UTC, convertite dal fuso evento. Importi e quantità usano decimal.

Le politiche mancanti saranno esplicitate in DECISIONS prima dell'implementazione: voti individuali per adulti confermati, nessuna delega familiare automatica; bambini distinti nelle quote; nessuna pubblicazione di saldi o dati personali nella coda di gruppo; giochi solo con adesione volontaria.
