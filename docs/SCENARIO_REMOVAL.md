# Rimozione dello scenario di test su Azure

Operazione completata il 23 ottobre 2026 su richiesta esplicita dell'utente. Scenario rimosso: `ce014f6d-1a90-4388-b187-771235bcb918`, nome originale conservato come costante Unicode nel servizio di manutenzione. Il collegamento WhatsApp e stato spostato prima su **Capodanno 2027**, `fc3dfb11-fbcf-46e9-b677-9521e1c07f03`.

## Autorizzazioni e disegno

L'utente ha scelto lo spostamento WhatsApp e ha confermato esplicitamente che Dan e Tro dello scenario corrispondono ai partecipanti omonimi gia presenti nell'evento reale. Le anagrafiche sono distinte: le identita WhatsApp vengono associate ai due partecipanti reali confermati, senza creare persone. Lo scenario conteneva dati sintetici e alcune ricevute/interazioni reali; il backup completo privato conserva lo stato precedente.

Brainstorming applicato come intervento circoscritto, secondo l'autonomia richiesta in AGENTS. Nessuna funzione generale di eliminazione eventi aggiunta al portale. Il comando gia presente **Organizzatori > Revoca evento** revoca solo l'assegnazione dell'organizzatore.

## Strumenti e controlli

- `tools/TRobot.WhatsAppTransfer`: orchestration locale alla VM tramite WhatsAppService esistente, con amministratore abilitato e assegnato a entrambi gli eventi, conferma esplicita dei due mapping, gruppo originale e membri attuali verificati. Token acquisito con LoadCredential systemd; esecuzione come proprietario del DB con Web e timer fermi. Nessun Send, nuovo pairing o reinvio. Il target aveva gia quattro alias corrispondenti: riusati solo dopo uguaglianza completa dei due hash per ciascuna persona, rifiutando associazioni diverse/parziali/inattive o aggiuntive.
- `tools/TRobot.ScenarioCleanup` e servizio Application/Infrastructure dedicato: unico UUID e nome ammessi, revisione, amministratore attivo/assegnato/non bloccato, binding sorgente disattivato, conferma CLI, fingerprint completo e ricevuta SHA del backup. Cancellazione transazionale delle sole righe dello scenario con FK ON e vincoli differiti per il ciclo Poll/DecisionOption. Tutte le righe globali e degli altri eventi confrontate prima del commit; rollback su qualsiasi differenza o errore di integrita/FK. People e Identity conservati.
- I due SHA passati alle CLI sono attestazioni della verifica esterna del backup: i programmi ne controllano il formato, il coordinatore verifica realmente gli archivi.

Implementazione delegata per il cleanup e revisore distinto gpt-6.1-sol medium: **APPROVED**, compreso il delta per riusare soltanto alias target perfettamente coincidenti. Build/publish ARM64 puliti, **12/12 test mirati** e **620/620 test .NET**. Test includono rifiuti di ambito/nome/revisione/permessi/binding/fingerprint/conferma, ciclo FK e rollback di un trigger che altera People fuori scope. I dry-run remoti hanno inizialmente rifiutato la destinazione non vuota senza modificare dati; dopo verifica rigorosa delle associazioni coincidenti il preflight e riuscito.

## Backup, rehearsal e applicazione

Primo backup completo prima del trasferimento, SHA-256 `4BFC1B305174796673036E2B5BA0A61E1D4A2124B77F92463FA181F0FE96BE03`.

La revisione automatica ha rifiutato il download di questo archivio completo sul PC perche include DB, chiavi e sessione e non considerava esplicitamente autorizzata l'esportazione. Il rifiuto e stato rispettato: nessun download alternativo, token e payload rimasti sulla VM. Le operazioni sono proseguite sulla VM con credenziali effimere gestite da systemd.

Dopo il trasferimento verificato, fermati Web/gateway/timer e acquisito nuovo backup coerente, SHA-256 `E7F7C078CD7A47A6AF285ABCAA394D3B591FAFD29D8BB5283FE533689522A1CE`. Archivio e report privati sotto `/root/trobot-provisioning/scenario-cleanup-20261023`, accesso root. Copia di rehearsal nello stesso server: il fingerprint coincide con quello operativo; rimozione sulla copia riuscita con tutte le righe esterne conservate, integrita OK e nessuna violazione FK. Applicazione operativa con identico fingerprint e backup verificato riuscita; un solo evento rimosso. Nessuna migration o modifica allo schema.

## Collaudo finale

Eventi elenca solo Capodanno 2027; il vecchio link non e piu accessibile (Identity reindirizza ad Access/Denied). Evento reale disponibile, **35 partecipanti** conservati, **quattro alias tecnici per Dan/Tro**, gateway connected sul gruppo originale. TLS pubblico, login/logout e Soggiorni verificati. Report privato `.local/azure/event-removal-smoke-report.json`.

Backup live successivo riuscito e recovery automatico del nuovo evento riuscito, Result=success/ExecMainStatus=0. Web, gateway e timer attivi. Nessun messaggio di prova inviato. Le presenze e gli altri dati specifici dello scenario non sono stati copiati sul reale; restano nel backup storico privato. Tutti i dati preesistenti fuori dallo scenario sono stati verificati identici durante la cancellazione.

La manutenzione e conclusa: non rilanciare il seeder sul DB operativo e non ripetere la cancellazione. Un eventuale ripristino richiede considerare le nuove modifiche operative successive, prima di sostituire un database.
