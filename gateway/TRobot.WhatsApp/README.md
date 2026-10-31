# T.Robot — gateway WhatsApp sperimentale

Gateway non ufficiale, riservato al numero dedicato e al gruppo autorizzato. Node 24 LTS portabile, dipendenze fissate e lockfile; nessun servizio Windows installato. Web e gateway attuali sono M34 su Azure: [rilascio e verifiche](../../docs/MILESTONE_34.md). Le pagine seguenti descrivono l'avvio locale, non necessario sull'ambiente operativo. Non avviare una seconda copia locale sulla stessa identita.

Il coordinatore prepara `.local/whatsapp/gateway.json` e `.local/node`. Avvio da PowerShell:

```powershell
./gateway/TRobot.WhatsApp/start-gateway.ps1
```

L'avvio apre soltanto HTTP su `127.0.0.1:5240`. Collegamento, QR, gruppo e associazioni si gestiscono dalla pagina riservata del sito; il gateway non si collega da solo all'avvio. Lo scollegamento conserva la sessione autenticata senza eseguire logout; dopo una nuova connessione va riattivato il gruppo. Un riavvio perde sempre il gruppo ammesso.

Tutte le API richiedono il bearer locale, anche health e QR. Non copiare token, QR, auth state o numeri nei file versionati. Lo stato sperimentale `useMultiFileAuthState` rimane esclusivamente in `.local`; non è una soluzione di autenticazione per produzione. Il logger Baileys è silenzioso e il processo sopprime le console delle dipendenze: libsignal include chiamate dirette che possono esporre intere sessioni. La diagnostica propria usa solo messaggi tecnici fissi.

Dal gruppo attivato vengono passati al sito soltanto i comandi reali previsti (`!presenza` e le sue varianti, `!aiuto`, `!menu`, `!todo`, `!riepilogo`, `!porto`, `!prendo`, `!fatto`, `!conferma`, `!proponi`, `!preferenze`) per mittenti associati. Restano esclusi storico, allegati, invio dei vecchi messaggi simulati e collegamento allo scheduler locale. I soli dialoghi privati ammessi sono quelli avviati nel gruppo da `!proponi` e `!preferenze`, verso il mittente verificato; il secondo consente a un genitore di indicare separatamente le preferenze dei figli collegati. Nessun DM arbitrario o avvio autonomo da messaggio privato.

Gli invii reali richiedono un ID operazione e il gruppo attivo. Il journal salva solo HMAC del payload e metadati tecnici, con `pending` persistito prima della chiamata esterna. Reinvii identici restituiscono l'esito precedente; la rilettura di `pending` restituisce `uncertain` senza riscrittura o nuovo invio automatico. Non è possibile garantire exactly-once su un trasporto esterno: gli esiti incerti richiedono controllo umano.

Test senza account o rete WhatsApp:

```powershell
& ./.local/node/node-v24.22.0-win-x64/node.exe --test ./gateway/TRobot.WhatsApp/test/*.test.js
```

I test usano trasporto finto, porte HTTP casuali loopback e directory temporanee; nessun test apre una sessione WhatsApp o invia messaggi reali.
