# Milestone 22 — Scheduler automatico locale

## Disegno dell'incremento

Il tick manuale e la coda simulata esistenti vengono estesi con un worker locale, solo Development. L'utente ha autorizzato l'automazione del prototipo; gateway, trasporti reali, servizi esterni e deploy restano esclusi.

L'organizzatore attiva l'automazione per singolo evento dalla pagina Messaggi, con conferma esplicita e revisione corrente. La configurazione conserva l'attivatore. Il default è disattivato, anche per eventi già presenti dopo la migrazione. Il controllo avviene ogni 30 secondi e funziona solo mentre l'applicazione è aperta: nessun servizio Windows o task esterno viene installato.

La discovery restituisce candidati, non autorizzazioni. Per ciascun evento il worker apre uno scope distinto; il servizio verifica nella stessa transazione flag, attivatore, account attivo e assegnazione all'evento, quindi riusa il core del tick manuale. Nessuna transazione annidata e nessuna conferma fittizia per singolo tick.

Pausa blocca tutte le elaborazioni. Quiet hours, limite giornaliero, frequenza dei riepiloghi e soppressione delle novità invariate mantengono le regole esistenti. Gli operativi già approvati conservano l'eccezione a silenzio e limite, come nel tick manuale. Un riepilogo può essere accodato durante silenzio/limite e inviato successivamente. Nessuna chiusura automatica di sondaggi/quiz, nessuna nuova iscrizione e nessun contenuto libero generato.

L'automazione rimane sospesa se l'attivatore perde accesso; può riprendere quando lo riottiene in base all'attivazione persistente precedente. Qualsiasi organizzatore ancora autorizzato può disattivarla o riattivarla. La pagina indica lo stato e il requisito dell'applicazione aperta.

I controlli senza effetti non modificano revisione o audit. Errori e conflitti di un evento non fermano gli altri; i conflitti vengono riprovati al ciclo successivo, senza retry dentro la stessa transazione. Arresto cancellabile e TimeProvider per il tempo. I comandi di inizializzazione demo/account non avviano il worker.

## Verifiche

- Review indipendente positiva; corretto durante la review il requisito della revisione obbligatoria sul nuovo POST di configurazione (`BindRequired`). Nessun rilievo residuo.
- Build Debug senza errori o avvisi; 44/44 test mirati scheduler/messaggistica e 380/380 test completi superati, nessuno ignorato (`.local/test-results/milestone22-final.trx`).
- Coperti migrazione default off/null e dati precedenti, conferme/revisioni/accessi, revoca e successiva riattivazione, cambio attivatore e candidati obsoleti, equivalenza tick manuale/automatico per pausa/silenzio/cap/novità/giorno evento, no-op senza audit/revisioni, rollback atomico, scope distinti/errori isolati/cancellazione e concorrenza su SQLite reale. Test HTTP con cookie e anti-CSRF, revisione assente e stato istanza disabilitata. Nei test HTTP generali il timer periodico è disattivato.
- Migrazione `20261017023924_AutomaticLocalScheduler` applicata prima alla copia, poi all'originale; aggiornamento ripetuto senza operazioni. EF conferma modello allineato. SQLite ricostruisce i vincoli di BotSettings con PRAGMA fuori transazione: backup consistente preventivo e controlli post-migrazione eseguiti con integrità/FK OK. Baseline delle 30 tabelle business precedenti e hash del file credenziali conservati.
- Detector Impeccable sulle viste modificate: nessuna segnalazione (`.local/ui/impeccable-increments-detect.json`).

- Collaudo browser della build finale sulla copia SQLite: dieci flussi generali riusciti, 25 verifiche dei comandi/impegni/voti/quiz riuscite e sette verifiche del timer reale riuscite. Attivazione senza conferma rifiutata; operativo inviato senza tick manuale in circa 25 secondi; pausa rispettata per circa 37 secondi, invio dopo ripresa in circa 22 secondi; disattivazione mantiene la nuova comunicazione in coda per oltre 37 secondi. Nessun duplicato o revisione spuria nei controlli senza effetti.
- Messaggi desktop/mobile e sei moduli a 390 px senza overflow globale; screenshot esaminati di risposte menu/sondaggio e automazione attiva/disattivata. Nessun errore JavaScript o HTTP 500 nei collaudi riusciti. Artefatti in `.local/ui/collaudo-report.json`, `increments-commands-report.json` e `automation-report.json`; cleanup dello scheduler sulla copia riuscito.
- Colonne preesistenti delle 30 tabelle business e file credenziali originali invariati. Nessuna automazione attivata e nessuna ricevuta di impegni creata nel database originale; scritture del collaudo limitate alla copia o ai database di test. Backup `.local/trobot-before-milestone20.db` conservato.

Verifiche browser limitate a Chromium headless/emulazione desktop e mobile: nessuna certificazione di accessibilità o prova su dispositivi fisici, nessuna integrazione WhatsApp reale.
