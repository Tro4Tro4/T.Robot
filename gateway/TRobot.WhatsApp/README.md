# T.Robot — gateway WhatsApp sperimentale

Gateway locale non ufficiale, riservato al numero dedicato e al gruppo di prova. Node 24 LTS portabile, dipendenze fissate e lockfile; nessun servizio Windows o deploy.

Il coordinatore prepara `.local/whatsapp/gateway.json` e `.local/node`. Avvio da PowerShell:

```powershell
./gateway/TRobot.WhatsApp/start-gateway.ps1
```

L'avvio apre soltanto HTTP su `127.0.0.1:5240`. Collegamento, QR, gruppo e associazioni si gestiscono dalla pagina riservata del sito; il gateway non si collega da solo all'avvio. Lo scollegamento conserva la sessione autenticata senza eseguire logout; dopo una nuova connessione va riattivato il gruppo. Un riavvio perde sempre il gruppo ammesso.

Tutte le API richiedono il bearer locale, anche health e QR. Non copiare token, QR, auth state o numeri nei file versionati. Lo stato sperimentale `useMultiFileAuthState` rimane esclusivamente in `.local`; non è una soluzione di autenticazione per produzione. Il logger Baileys è silenzioso e il processo sopprime le console delle dipendenze: libsignal include chiamate dirette che possono esporre intere sessioni. La diagnostica propria usa solo messaggi tecnici fissi.

Solo messaggi nuovi `!presenza` del gruppo attivato vengono passati al sito. Niente DM, storico, allegati, invio dei vecchi messaggi simulati o collegamento allo scheduler locale. Una reazione generica può confermare una presenza elaborata; non vengono pubblicati nomi, voti o saldi individuali.

Gli invii reali richiedono un ID operazione e il gruppo attivo. Il journal salva solo HMAC del payload e metadati tecnici, con Pending persistito prima della chiamata esterna. Reinvii identici restituiscono l'esito precedente; un Pending recuperato dopo riavvio diventa Uncertain e non viene ritentato automaticamente. Non è possibile garantire exactly-once su un trasporto esterno: Uncertain richiede controllo umano.

Test senza account o rete WhatsApp:

```powershell
& ./.local/node/node-v24.22.0-win-x64/node.exe --test ./gateway/TRobot.WhatsApp/test/*.test.js
```

I test usano trasporto finto, porte HTTP casuali loopback e directory temporanee; nessun test apre una sessione WhatsApp o invia messaggi reali.
