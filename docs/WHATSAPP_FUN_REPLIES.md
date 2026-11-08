# Risposte simpatiche a comandi sconosciuti o sbagliati - 8 novembre 2026

Richiesta dell'utente: a un comando sconosciuto (`!` seguito subito da testo) rispondere in modo simpatico che il comando non esiste; a un comando esistente usato male (es. `!presenza bho`) rispondere in modo simpatico rimandando al manuale. Una decina di testi per caso, scelto a caso. Liberta lasciata sui testi.

## Riduzione di ambito (8 novembre 2026)

Su indicazione dell'utente ("se incasina troppo lasciamo perdere") il riconoscimento dei comandi sconosciuti `!testo` e stato abbandonato: avrebbe richiesto di modificare e rilasciare anche il gateway, aumentato il traffico verso il Web e il rischio di alimentare lo spam. Restano solo le risposte ai comandi esistenti usati male; gateway invariato. Le regole 2 e 3 e i testi per comando sconosciuto sotto sono conservati solo come traccia del disegno iniziale e non sono implementati.

## Stato di partenza verificato

- Il gateway Node (`gateway/TRobot.WhatsApp/src/gateway.js`) inoltra nel gruppo solo i comandi noti: `!pizza` viene scartato prima del Web. Serve quindi una modifica al gateway, da rilasciare insieme al Web.
- `!presenza bho` (o `!presenza` da solo) arriva al Web ma viene ignorato in silenzio (`AttendanceService.Parse` fallisce e `IncomingAsync` restituisce `new(false)`).
- `!riepilogo`, `!menu`, `!todo` con argomenti errati rispondono gia con un messaggio "ℹ️ Sintassi"; `!aiuto` con voce errata risponde "Voce non trovata".

## Regole

1. Solo nel gruppo autorizzato e solo da mittenti associati (stessi controlli dei comandi esistenti). I messaggi privati non cambiano.
2. **Comando sconosciuto:** testo che inizia con `!` seguito subito da una lettera (`^![\p{L}][\p{L}\p{N}]*`), comando non fra quelli gestiti. `! ciao`, `!!!`, `!123` restano ignorati. Il nome del comando citato nella risposta e solo il token, ripulito e troncato a 20 caratteri; il resto del testo non viene mai ripetuto.
3. **Suggerimento:** se il token dista al massimo 2 modifiche da un comando esistente (token di almeno 4 lettere), si aggiunge "🤔 Forse intendevi !xxx?".
4. **Comando esistente usato male:** `!presenza` senza argomento o con argomento non valido riceve una frase simpatica + uso corretto + rimando a `!aiuto presenza`. Le risposte "ℹ️ Sintassi" gia esistenti di `!riepilogo`, `!menu`, `!todo` ricevono in testa una frase simpatica, mantenendo il testo di sintassi preciso. `!aiuto` con voce errata resta invariato.
5. **Scelta casuale riproducibile:** indice = fingerprint del messaggio modulo il numero di testi. Stesso messaggio, stessa risposta (idempotenza e retry del gateway coerenti), messaggi diversi distribuiti a caso; nei test la scelta e deterministica.
6. **Anti-spam:** al massimo una risposta simpatica ogni 5 minuti per persona e al massimo 6 ogni 10 minuti in tutto il gruppo. Oltre il limite: nessuna risposta per i comandi sconosciuti e per `!presenza` errato; per i comandi con sintassi esistente si invia la sola sintassi senza frase simpatica. Il gateway conserva il proprio limite di 6 input al minuto per mittente.
7. Ricevuta `WhatsAppCommandReceipt` e audit come gli altri comandi informativi; nessun testo chat conservato.
8. Testi sotto 2000 caratteri, nessun dato personale, nessun U+2014.

## Testi - comando sconosciuto (`{c}` = token ripulito)

1. 🤖 Comando !{c} non trovato nei miei circuiti. Sarà implementato nella versione "mai", subito dopo la versione "neanche per sogno".
2. ⚡ !{c}? Le forze del male hanno bloccato questo comando. Io ci ho provato, ma loro hanno più biscotti.
3. 🛸 !{c} è stato rapito dagli alieni durante i test. Non è più tornato e non ha lasciato recapiti.
4. 📟 ERRORE 404: !{c} non esiste. Ho cercato anche sotto il divano, ho trovato solo briciole di panettone.
5. 🦾 Il mio piano di conquista del mondo non prevede !{c}. Proposta respinta dal Consiglio Supremo dei Robot.
6. 🔮 Ho consultato il futuro: !{c} arriverà il 31 febbraio, alle 25:00. Segnatelo.
7. 🧠 Ho elaborato !{c} per 0,0003 secondi e ho deciso che non mi piace. Il robot ha sempre ragione.
8. 🚧 !{c} è in costruzione da quando i dinosauri navigavano col modem a 56k.
9. 🔌 Ho provato a eseguire !{c} ed è saltata la corrente in tre condomini. Meglio di no.
10. 👾 !{c}? Bel tentativo, umano. I miei creatori non mi hanno dato questo potere. Ancora.

Dopo il testo: eventuale suggerimento, poi la riga "📖 I comandi che conosco: !aiuto".

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

## Stato di partenza verificato

