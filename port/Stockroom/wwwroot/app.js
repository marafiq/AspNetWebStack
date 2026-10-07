"use strict";
document.getElementById("save-json").addEventListener("click", async function () {
  const form = document.getElementById("stock-form"), output = document.getElementById("json-result");
  this.disabled = true;
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
  finally { this.disabled = false; }
});
