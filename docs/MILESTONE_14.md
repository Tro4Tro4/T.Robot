# Milestone 14 — Messaggi e scheduler locali

## Obiettivo e disegno

Sperimentare il flusso organizzatore → bozza → anteprima → conferma → coda → esito con trasporto interamente simulato. Nessun gateway, rete, AI o job automatico. Il coordinatore integra configurazione, mapping e migrazione in sequenza; un agente differente esegue la review.

Il modulo usa OutboundMessage e BotSettings in Domain, MessageService/SchedulePolicy in Application, configurazioni EF in Infrastructure e pagine MVC Messaggi. Le operazioni verificano assegnazione attiva e account abilitato dentro la transazione condivisa; audit ed entità sono atomici. Testo semplice codificato, 4000 caratteri normalizzati, nessuna cancellazione dello storico, revisione per le pagine obsolete, idempotenza della creazione e dei successivi tick.

## Regole e default

- Stati: Draft, Queued, Sent, Cancelled. Solo bozza modificabile; anteprima e checkbox obbligatoria per accodamento/annullamento. Sent indica esclusivamente un esito simulato. L'invio effettivo non è implementato.
- Orario locale convertito in UTC nel fuso dell'evento; ora ambigua o inesistente al cambio DST rifiutata. Programmazione vuota significa ora attuale per nuove bozze o bozze future; una bozza già dovuta conserva l'orario, così i salvataggi ripetuti sono innocui. Un tick manuale confermato processa solo messaggi in coda già dovuti.
- Pausa evento blocca tutti gli invii e generazioni. Silenzio predefinito 23–09, attraversamento mezzanotte supportato; inizio=fine disabilita il silenzio. Messaggi operativi superano silenzio e limite; riepiloghi/intrattenimento condividono massimo tre invii spontanei per giorno locale (configurabile 0–3). Sarcasmo 0–10, default 5, memorizzato come preferenza senza generazione AI.
- Frequenza riepiloghi rispetto alla data locale evento: oltre 30 giorni ogni 7 giorni; 30–15 ogni 4; 14–7 ogni 2; 6–1 ogni giorno; giorno evento e dopo nessun riepilogo, anche per quelli rimasti in coda. L'intervallo trascorso è misurato in giorni da 24 ore UTC dall'ultimo esito simulato.
- I riepiloghi generati contengono esclusivamente conteggi di presenze, attività aperte, sondaggi aperti e menu pronto. Nessun nome, dato personale, testo libero evento, dettaglio contabile o saldo. Il fingerprint usa solo questi aggregati: se invariato, nessun riepilogo. Un riepilogo già in coda impedisce copie; la memoria dell'ultimo riepilogo cambia solo al suo esito Sent.
- Nessuna lettura di messaggi del gruppo. Il compositore è esplicito e riservato agli organizzatori; un avviso ricorda di non includere dati personali o saldi nel testo destinato al gruppo.

## Verifiche predisposte

Integrazione completata: review indipendente positiva, build con zero errori e avvisi e 248 test superati nella suite completa. Migrazione `SimulatedOutbox` applicata al database locale; confronto delle tabelle precedenti invariato, integrità e chiavi esterne OK.

Test di dominio, confini frequenza, silenzio a cavallo della mezzanotte, limite giornaliero nel fuso evento, pausa, operativi esenti, programmazione e DST, deduplica, riepiloghi immutati e coda, conservazione storico, autorizzazione e account disabilitato, revisione configurazione, rollback su errore audit, autenticazione/anti-CSRF/anteprima codificata. Risultati effettivi riportati dal coordinatore dopo integrazione e review indipendente.

Per provare: aprire Messaggi dall'evento; comporre e salvare una bozza; verificare l'anteprima; confermare e accodare; tornare a Messaggi e confermare «Simula scheduler e invio». Il riepilogo può restare in coda durante il silenzio o se il limite giornaliero è esaurito.

## Contratto per sondaggi successivi

Il sondaggio conserva un riferimento al messaggio di pubblicazione dello stesso evento. La conferma accoda il messaggio; i voti diventano ammessi soltanto dopo che il servizio sondaggi ha osservato `Sent` con `SentAtUtc` valorizzato. Lo scheduler non apre direttamente sondaggi e un messaggio annullato non conferma la pubblicazione.

La suite include regressioni HTTP per 4000 caratteri dopo normalizzazione e salvataggi equivalenti, programmazione futura svuotata e riepiloghi generati immutabili. Build, test e migrazione vengono eseguiti sequenzialmente dal coordinatore.
