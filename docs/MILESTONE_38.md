# M38 - Rubrica comune e associazione automatica tra eventi

Disegno e stato dell'implementazione del 28 novembre 2026. Stato: implementata nel repository, non rilasciata e non provata dal telefono.

## Obiettivo

Con più eventi attivi sullo stesso numero WhatsApp, le associazioni tra membri del gruppo e partecipanti devono confluire in una rubrica comune. Se una coppia nome-numero e' gia' stata definita in un evento, la stessa coppia deve poter essere riconosciuta anche negli altri eventi.

La rubrica e' l'unione distinta di tutte le coppie nome-numero definite dalle associazioni attive di tutti gli eventi. I partecipanti restano distinti per evento: un'associazione creata in A non autorizza comandi in B e non importa persone.

## Comportamento

- **Automatico all'attivazione:** quando un gruppo viene attivato, il bot legge i membri dal gateway e applica le coppie della rubrica. L'errore del gateway o un conflitto non annulla l'attivazione; il pulsante nel pannello permette di riprovare.
- **Pulsante "Associa da rubrica":** nella pagina WhatsApp dell'evento, per un gruppo attivo. Richiede conferma esplicita e la revisione corrente del collegamento. Applica le stesse regole dell'automatico e mostra il conteggio di associati, gia' associati, non riconosciuti, ambigui, senza partecipante e in conflitto.
- **Coppia completa:** un membro viene associato solo se il nome visualizzato nel gruppo e il numero corrispondono entrambi a una coppia della rubrica. La sola uguaglianza del numero o del nome non basta.
- **Nome normalizzato:** il confronto ignora maiuscole/minuscole, spazi iniziali/finali e spazi ripetuti. Non e' una ricerca approssimata: nomi diversi non coincidono.
- **Destinatario:** il partecipante deve essere gia' presente nell'evento, con lo stesso nome normalizzato. Non si importano persone. Se nell'evento ci sono due partecipanti con lo stesso nome, l'associazione e' ambigua e viene saltata.
- **Conflitti:** se uno degli identificativi del membro e' gia' associato a un'altra persona nell'evento, il membro viene saltato. La rubrica non sovrascrive mai un'associazione esistente.
- **Revoca:** un'associazione disattivata non contribuisce piu' alla rubrica. Se la stessa coppia resta attiva in un altro evento, la rubrica puo' riattivare l'associazione revocata nell'evento di destinazione; l'entita' viene ricaricata tracciata prima della modifica.

## Perche' non c'e' una nuova tabella

La rubrica e' calcolata dalle associazioni attive esistenti: per ogni `WhatsAppIdentityAlias` attiva si legge il nome della persona collegata e l'impronta HMAC dell'identita' WhatsApp. Questo:

- evita di duplicare dati e di aggiungere una migrazione;
- rispetta la regola esistente di non persistere l'etichetta WhatsApp: l'etichetta del membro viene usata solo in memoria durante il confronto;
- mantiene allineata la rubrica quando un'associazione viene revocata o un partecipante viene rinominato;
- non introduce numeri telefonici in chiaro nel database.

Il pannello mostra, per i soli partecipanti dell'evento corrente, il nome e il numero di identita' distinte (numero e LID possono produrre piu' impronte per lo stesso membro). I nomi di partecipanti di altri eventi non vengono esposti; la rubrica completa e' usata solo dal servizio per il confronto.

## Schema e migrazioni

Nessuna modifica al modello EF e nessuna migrazione. `IWhatsAppStore` acquisisce la sola lettura `ActiveContactsAsync`, implementata con una join tra alias attivi e persone. Il controllo `dotnet ef migrations has-pending-model-changes` deve restare pulito.

## File interessati

- `src/TRobot.Application/WhatsApp/WhatsAppRubrica.cs`
- `src/TRobot.Application/WhatsApp/WhatsAppService.cs`
- `src/TRobot.Application/WhatsApp/Contracts.cs`
- `src/TRobot.Infrastructure/Persistence/WhatsAppStore.cs`
- `src/TRobot.Web/Controllers/WhatsAppController.cs`
- `src/TRobot.Web/Models/WhatsAppModels.cs`
- `src/TRobot.Web/Views/WhatsApp/Index.cshtml`
- `tests/TRobot.Tests/WhatsAppRubricaTests.cs`

## Verifiche

- Build della soluzione senza errori o avvisi.
- Suite .NET completa: 742 test superati, 0 falliti (5 nuovi).
- Test nuovi: associazione automatica all'attivazione con nomi uguali ma persone distinte per evento; solo nome o solo numero non bastano; partecipante assente dall'evento; nome duplicato nell'evento; conflitto con un'associazione esistente; conferma e revisione; associazioni disattivate escluse; riattivazione di un'associazione revocata nell'evento di destinazione; rubrica visibile solo a chi gestisce il dispositivo.
- `dotnet ef migrations has-pending-model-changes`: nessuna modifica di modello pendente, nessuna migrazione.
- Non provato dal telefono ne' in un browser.

## Limiti

- Per i gruppi gia' attivi prima di questa funzionalita' la rubrica non viene applicata da sola: serve il pulsante una volta per evento.
- L'attivazione automatica dipende dal gateway disponibile subito dopo l'attivazione; in caso di errore resta il pulsante.
- Il pannello non mostra i numeri in chiaro, solo il conteggio delle impronte distinte per nome.
- La rubrica non crea partecipanti e non modifica le persone esistenti.
