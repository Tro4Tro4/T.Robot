# T.Robot - Consultazione e preferenze pasti

Stato: disegno raccolto nel brainstorming del 31 ottobre 2026. Funzione non implementata. Le scelte concordate sono separate dai default tecnici di questo documento. Nessun rilascio, migrazione operativa o invio reale autorizzato da questa specifica.

## Obiettivo e contesto

Consentire ai partecipanti di consultare i piatti proposti e indicare le proprie preferenze su WhatsApp. Il sito resta riservato agli organizzatori. Le preferenze aiutano a scegliere il menu; non sono ordinazioni, porzioni, incarichi, dichiarazioni di allergie o acquisti.

M33 conserva proposte confermate con giorno, pasto, autore e disponibilita a cucinare. Il 31 ottobre l'utente ha confermato in chat il funzionamento reale di proposta e salvataggio dal telefono. Questa evidenza riguarda M33, non il nuovo voto e non un controllo live dei servizi effettuato dall'agente.

Fonti esistenti: `src/TRobot.Domain/MealProposal.cs`, `src/TRobot.Application/Menu/MealProposalService.cs`, `src/TRobot.Application/WhatsApp/WhatsAppMealProposalService.cs`, `src/TRobot.Domain/Household.cs`, `docs/MILESTONE_33.md`.

## Decisioni concordate con l'utente

1. Piu preferenze per lo stesso giorno e pasto; non una singola scelta vincente.
2. Consultazione e voto in un dialogo privato avviato da un comando nel gruppo autorizzato. Nuova eccezione esplicita ai DM, circoscritta a questa funzione.
3. Raccolta delle proposte e votazione in fasi separate. Elenco fisso durante il voto.
4. Preparazione dal pannello: correggere nomi, escludere piatti sbagliati o non desiderati e accorpare doppioni dello stesso giorno/pasto, conservando gli originali e le disponibilita a cucinare.
5. Voto limitato ai giorni del soggiorno confermato della persona interessata. Non esiste attualmente una presenza distinta per pranzo e cena.
6. Ogni adulto sceglie per se e puo indicare preferenze distinte per i figli esplicitamente collegati a lui; nessuna delega implicita agli altri adulti del nucleo.
7. Preferenze modificabili fino alla chiusura. Riepilogo e conferma prima del salvataggio; deve essere possibile anche azzerare le preferenze di un pasto.
8. Risultati aggregati visibili ai partecipanti anche durante il voto, distinti tra adulti, bambini e totale; nessun elenco pubblico dei votanti.
9. Se cambiano le presenze, conservare le scelte nello storico ma escludere dal conteggio corrente quelle dei giorni senza presenza confermata.
10. Alla chiusura conservare il risultato di quel momento. Il pannello mostra separatamente anche il conteggio ricalcolato sulle presenze correnti per aiutare la preparazione dei pasti.

## Approcci considerati

- Scelta singola per pasto: non adottata, perche l'utente vuole esprimere piu preferenze.
- Valutazione graduata di ogni piatto: non adottata in questo incremento; richiede piu interazioni e non serve alla preferenza multipla scelta.
- Comandi di voto nel gruppo: non adottati; dialogo privato scelto per ridurre il rumore e gestire le scelte personali e dei figli.
- Raccolta e voto simultanei: non adottati, per mantenere lo stesso elenco per tutti.
- Riattivazione del modulo Sondaggi generale: non necessaria al flusso scelto. Default tecnico: mantenere il nuovo voto collegato ai pasti senza riaprire i moduli sospesi.

## Ciclo della raccolta e della votazione

Default tecnico: un ciclo pasti per evento attraversa raccolta, preparazione, voto aperto e voto chiuso. Il pannello rende evidente lo stato e richiede azioni esplicite per passare alla fase successiva.

- Durante la raccolta `!proponi` mantiene il flusso M33.
- Chiudendo la raccolta, le sessioni M33 non ancora confermate non possono aggiungere nuove proposte al ciclo. La conferma ricontrolla lo stato; il bot spiega che la raccolta e chiusa.
- In preparazione gli organizzatori costruiscono le voci da votare. Escludere una proposta non la cancella; ogni accorpamento conserva il collegamento alle fonti.
- L'anteprima prima dell'apertura mostra esattamente i contenuti destinati ai partecipanti. Non include nomi degli autori o dei cuochi; i riferimenti alle disponibilita restano nella regia riservata.
- L'apertura richiede almeno una voce valida. Blocca contenuto e identificativi dell'elenco per la tornata.
- Durante il voto non si aggiungono, rimuovono o rinominano le voci pubblicate. Una correzione sostanziale richiede chiusura della tornata e preparazione di una nuova; le vecchie preferenze non vengono trasferite automaticamente.
- La chiusura blocca nuovi voti e modifiche e salva atomicamente il risultato. Non chiude automaticamente altre attivita o moduli.