- Il gateway Node (`gateway/TRobot.WhatsApp/src/gateway.js`) inoltra nel gruppo solo i comandi noti: `!pizza` viene scartato prima del Web. Serve quindi una modifica al gateway, da rilasciare insieme al Web.
- `!presenza bho` (o `!presenza` da solo) arriva al Web ma viene ignorato in silenzio (`AttendanceService.Parse` fallisce e `IncomingAsync` restituisce `new(false)`).
- `!riepilogo`, `!menu`, `!todo` con argomenti errati rispondono gia con un messaggio "ℹ️ Sintassi"; `!aiuto` con voce errata risponde "Voce non trovata".

## Regole

1. Solo nel gruppo autorizzato e solo da mittenti associati (stessi controlli dei comandi esistenti). I messaggi privati non cambiano.
2. **Comando sconosciuto:** testo che inizia con `!` seguito subito da una lettera (`^![\p{L}][\p{L}\p{N}]*`), comando non fra quelli gestiti. `! ciao`, `!!!`, `!123` restano ignorati. Il nome del comando citato nella risposta e solo il token, ripulito e troncato a 20 caratteri; il resto del testo non viene mai ripetuto.
3. **Suggerimento:** se il token dista al massimo 2 modifiche da un comando esistente (token di almeno 4 lettere), si aggiunge "🤔 Forse intendevi !xxx?".
4. **Comando esistente usato male:** `!presenza` senza argomento o con argomento non valido riceve una frase simpatica + uso corretto + rimando a `!aiuto presenza`. Le risposte "ℹ️ Sintassi" gia esistenti di `!riepilogo`, `!menu`, `!todo` ricevono in testa una frase simpatica, mantenendo il testo di sintassi preciso. `!aiuto` con voce errata resta invariato.
5. **Scelta casuale riproducibile:** indice = fingerprint del messaggio modulo il numero di testi. Stesso messaggio, stessa risposta (idempotenza e retry del gateway coerenti), messaggi diversi distribuiti a caso; nei test la scelta e deterministica.
6. **Anti-spam:** al massimo una risposta simpatica ogni 5 minuti per persona e al massimo 6 ogni 10 minuti in tutto il gruppo. Oltre il limite: nessuna risposta per i comandi sconosciuti e per `!presenza` errato; per i comandi con sintassi esistente si invia la sola sintassi senza frase simpatica. Il gateway conserva il proprio limite di 6 input al minuto per mittente.
7. Ricevuta `WhatsAppCommandReceipt` e audit come gli altri comandi informativi; nessun testo chat conservato.
8. Testi sotto 2000 caratteri, nessun dato personale, nessun U+2014.

## Testi - comando sconosciuto (`{c}` = token ripulito)

1. 🤖 Comando !{c} non trovato nei miei circuiti. Sarà implementato nella versione "mai", subito dopo la versione "neanche per sogno".
2. ⚡ !{c}? Le forze del male hanno bloccato questo comando. Io ci ho provato, ma loro hanno più biscotti.
3. 🛸 !{c} è stato rapito dagli alieni durante i test. Non è più tornato e non ha lasciato recapiti.
4. 📟 ERRORE 404: !{c} non esiste. Ho cercato anche sotto il divano, ho trovato solo briciole di panettone.
5. 🦾 Il mio piano di conquista del mondo non prevede !{c}. Proposta respinta dal Consiglio Supremo dei Robot.
6. 🔮 Ho consultato il futuro: !{c} arriverà il 31 febbraio, alle 25:00. Segnatelo.
7. 🧠 Ho elaborato !{c} per 0,0003 secondi e ho deciso che non mi piace. Il robot ha sempre ragione.
8. 🚧 !{c} è in costruzione da quando i dinosauri navigavano col modem a 56k.
9. 🔌 Ho provato a eseguire !{c} ed è saltata la corrente in tre condomini. Meglio di no.
10. 👾 !{c}? Bel tentativo, umano. I miei creatori non mi hanno dato questo potere. Ancora.

Dopo il testo: eventuale suggerimento, poi la riga "📖 I comandi che conosco: !aiuto".

## Testi - comando usato male

1. 🤖 Il comando l'ho riconosciuto, ma il resto l'hai scritto in elfico antico.
2. 📖 Il manuale esiste, giuro. Si chiama !aiuto e non morde.
3. 🧐 Rilevo un comando giusto con parametri molto creativi. Apprezzo l'arte, ma non la capisco.
4. 🎓 Facoltà di Comandologia, primo anno, primo esame: ripassiamo insieme?
5. ⚙️ Ingranaggi inceppati: il comando c'è, ma scritto così non gira.
6. 🦾 Anche un robot conquistatore ha bisogno di istruzioni chiare. Riprova, umano.
7. 📡 Segnale ricevuto, significato perso nello spazio profondo.
8. 🤯 Mi è andato in crash un neurone di silicio. Era il mio preferito.
9. 🧩 Il pezzo del puzzle è quello giusto, ma l'hai messo al contrario.
10. 🐢 Respira, leggi le istruzioni e poi conquistiamo il mondo insieme.

Per `!presenza` dopo la frase: "Uso corretto: !presenza si | forse | no | modifica" e "Dettagli: !aiuto presenza". Per i comandi con sintassi esistente: frase + riga vuota + testo "ℹ️ Sintassi" invariato.

## Stato

In sviluppo nel branch di sessione, solo Web (gateway invariato). Nessun rilascio senza richiesta esplicita.
