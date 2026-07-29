// Redimensionador de columnas para el modulo Contenedor de Datos.
// Aplica y persiste anchos por (contenedor, columna) en localStorage. Sobrevive recargas del
// browser sin necesidad de tocar la BD. Clave: dc-widths-{containerId} -> { columnId: pxWidth }.
//
// Uso desde Blazor:
//   await JS.InvokeVoidAsync("dcResize.init", tableRef, containerId);
// El tableRef debe apuntar al <table> y cada <th> debe tener data-col-id="{guid}" y contener un
// <div class="dc-resizer"></div> al final.
(function () {
    if (window.dcResize) { return; } // idempotente

    function storageKey(containerId) {
        return "dc-widths-" + (containerId || "default");
    }

    function loadWidths(containerId) {
        try { return JSON.parse(localStorage.getItem(storageKey(containerId)) || "{}"); }
        catch { return {}; }
    }

    function saveWidths(containerId, map) {
        try { localStorage.setItem(storageKey(containerId), JSON.stringify(map)); }
        catch { /* quota exceeded o modo privado - ignoramos silenciosamente */ }
    }

    function applyWidths(tableEl, containerId) {
        var map = loadWidths(containerId);
        var ths = tableEl.querySelectorAll('th[data-col-id]');
        ths.forEach(function (th) {
            var w = map[th.getAttribute('data-col-id')];
            if (w && w > 20) { th.style.width = w + "px"; }
        });
    }

    function wireResizers(tableEl, containerId) {
        var handles = tableEl.querySelectorAll('.dc-resizer');
        handles.forEach(function (handle) {
            // Evitar duplicar listener si init corre 2 veces sobre la misma celda.
            if (handle.dataset.wired === "1") { return; }
            handle.dataset.wired = "1";

            handle.addEventListener('mousedown', function (e) {
                e.preventDefault();
                e.stopPropagation();
                var th = handle.closest('th');
                if (!th) { return; }
                var startX = e.clientX;
                var startW = th.getBoundingClientRect().width;
                document.body.style.cursor = 'col-resize';

                function onMove(ev) {
                    var delta = ev.clientX - startX;
                    var w = Math.max(50, startW + delta);
                    th.style.width = w + "px";
                }

                function onUp() {
                    document.removeEventListener('mousemove', onMove);
                    document.removeEventListener('mouseup', onUp);
                    document.body.style.cursor = '';
                    var colId = th.getAttribute('data-col-id');
                    if (!colId) { return; }
                    var map = loadWidths(containerId);
                    map[colId] = Math.round(th.getBoundingClientRect().width);
                    saveWidths(containerId, map);
                }

                document.addEventListener('mousemove', onMove);
                document.addEventListener('mouseup', onUp);
            });
        });
    }

    window.dcResize = {
        init: function (tableEl, containerId) {
            if (!tableEl) { return; }
            applyWidths(tableEl, containerId);
            wireResizers(tableEl, containerId);
        }
    };
})();
