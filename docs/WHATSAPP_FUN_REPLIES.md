# Risposte simpatiche a comandi usati male - 8 novembre 2026

Richiesta dell'utente: a un comando esistente usato male (es. `!presenza bho`) rispondere in modo simpatico rimandando al manuale, con una frase scelta a caso; poi frasi diverse per ogni tipologia di comando.

## Riduzione di ambito

La prima richiesta comprendeva anche i comandi sconosciuti (`!` seguito da testo qualsiasi). Su indicazione dell'utente ("se incasina troppo lasciamo perdere") e stata abbandonata: il gateway inoltra al Web solo i comandi noti, quindi avrebbe richiesto di modificare e rilasciare anche il gateway, con piu traffico e rischio di alimentare lo spam gia presente nel gruppo. I comandi sconosciuti restano ignorati come prima; gateway invariato.

## Comportamento

- Solo nel gruppo autorizzato e solo da mittenti associati, con gli stessi controlli dei comandi esistenti (connessione, gruppo, alias unico, timestamp). Messaggi privati, `!annulla`, risposte dei dialoghi, `!proponi`/`!preferenze` invariati.
- `!presenza` senza argomento o con argomento non valido (prima ignorato in silenzio): frase dal gruppo presenza, poi "Uso corretto: !presenza si | forse | no | modifica" e "Dettagli: !aiuto presenza". Nessun cambio di stato.
- Risposte "ℹ️ Sintassi" gia esistenti di `!riepilogo`, `!menu`, `!todo`: frase dal gruppo del comando, riga vuota, sintassi invariata. `!aiuto` con voce errata e le sintassi di `!porto`/`!prendo`/`!fatto`/`!conferma` restano invariati.
- Scelta riproducibile: fingerprint del messaggio modulo 10 dentro il gruppo del comando. Stesso messaggio, stessa frase; ricevuta `WhatsAppCommandReceipt`, duplicato idempotente e conflitto su testo diverso come gli altri comandi informativi.
- Anti-spam: al massimo una frase ogni 5 minuti per persona e 6 ogni 10 minuti per evento, contando le frasi inviate tramite audit `FunReplySent` dentro la stessa transazione. Oltre il limite: `!presenza` errato riceve risposta gestita senza testo ne reazione (ricevuta salvata, il gateway non invia nulla); la sintassi arriva senza frase.
- Nessuna migrazione; nessun testo chat conservato; testi sotto 2000 caratteri e senza U+2014.

## Testi - comando usato male, per tipologia

Aggiornamento dell'8 novembre 2026 su richiesta dell'utente: frasi diverse per ogni comando, dieci per tipologia, scelta riproducibile all'interno del gruppo del comando.

### `!presenza` (argomento mancante o non valido)

1. 🧳 La tua valigia ha sentito "!presenza" ed è corsa alla porta. Poi ha letto il resto ed è tornata nell'armadio.
2. 🗓️ Ci sei, non ci sei, ci sei forse? Il mio calendario quantistico non regge l'incertezza.
3. 🤖 Il comando l'ho riconosciuto, ma il resto l'hai scritto in elfico antico.
4. 🎓 Facoltà di Comandologia, esame di Presenza I: bocciato con simpatia. Si ripete subito.
5. 🚪 Ho aperto il registro delle presenze e mi ha risposto "eh?". Anche lui.
6. 🦾 Per conquistare il Capodanno mi serve un sì, un forse o un no. Le poesie le leggo dopo.
7. 📡 Segnale di presenza ricevuto, significato perso fra la Terra e Marte.
8. 🧭 Sei a metà strada fra il sì e il no, ma il navigatore non trova la via.
9. 🐧 Persino i pinguini rispondono con un semplice sì o no. Fidati, li ho intervistati.
10. 🎟️ Il buttafuori del Capodanno vuole una risposta chiara, non un indovinello.

Dopo la frase: "Uso corretto: !presenza si | forse | no | modifica" e "Dettagli: !aiuto presenza".

