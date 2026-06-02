// Editor de texto para prompts de agentes: formato estilo WhatsApp (*negrita*, _cursiva_,
// ~tachado~, ```mono```) y emojis, operando sobre un <textarea>. Tras modificar el valor se
// dispara un evento 'input' para que el binding de Blazor (@bind:event="oninput") lo capture.
window.cubotAgentEditor = (function () {
  function fire(el) {
    el.dispatchEvent(new Event('input', { bubbles: true }));
  }

  // Envuelve la seleccion (o inserta marcadores con el cursor en medio si no hay seleccion).
  function wrap(id, before, after) {
    const el = document.getElementById(id);
    if (!el) { return; }
    const s = el.selectionStart ?? el.value.length;
    const e = el.selectionEnd ?? el.value.length;
    const sel = el.value.substring(s, e);
    el.value = el.value.substring(0, s) + before + sel + after + el.value.substring(e);
    const pos = s + before.length + sel.length;
    el.focus();
    el.setSelectionRange(pos, pos);
    fire(el);
  }

  // Inserta texto (emoji) en la posicion del cursor.
  function insert(id, text) {
    const el = document.getElementById(id);
    if (!el) { return; }
    const s = el.selectionStart ?? el.value.length;
    const e = el.selectionEnd ?? el.value.length;
    el.value = el.value.substring(0, s) + text + el.value.substring(e);
    const pos = s + text.length;
    el.focus();
    el.setSelectionRange(pos, pos);
    fire(el);
  }

  return { wrap, insert };
})();
