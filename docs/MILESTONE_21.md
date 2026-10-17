# Milestone 21 — Sondaggi e quiz dal simulatore

## Disegno

Incremento del flusso locale esistente, autorizzato in autonomia dopo la verifica della milestone 20. Nessun gateway o invio reale, nessuna nuova migrazione prevista.

| Comando | Comportamento |
| --- | --- |
| `!sondaggi [pagina]` | Elenco paginato dei sondaggi pubblicati, aperti e non scaduti |
| `!sondaggio <riferimento>` | Titolo, opzioni, scadenza e stato di un sondaggio già pubblicato |
| `!vota <riferimento> <numero>` | Voto modificabile fino alla scadenza/chiusura, con conferma |
| `!quiz` oppure `!quiz lista [pagina]` | Elenco paginato dei quiz pubblicati ancora aperti |
| `!quiz <riferimento>` | Domanda, opzioni, scadenza e stato; nessuna soluzione o classifica |
| `!rispondi <riferimento> <numero>` | Unica risposta definitiva del mittente aderente, con conferma |

Riferimenti brevi della milestone 20, GUID completo compatibile e rifiuto dei riferimenti ambigui. Elenchi limitati a cinque elementi; dettaglio limitato dal dominio a dieci opzioni. Bozze e pubblicazioni ancora in coda non vengono esposte come contenuto pubblicato. Il dettaglio di elementi già pubblicati ma chiusi/scaduti rimane consultabile.

Le risposte sono costruite da campi espliciti: nessuna serializzazione dei modelli riservati, nessun nome, voto individuale, soluzione corretta, punteggio o classifica. I titoli e le opzioni rimangono contenuto locale e non vengono accodati dal router.

Il voto resta affidato a PollService, la risposta a GameService. L'adesione ai giochi deve essere stata registrata volontariamente nel modulo Giochi: il comando non iscrive implicitamente nessuno. Si riusano PollVoteReceipt e QuizAnswer con l'identificativo deterministico v1; nessuna nuova ricevuta duplicata o testo chat conservato. Collisioni con presenze e impegni vengono rifiutate in entrambi i versi. I reinvii storici identici restano innocui anche dopo chiusura o ritiro dai giochi, previa autorizzazione corrente dell'organizzatore.

## Verifiche

- Review indipendente positiva; nessuna nuova migrazione, modello EF allineato.
- Build Debug senza errori o avvisi. Test mirati sondaggi/quiz, impegni e regressione del simulatore: 69/69 superati.
- Suite completa: 365/365 superati, nessuno ignorato (`.local/test-results/milestone21.trx`).
- Coperti pubblicazione/scadenza/chiusura e confine esatto della deadline, riferimenti ambigui anche fra righe nascoste, paginazione, adesione volontaria, risposta unica, cambio voto, reinvii dopo ritiro/chiusura, autorizzazioni revocate/disabilitate, attore/mittente diversi, collisioni incrociate e concorrenza, rollback dopo salvataggio e HTTP anti-CSRF/codifica HTML.
- Dettaglio massimo con domanda Unicode di 200 caratteri e dieci opzioni di 150: roundtrip POST/TempData/GET riuscito con header Set-Cookie sotto 16 KB. Nessuna soluzione, posizione corretta, classifica o risposta individuale nei risultati del simulatore.
- Baseline di dati e credenziali originali conservata. Collaudo browser degli incrementi sulla copia SQLite al termine della milestone 22.
