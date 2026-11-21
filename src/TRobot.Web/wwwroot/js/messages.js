(() => {
  const panel = document.getElementById('delivery-live');
  if (!panel) return;
  const gateway = document.getElementById('gateway-state');
  const gatewayDetail = document.getElementById('gateway-detail');
  const connection = document.getElementById('connection-state');
  const connectionDetail = document.getElementById('connection-detail');
  const updated = document.getElementById('delivery-updated');
  const gatewayNames = {
    disabled: 'Disattivato in questa istanza',
    invalid: 'Configurazione non valida',
    unreachable: 'Abilitato, non raggiungibile',
    connected: 'Abilitato, connesso',
    qr: 'Abilitato, in attesa del QR',
    connecting: 'Abilitato, collegamento in corso',
    disconnected: 'Abilitato, dispositivo non collegato'
  };
  function connectionName(status) {
    if (status.gatewayState === 'qr') return 'Da collegare: scansiona il QR dal pannello WhatsApp';
    if (status.gatewayState === 'connecting') return 'Collegamento in corso';
    if (status.gatewayState === 'disconnected') return 'Dispositivo non collegato';
    if (status.gatewayState !== 'connected') return 'Non verificabile: gateway non connesso';
    if (!status.connectionActive) return 'Dispositivo collegato, nessun gruppo attivo per questo evento';
    return status.groupMatches ? 'Attiva, gruppo verificato' : 'Dispositivo collegato, gruppo diverso da quello confermato';
  }
  async function poll() {
    if (document.hidden) return;
    try {
      const response = await fetch(panel.dataset.statusUrl, { credentials: 'same-origin', cache: 'no-store', redirect: 'error' });
      if (!response.ok) throw new Error('status_unavailable');
      const status = await response.json();
      gateway.textContent = gatewayNames[status.gatewayState] || 'Abilitato, stato sconosciuto';
      gatewayDetail.textContent = status.gatewayState === 'unreachable' ? 'Avvia il gateway e riprova.' : '';
      connection.textContent = connectionName(status);
      connectionDetail.textContent = status.connectionActive ? `Gruppo: ${status.group}` : '';
      updated.textContent = `Stato aggiornato alle ${new Date().toLocaleTimeString()}. Il singolo messaggio mostra stato, tentativi ed errore nella tabella e nell'anteprima.`;
    } catch {
      gateway.textContent = 'Stato non disponibile';
      connection.textContent = 'Stato non disponibile';
      updated.textContent = 'Verifica live non disponibile: ricarica la pagina o controlla l\'accesso.';
    }
  }
  poll();
  window.setInterval(poll, 10000);
})();