Apertura e chiusura manuali dal pannello, nessuna scadenza automatica o messaggio spontaneo. Default: una sola tornata aperta per evento; le tornate chiuse restano consultabili nel pannello e non vengono riaperte nella prima versione. Una successiva raccolta richiede un nuovo ciclo esplicito, conservando i precedenti.

La correzione del nome pubblicato non cambia silenziosamente l'offerta di un autore: se il significato del piatto cambia, il pannello segnala che la disponibilita originaria va ricontrollata. Nessun incarico viene creato.

## Dialogo WhatsApp

Default tecnico per il comando: `!preferenze` nel gruppo autorizzato avvia o riprende il dialogo privato. Non cambia il significato dei comandi gia esistenti. I normali DM non avviano autonomamente il flusso.

Percorso:

1. Selezione della persona: adulto mittente oppure uno dei bambini attivi esplicitamente collegati tramite `CompanionOfMemberId` nel medesimo evento.
2. Scelta del giorno e del pasto; consultazione dell'elenco con riferimenti stabili, conteggi adulti/bambini/totale e preferenze gia salvate per la persona selezionata.
3. Selezione multipla mediante riferimenti, anche attraverso pagine diverse. Prima della conferma e sempre visibile l'insieme completo delle scelte proposte.
4. Riepilogo di persona, giorno, pasto e piatti. `!conferma` sostituisce atomicamente l'insieme delle preferenze per quel giorno/pasto/persona/tornata.
5. `!annulla` scarta soltanto le modifiche non confermate. Un'azione esplicita per nessuna preferenza richiede lo stesso riepilogo e conferma.

Default: sessione persistente di cinque minuti assoluti dall'avvio, senza estensioni o messaggi alla scadenza. Persona, giorno, pasto e selezione pendente appartengono alla sessione; ogni cambio di destinatario azzera la bozza precedente. La ripresa non prolunga la scadenza.

Per il primo incremento M33 e preferenze non hanno due dialoghi contemporanei per lo stesso mittente: se uno e aperto, il bot invita a completarlo o annullarlo prima di iniziare l'altro. Evita che `!conferma` o una risposta numerica vengano interpretati nel flusso sbagliato. Una sessione scaduta non blocca l'altra funzione.

La consultazione puo mostrare tutti i giorni/pasti pubblicati; il salvataggio e disponibile soltanto per quelli in cui la persona selezionata e eleggibile. Il genitore puo gestire un figlio presente anche se il proprio soggiorno non comprende quel giorno. Le classificazioni o i collegamenti mancanti richiedono correzione dal pannello, senza inventare deleghe o classificare automaticamente una persona come adulta.

I risultati mostrati nel dialogo sono provvisori durante il voto. Dopo la chiusura il partecipante consulta il risultato congelato, chiaramente etichettato. Il conteggio operativo aggiornato successivamente resta distinto e riservato al pannello.

Nomi dei figli sono mostrati soltanto al mittente che puo gestirli. Gli elenchi dei piatti e i risultati non contengono nomi di altri votanti, autori, numeri o saldi. Il gruppo riceve al piu la reazione di avvio; nessun invio automatico a ogni preferenza o variazione di conteggio. Qualunque futuro riepilogo nel gruppo richiedera un flusso esplicito separato.

## Regole di voto e conteggio

- Una preferenza binaria per voce/persona/tornata: la stessa persona non aumenta il conteggio scegliendo piu volte lo stesso piatto. Puo scegliere tutte le voci dello stesso pasto o nessuna.
- Il voto di un bambino appartiene al bambino, non al genitore. L'audit registra separatamente chi ha operato. Un cambio di genitore di riferimento non duplica le scelte: il nuovo adulto autorizzato puo modificarle, quello precedente perde tale facolta.
- Elegibilita corrente: partecipazione attiva nell'evento, stato confermato, soggiorno valido che include il giorno del pasto e classificazione attiva adulto/bambino. Il sistema non deduce l'eta dal nome o dall'identita WhatsApp.
- Per ogni voce i conteggi correnti includono soltanto preferenze valide di persone eleggibili; per gli altri casi il pannello espone il motivo di esclusione. Totale uguale ad adulti piu bambini, non numero di schede compilate.
- Default: le scelte non vengono cancellate quando manca l'eleggibilita. Se viene ripristinata, tornano a contare nel conteggio corrente; restano sempre distinguibili dallo snapshot della chiusura.
- Adulto/bambino nel conteggio corrente segue la classificazione attiva. Lo snapshot conserva la classificazione e l'eleggibilita usate alla chiusura, per non riscrivere retroattivamente il risultato.
- Il risultato alla chiusura viene calcolato e salvato nella stessa transazione che chiude la tornata. Il dettaglio tecnico dello snapshot consente di ricostruire i totali senza dipendere dai nomi correnti o dalle presenze future.
- Un conteggio e una misura di gradimento: piu preferenze della stessa persona su piatti diversi non equivalgono a piu porzioni. Nessuna selezione automatica del vincitore o del menu definitivo.

