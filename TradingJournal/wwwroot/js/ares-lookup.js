(() => {
  const button = document.getElementById("aresLookupButton");
  const icoInput = document.getElementById("aresIco");
  const message = document.getElementById("aresLookupMessage");

  if (!button || !icoInput || !message) {
    return;
  }

  const fields = {
    name: document.getElementById("Name"),
    registrationNumber: document.getElementById("RegistrationNumber"),
    taxNumber: document.getElementById("TaxNumber"),
    street: document.getElementById("Street"),
    city: document.getElementById("City"),
    zipCode: document.getElementById("ZipCode"),
    country: document.getElementById("Country")
  };

  function setMessage(text, type) {
    message.textContent = text;
    message.classList.remove("is-error", "is-success");
    if (type) {
      message.classList.add(type);
    }
  }

  function fillField(field, value) {
    if (field && value) {
      field.value = value;
      field.dispatchEvent(new Event("input", { bubbles: true }));
    }
  }

  button.addEventListener("click", async () => {
    const ico = icoInput.value.replace(/\D/g, "");
    icoInput.value = ico;

    if (ico.length !== 8) {
      setMessage("Zadej IČO ve formátu 8 číslic.", "is-error");
      return;
    }

    button.disabled = true;
    setMessage("Načítám údaje z ARES...", null);

    try {
      const response = await fetch(`/Invoices/LookupAres?ico=${encodeURIComponent(ico)}`, {
        headers: { "Accept": "application/json" }
      });

      const data = await response.json().catch(() => ({}));
      if (!response.ok) {
        setMessage(data.message || "Údaje se nepodařilo načíst.", "is-error");
        return;
      }

      fillField(fields.name, data.name);
      fillField(fields.registrationNumber, data.registrationNumber);
      fillField(fields.taxNumber, data.taxNumber);
      fillField(fields.street, data.street);
      fillField(fields.city, data.city);
      fillField(fields.zipCode, data.zipCode);
      fillField(fields.country, data.country || "Česká republika");
      setMessage("Údaje z ARES jsou vyplněné. Zkontroluj je a firmu ulož.", "is-success");
    } catch {
      setMessage("ARES se nepodařilo kontaktovat. Zkus to prosím za chvíli.", "is-error");
    } finally {
      button.disabled = false;
    }
  });
})();
