# TRobot.AttendanceRestore

Strumento monouso del 25 novembre 2026. Serve a ricostruire presenze e soggiorni persi con il DB Azure; l'utente l'ha autorizzato in via eccezionale, solo per questa ricostruzione.

Riproduce il codice dei servizi: per lo stato `OrganizerAttendanceService.SetAsync`, con ricevuta `Organizer`; per le date la conferma del soggiorno di `WhatsAppAttendanceService`, con `AttendanceStay`, `AttendanceStayChange` e audit. Tutto viene attribuito all'organizzatore: mittente e attore sono l'organizzatore, l'azione di audit è `StayRestored`. Nessun messaggio WhatsApp viene inviato.

- Evento ammesso: solo `fc3dfb11-fbcf-46e9-b677-9521e1c07f03`.
- Chi appartiene a un nucleo attivo vale sempre per tutto il nucleo (regola dell'organizzatore); i bambini non possono essere indicati da soli.
- Date ammesse solo per i confermati: da 1 a 31 giorni, arrivo entro 31 giorni dall'inizio dell'evento.
- Senza `--apply` mostra solo l'anteprima e il fingerprint. `--apply <fingerprint>` applica tutto in una transazione e si rifiuta di procedere se dati o piano sono cambiati dopo l'anteprima.

```bash
# piano JSON: [{"name": "Tro", "status": "Confirmed", "arrival": "2026-12-30", "days": 4}, {"name": "Lino", "status": "Absent"}]
dotnet TRobot.AttendanceRestore.dll /var/lib/trobot/trobot.db piano.json <utente-organizzatore>
dotnet TRobot.AttendanceRestore.dll /var/lib/trobot/trobot.db piano.json <utente-organizzatore> --apply <fingerprint>
```

Sulla VM si esegue come utente `trobot`, dopo un backup (`systemctl start trobot-backup.service`). Il piano con i nomi resta in `.local/gcp/restore`, che non è versionato.