## Architettura prevista

Nuove entita Domain per ciclo/tornata, voci pubblicate collegate alle proposte originali, preferenze e relativo storico, snapshot della chiusura e sessione privata. Nomi definitivi da scegliere nell'implementazione seguendo `TRobot.*`; non sono classi gia presenti.

Application gestisce preparazione, conferme, stato della raccolta, eleggibilita, deleghe ai bambini, salvataggi e conteggi. Riutilizza gli accessi per evento e gli store senza scritture dai controller o dall'AI.

Infrastructure aggiunge mapping e migrazione conservativa; non modifica o cancella le proposte M33 e non dipende dal modulo Sondaggi. Web aggiunge comandi riservati alla regia Proposte pasti per preparazione, anteprima, apertura, chiusura e risultati. Nessuna pagina pubblica per i partecipanti.

Gateway e callback estendono i candidati ammessi al nuovo comando e instradano il dialogo privato alla sessione corretta. Il destinatario rimane derivato dal messaggio verificato. Non introdurre un endpoint per DM arbitrari. Riutilizzare ricevute persistenti, journal, filtri di gruppo, quote, limiti di testo e gestione degli invii incerti senza replay automatico.

Ogni conferma ricontrolla autorizzazioni del collegamento e dell'attivatore, identita mittente, persona gestita, rapporto genitore/figlio, presenze, tornata aperta, revisione dell'elenco e revisione delle preferenze gia salvate. Un riepilogo obsoleto richiede nuova consultazione, senza sovrascrivere scelte piu recenti. Il reinvio identico e innocuo; un identificativo riusato con dati diversi e un conflitto.

## Verifiche richieste prima del rilascio

- Preparazione senza perdita delle fonti: correzione, esclusione e accorpamento limitato allo stesso giorno/pasto; anteprima priva di dati personali.
- Fasi separate: conferma M33 rifiutata dopo chiusura raccolta, elenco immutabile durante il voto, chiusura atomica e rifiuto dei voti tardivi.
- Preferenze multiple, selezione su piu pagine, modifica e azzeramento confermati, nessuna modifica prima della conferma o dopo annullamento/scadenza.
- Isolamento fra eventi, mittenti, canali, tornate, persone e dialoghi M33/preferenze; nessuna ambiguita di `!conferma`.
- Voto personale e per figli collegati; divieto sugli altri adulti e sui bambini non autorizzati, revoca/cambio del rapporto durante una sessione.
- Soggiorni ai confini inclusivi, date mancanti, stato non confermato, cambio soggiorno/classificazione; conservazione dello storico ed esclusione/rientro nei soli conteggi correnti.
- Totali adulti/bambini e snapshot della chiusura stabili dopo cambiamenti successivi, distinto conteggio operativo aggiornato.
- Duplicati, reinvii, concorrenza, ripartenza, rollback dopo errore e conferme obsolete; nessun invio automatico di prova.
- Protezione del pannello, antiforgery, autorizzazione per evento, codifica dei nomi piatto, paginazione e limite UTF-16.
- Migrazione su DB sintetico e copia autorizzata: righe e schemi precedenti conservati, integrita e foreign key valide.
- Build/test .NET, suite Node, controllo del modello EF e review indipendente del codice significativo. Test e review futuri non vanno dichiarati gia eseguiti.

## Consegna e limiti

Sviluppo successivo per incrementi: modello/preparazione; preferenze e conteggi; dialogo privato; integrazione e verifiche. Migrazioni integrate sequenzialmente. Prima di modificare codice leggere `AGENTS.md` e lo stato Git, conservando i lavori altrui.

Menu definitivo, assegnazioni ai cuochi, quantita/spesa, allergie, pesi differenti dei voti, notifiche automatiche e riattivazione dei moduli sospesi sono esclusi da questo incremento. Il collaudo della nuova funzione sul telefono sara distinto da quello M33 gia confermato dall'utente. Deploy e cambiamenti al DB operativo richiedono un successivo incarico esplicito.