### `!riepilogo` (sintassi errata)

1. 🧮 Ho provato a contare con questi parametri e la calcolatrice ha chiesto le ferie.
2. 📊 I miei grafici a torta si sono trasformati in grafici a frittata.
3. 🔢 Conto gli umani da mesi, ma questa data non esiste nemmeno nel calendario dei Maya.
4. 🤖 Errore di conteggio: mi sono contato due volte e sono andato in crisi d'identità.
5. 📅 Quel giorno non l'ho trovato. Ho cercato anche fra i giorni avanzati dell'anno scorso.
6. 🧠 Il mio processore statistico ha fatto "bip", poi "bop", poi ha smesso di parlarmi.
7. 📈 Le statistiche non mentono, ma con questi parametri non dicono proprio niente.
8. 🦾 Sono un robot conquistatore, non un indovino: dammi un numero di pagina vero.
9. 🗂️ Ho sfogliato tutti i fascicoli: questa pagina è stata mangiata dal cane del server.
10. 🎲 Ho tirato i dadi per indovinare cosa intendevi. È uscito "riprova".

### `!menu` (sintassi errata)

1. 🍝 Il cuoco robot ha letto la tua richiesta e ha bruciato la pasta per lo stupore.
2. 🧑‍🍳 Questa pagina del menu non esiste: forse l'ha mangiata qualcuno di notte.
3. 🍕 Ho cercato quel piatto ovunque, anche nel freezer dei sogni.
4. 🥘 Il menu ha poche pagine, ma la tua fame sembra infinita.
5. 🍰 Richiesta non riconosciuta. Il dolce però si riconosce sempre: prima finisci il primo.
6. 🔥 I fornelli dei miei circuiti si sono spenti per la confusione.
7. 🥖 Ho impastato la tua richiesta, ma non è lievitata.
8. 🍷 Il sommelier robot consiglia: un numero di pagina, servito a temperatura ambiente.
9. 🧂 Manca un pizzico di sintassi. E forse anche di sale.
10. 🍽️ Tavolo per uno, comando per nessuno. Riproviamo?

### `!todo` (sintassi errata)

1. 🧹 Ho cercato quella pagina di incarichi, ma la scopa robot l'ha già spazzata via.
2. 📋 La mia lista delle cose da fare ora include "capire cosa hai scritto".
3. 🛠️ Attrezzi pronti, istruzioni no. Anche i robot leggono il manuale, ogni tanto.
4. 🐌 Il mio assistente lumaca sta ancora cercando questa pagina. Partito martedì.
5. 🧾 Ho stampato il tuo comando: la stampante si è rifiutata per motivi etici.
6. 🏗️ Cantiere aperto, ma la gru non trova questa pagina.
7. ✅ Sono bravissimo a spuntare le caselle, ma questa casella non esiste.
8. 🦾 Gli incarichi li distribuisco io, ma prima mi serve una pagina vera.
9. 🧺 Questa richiesta è finita nel cesto del bucato insieme ai calzini spaiati.
10. ⏰ Ho messo la sveglia per eseguire il tuo comando. Suonerà quando avrà senso.

Per `!riepilogo`, `!menu`, `!todo`: frase + riga vuota + testo "ℹ️ Sintassi" esistente invariato.

## Stato

Implementato nel branch di sessione l'8 novembre 2026, solo Web (gateway invariato), nessuna migrazione. Build 0 avvisi, 695/695 test .NET (SDK 10.0.114 su copia senza global.json), gateway 60/60 con Node 22 (Node 24 non disponibile nell'ambiente). Revisione indipendente in sola lettura APPROVED. Facoltativi non applicati: test aggiuntivi per "!presenzaxyz", "Pagina inesistente" ed "Elenco in attesa"; indice composito sugli audit solo se il volume crescera; "Pagina inesistente" resta senza frase; due frasi presenza citano Capodanno. Nessun rilascio senza richiesta esplicita.
