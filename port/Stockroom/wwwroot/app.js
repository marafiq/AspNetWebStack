"use strict";
document.addEventListener("click", async function (event) {
  const button = event.target.closest("#save-json");
  if (!button) return;
  const form = document.getElementById("stock-form"), output = document.getElementById("json-result");
  button.disabled = true;
  try {
    const response = await fetch(form.dataset.jsonUrl, {
      method: "POST", credentials: "same-origin", headers: { "Content-Type": "application/json", "X-Stockroom-CSRF": form.elements.__RequestVerificationToken.value },
      body: JSON.stringify({ Quantity: Number(form.elements.Quantity.value), Version: Number(form.elements.Version.value) })
    });
    const value = await response.json();
    if (!response.ok) { output.textContent = value.error || "Save failed. Reload and try again."; return; }
    form.elements.Version.value = value.version;
    output.textContent = "Saved " + value.quantity + " notebooks.";
  } catch { output.textContent = "Save could not be confirmed. Reload the stock before retrying."; }
  finally { button.disabled = false; }
});

window.Stockroom = {
  begin: function () {
    document.getElementById("editor-region").setAttribute("aria-busy", "true");
    document.getElementById("ajax-status").textContent = "Working…";
  },
  saved: function () {
    jQuery.validator.unobtrusive.parse("#editor-region");
    document.getElementById("ajax-status").textContent = document.querySelector("#editor-region .validation-summary-errors") ? "Check the highlighted quantity." : "Quantity saved.";
  },
  loaded: function () {
    jQuery.validator.unobtrusive.parse("#editor-region");
    document.getElementById("ajax-status").textContent = "Current stock loaded.";
  },
  failed: function (xhr) {
    document.getElementById("ajax-status").textContent = xhr.status === 401 ? "Your sign-in has expired. Reload this page to sign in again." : xhr.status === 409 ? "Stock changed. Reload current stock before saving." : "Save could not be confirmed. Reload current stock before retrying.";
  },
  complete: function () { document.getElementById("editor-region").removeAttribute("aria-busy"); }
};
