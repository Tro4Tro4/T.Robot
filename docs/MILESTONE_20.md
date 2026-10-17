# Milestone 20 — Menu e attività operativi nel simulatore

## Disegno e regole

Incremento autorizzato il 17 ottobre 2026, con sviluppo e review indipendente. Si estende il flusso locale MVC esistente, senza gateway o invii reali.

- `!menu [pagina]` e `!todo [pagina]`: prima riga con conteggi, poi elenco di massimo cinque elementi con riferimenti stabili, titolo e quantità/stato. Paginazione necessaria anche per limitare il risultato conservato dal provider TempData a cookie. Non si mostrano responsabili, descrizioni, note o saldi. Questi titoli sono locali e non vengono copiati in outbox.
- Riferimenti di otto cifre esadecimali dal GUID: validi solo nel tipo e nell'evento autorizzato. Un riferimento ambiguo viene rifiutato; il GUID completo rimane utilizzabile. La risoluzione considera anche righe chiuse/annullate, per non spostare un riferimento verso un altro elemento al cambio di stato.
- `!porto <voce>` assegna al mittente una voce da organizzare; non cambia quantità, titolo o note e non sostituisce altri incaricati. Voci pronte o annullate escluse.
- `!prendo <attività>` prende in carico un'attività aperta non assegnata o già propria; `!fatto <attività>` completa solo un'attività aperta assegnata al mittente. Nessuna riapertura o sostituzione di incaricati.
- Nuovi impegni consentiti solo ad adulti confermati, verificati nella transazione; bambini classificati in un nucleo attivo esclusi. Conferma esplicita nel modulo del simulatore. La proprietà tecnica precedente `ConfirmVote` conserva la compatibilità dei form, con etichetta ora riferita all'operazione.

Application gestisce regole e autorizzazioni; le entità Domain gestiscono le invarianti. Ogni operazione conserva una ricevuta tecnica `SimulationActionReceipt` con evento, attore, persona, azione, target e timestamp UTC, senza testo chat. Ricevuta, mutazione e audit sono atomici. Anche un nuovo impegno già proprio conserva la ricevuta senza incrementare inutilmente la revisione dell'elemento.

I reinvii identici non ripristinano stati precedenti; account e assegnazione dell'attore sono sempre rivalidati. Cambi di attore, mittente, azione o target con lo stesso ID sono conflitti. Si preserva l'hash v1 delle ricevute voto e si verificano collisioni con presenze e voti precedenti. La serializzazione fra tipi diversi del router resta limitata al singolo processo locale; le revisioni e le transazioni proteggono gli elementi anche fra scope distinti.

## Persistenza

Migrazione incrementale `20261017022511_SimulationActions`: aggiunge soltanto ricevute e indici. FK restrittive verso evento, attore e partecipante dello stesso evento; il target polimorfico, come quello dell'audit, è validato per tipo/evento dal servizio applicativo. Nessuna modifica a identità, credenziali, presenze o storico precedente.

Backup consistente prima degli incrementi in `.local/trobot-before-milestone20.db`; collaudo delle scritture su `.local/trobot-increments-smoke.db` o SQLite di test. File locali esclusi da Git.

## Verifiche

- Review indipendente positiva, compresa verifica della paginazione aggiunta durante la review.
- Build Debug: zero errori e avvisi. Test mirati `OperationalCommandTests` e `SimulationCommandTests`: 36/36 superati.
- Suite completa: 332/332 superati, nessuno ignorato (`.local/test-results/milestone20.trx`). Coperti impegni propri, stati conclusi, adulti/bambini/assenti, riferimenti ambigui/estranei, collisioni tra comandi, no-op con nuove ricevute, attore diverso sui reinvii, concorrenza fra scope, rollback dopo SQL, POST/CSRF e codifica HTML.
- Migrazione applicata e verificata prima sulla copia SQLite; modello EF senza modifiche pendenti. La baseline dei dati originali e il file credenziali restano invariati. Verifica browser degli incrementi prevista sulla copia al termine delle tre milestone.
