(() => {
  const device = document.getElementById('whatsapp-device');
  if (!device) return;
  const status = document.getElementById('whatsapp-status');
  const qr = document.getElementById('whatsapp-qr');
  let loadedGroups = false;
  let loadedMembers = false;
  async function get(url) {
    const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', redirect: 'error' });
    if (!response.ok) throw new Error('gateway_unavailable');
    return response.json();
  }
  function options(select, rows, label) {
    select.replaceChildren(new Option(label, ''));
    for (const row of rows) select.add(new Option(row.name || row.label || 'Membro senza nome', row.id));
  }
  function memberOptions(select, rows) {
    select.replaceChildren(new Option('Seleziona un membro', ''));
    const pending = document.createElement('optgroup');
    pending.label = 'Da associare';
    const associated = document.createElement('optgroup');
    associated.label = 'Già associati';
    for (const row of rows) {
      const isAssociated = row.isAssociated === true;
      const option = new Option(`${isAssociated ? '✓ Già associato - ' : ''}${row.label || 'Membro senza nome'}`, row.id);
      option.dataset.associated = String(isAssociated);
      (isAssociated ? associated : pending).append(option);
    }
    if (pending.childElementCount) select.append(pending);
    if (associated.childElementCount) select.append(associated);
    const updateState = () => {
      const isAssociated = select.selectedOptions[0]?.dataset.associated === 'true';
      select.classList.toggle('whatsapp-member-associated', isAssociated);
    };
    select.addEventListener('change', updateState);
    updateState();
  }
  async function poll() {
    if (document.hidden) return;
    try {
      const health = await get(device.dataset.statusUrl);
      const names = { disconnected: 'Disconnesso', connecting: 'Collegamento in corso', qr: 'Scansiona il QR', connected: 'Dispositivo collegato', error: 'Gateway non disponibile' };
      status.textContent = names[health.status] || 'Stato gateway non disponibile';
      const validQr = typeof health.qrDataUrl === 'string' && health.qrDataUrl.length < 1000000 && health.qrDataUrl.startsWith('data:image/png;base64,');
      qr.hidden = !validQr;
      if (validQr) qr.src = health.qrDataUrl;
      else qr.removeAttribute('src');
      if (health.status === 'connected' && !loadedGroups) {
        options(document.getElementById('whatsapp-group'), await get(device.dataset.groupsUrl), 'Seleziona il gruppo di prova');
        loadedGroups = true;
      }
      const member = document.getElementById('whatsapp-member');
      if (member && health.status === 'connected' && health.activeGroupId === device.dataset.bindingGroup && !loadedMembers) {
        memberOptions(member, await get(device.dataset.membersUrl));
        loadedMembers = true;
      }
      if (device.dataset.bindingGroup && health.status === 'connected' && health.activeGroupId !== device.dataset.bindingGroup)
        status.textContent += '. Seleziona e attiva nuovamente il gruppo: il gateway non ha un gruppo corrispondente.';
    } catch { status.textContent = 'Gateway non disponibile o accesso scaduto. Ricarica la pagina dopo aver verificato il collegamento.'; qr.hidden = true; qr.removeAttribute('src'); }
  }
  poll();
  window.setInterval(poll, 3000);
})();
