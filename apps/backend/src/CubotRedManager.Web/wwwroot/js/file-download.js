// Utilidad global: recibe un archivo generado server-side (base64) y dispara la descarga en el
// navegador. Usada por paginas Blazor como Contenedor de Datos (export xlsx) y Agentes (export json).
window.cubotDownloadBase64 = function (filename, base64, mime) {
    var byteChars = atob(base64);
    var byteNumbers = new Array(byteChars.length);
    for (var i = 0; i < byteChars.length; i++) { byteNumbers[i] = byteChars.charCodeAt(i); }
    var byteArray = new Uint8Array(byteNumbers);
    var blob = new Blob([byteArray], { type: mime || 'application/octet-stream' });
    var url = URL.createObjectURL(blob);
    var a = document.createElement('a');
    a.href = url;
    a.download = filename || 'archivo';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    setTimeout(function () { URL.revokeObjectURL(url); }, 250);
};
