# Aiuto WhatsApp per voci dinamiche - 8 novembre 2026

Richiesta dell'utente: rendere `!aiuto` piu mirato. `!aiuto` da solo spiega come chiedere la spiegazione delle voci attive; `!aiuto presenza` (e le altre voci) danno la spiegazione specifica. Scelta dell'utente: elenco dinamico, cosi le voci future compaiono da sole quando diventano utilizzabili.

## Voci

| Voce | Alias accettati | Quando e attiva | Contenuto |
| --- | --- | --- | --- |
| `presenza` | `presenze` | sempre | `!presenza si/forse/no/modifica`, date, domanda famiglia anche per forse/no, `!annulla`, 15 minuti, scadenza adesioni se impostata |
| `riepilogo` | `riepiloghi` | sempre | `!riepilogo [pagina]`, `!riepilogo gg/mm [pagina]` |
| `pasti` | `pasto`, `piatti`, `proponi`, `preferenze` | sempre (`!proponi` sempre disponibile) | `!proponi` (dialogo privato, presenza e date confermate); `!preferenze` solo se esiste un ciclo di voto in stato `Open`, altrimenti indica che sara disponibile all'apertura del voto |
| `elenchi` | `elenco`, `menu`, `todo`, `impegni`, `porto`, `prendo`, `fatto` | solo se almeno un elenco Menu o Todo e approvato con il fingerprint corrente (stessa verifica usata da `!menu`/`!todo`) | solo i comandi degli elenchi approvati: Menu -> `!menu`, `!porto`; Todo -> `!todo`, `!prendo`, `!fatto`; `!conferma` entro cinque minuti |

## Regole

- `!aiuto`: messaggio corto con le sole voci attive, una riga ciascuna, e la scadenza adesioni se impostata.
- `!aiuto <voce>`: spiegazione della voce, confronto senza distinzione maiuscole/minuscole.
- Voce sconosciuta, non attiva o argomenti in eccesso: "voce non trovata o non attiva" seguito dall'elenco corto. Nessuna informazione su elenchi non approvati.
- Calcolo dello stato a ogni richiesta, nessun dato nuovo salvato e nessuna migrazione. Errori nel calcolo di un elenco (es. elenco troppo lungo) rendono la voce non attiva.
- Risposte entro 2000 caratteri, nessun dato personale, nessun U+2014.
- Gateway invariato: il filtro accetta gia `!aiuto` con argomenti.

## Stato

In sviluppo nel branch di sessione, insieme alle modifiche presenze dell'8 novembre ancora non rilasciate.
