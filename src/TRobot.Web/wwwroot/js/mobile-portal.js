(() => {
    "use strict";

    // Native, open disclosures remain usable when JavaScript is unavailable.
    const mobile = window.matchMedia("(max-width: 767.98px)");
    const menus = [...document.querySelectorAll("details[data-mobile-collapse]")];
    const adaptMenus = () => menus.forEach(menu => { menu.open = !mobile.matches; });
    adaptMenus();
    mobile.addEventListener("change", adaptMenus);
    window.addEventListener("pageshow", event => { if (event.persisted) adaptMenus(); });
    document.addEventListener("keydown", event => {
        if (event.key !== "Escape" || !mobile.matches) return;
        const menu = menus.find(item => item.open && item.contains(document.activeElement));
        if (menu) {
            menu.open = false;
            menu.querySelector(":scope > summary").focus();
        }
    });
    document.addEventListener("click", event => {
        if (!mobile.matches) return;
        menus.forEach(menu => { if (menu.open && !menu.contains(event.target)) menu.open = false; });
    });

    const filter = document.querySelector("[data-participant-filter]");
    const table = document.getElementById("participants-table");
    if (filter && table) {
        const input = filter.querySelector("input[type=search]");
        const count = filter.querySelector("[data-filter-count]");
        const empty = filter.querySelector("[data-filter-empty]");
        const normalize = text => text.normalize("NFD").replace(/\p{Diacritic}/gu, "").toLocaleLowerCase("it-IT");
        const entries = [...table.tBodies[0].rows].map(row => ({ row, text: normalize(row.cells[0].textContent + " " + row.cells[1].textContent) }));
        const apply = () => {
            const terms = normalize(input.value).trim().split(/\s+/).filter(Boolean);
            let visible = 0;
            entries.forEach(entry => {
                entry.row.hidden = !terms.every(term => entry.text.includes(term));
                if (!entry.row.hidden) visible++;
            });
            count.textContent = `${visible} partecipanti visibili su ${entries.length}.`;
            empty.hidden = visible !== 0;
        };
        input.addEventListener("input", apply);
        filter.querySelector("[data-clear-filter]").addEventListener("click", () => {
            input.value = "";
            apply();
            input.focus();
        });
        filter.hidden = false;
        apply();
    }

    // One table and one set of controls, with labels exposed only on narrow screens.
    document.querySelectorAll("table.table-mobile-cards").forEach(table => {
        const labels = [...table.tHead.rows[0].cells].map(cell => cell.textContent.trim());
        table.setAttribute("role", "table");
        table.tHead.setAttribute("role", "rowgroup");
        table.tHead.rows[0].setAttribute("role", "row");
        [...table.tHead.rows[0].cells].forEach(cell => cell.setAttribute("role", "columnheader"));
        [...table.tBodies].forEach(body => {
            body.setAttribute("role", "rowgroup");
            [...body.rows].forEach(row => {
                row.setAttribute("role", "row");
                [...row.cells].forEach((cell, index) => {
                    cell.setAttribute("role", "cell");
                    const label = document.createElement("span");
                    label.className = "mobile-field-label";
                    label.setAttribute("aria-hidden", "true");
                    label.textContent = labels[index] || "Azioni";
                    const value = document.createElement("div");
                    value.className = "mobile-field-value";
                    while (cell.firstChild) value.append(cell.firstChild);
                    cell.append(label, value);
                });
            });
        });
        table.classList.add("mobile-cards-ready");
    });
})();
