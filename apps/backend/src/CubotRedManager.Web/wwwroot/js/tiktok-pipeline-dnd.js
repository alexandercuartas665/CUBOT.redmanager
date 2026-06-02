// Drag & Drop minimal para el Pipeline TikTok Manager.
// Se invoca desde Blazor con DotNetObjectReference para llamar StageChangedAsync.
window.tiktokPipelineDnd = (function () {
    let dotnet = null;

    function init(dotNetRef) {
        dotnet = dotNetRef;
        bindAll();
    }

    function bindAll() {
        document.querySelectorAll('.tk-kanban-item').forEach(item => {
            item.setAttribute('draggable', 'true');
            item.ondragstart = ev => {
                ev.dataTransfer.setData('text/plain', item.dataset.id);
                item.classList.add('tk-dragging');
            };
            item.ondragend = () => item.classList.remove('tk-dragging');
        });
        document.querySelectorAll('.tk-kanban-col').forEach(col => {
            col.ondragover = ev => { ev.preventDefault(); col.classList.add('tk-col-hover'); };
            col.ondragleave = () => col.classList.remove('tk-col-hover');
            col.ondrop = async ev => {
                ev.preventDefault();
                col.classList.remove('tk-col-hover');
                const id = ev.dataTransfer.getData('text/plain');
                const stage = col.dataset.stage;
                if (id && stage && dotnet) {
                    await dotnet.invokeMethodAsync('OnStageChanged', id, stage);
                }
            };
        });
    }

    function rebind() { bindAll(); }

    return { init, rebind };
})();
