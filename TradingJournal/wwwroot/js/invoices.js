(() => {
  if (window.jQuery?.validator) {
    const normalizeDecimal = (value) => (value || "").toString().replace(/\s/g, "").replace(",", ".");

    window.jQuery.validator.methods.number = function (value, element) {
      const normalized = normalizeDecimal(value);
      return this.optional(element) || /^-?(?:\d+|\d*\.\d+)$/.test(normalized);
    };

    window.jQuery.validator.methods.range = function (value, element, param) {
      const normalized = Number.parseFloat(normalizeDecimal(value));
      return this.optional(element) || (Number.isFinite(normalized) && normalized >= param[0] && normalized <= param[1]);
    };
  }

  const list = document.getElementById("invoiceItems");
  const addButton = document.getElementById("addInvoiceItem");
  const template = document.getElementById("invoiceItemTemplate");

  if (!list) {
    return;
  }

  const timesheetTotal = document.getElementById("timesheetHoursTotal");
  const timesheetTotalInput = document.getElementById("timesheetHoursTotalInput");

  function parseDecimal(value) {
    const normalized = (value || "").toString().replace(/\s/g, "").replace(",", ".");
    const parsed = Number.parseFloat(normalized);
    return Number.isFinite(parsed) ? parsed : 0;
  }

  function formatHours(value) {
    return value.toLocaleString("cs-CZ", {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    });
  }

  function syncTimesheetTotal() {
    if (!timesheetTotal && !timesheetTotalInput) {
      return;
    }

    const total = [...list.querySelectorAll(".timesheet-hours-input")]
      .reduce((sum, input) => sum + parseDecimal(input.value), 0);
    if (timesheetTotal) {
      timesheetTotal.textContent = formatHours(total);
    }

    if (timesheetTotalInput && document.activeElement !== timesheetTotalInput) {
      timesheetTotalInput.value = formatHours(total);
    }
  }

  list.addEventListener("input", (event) => {
    if (event.target.matches(".timesheet-hours-input")) {
      syncTimesheetTotal();
    }
  });

  syncTimesheetTotal();

  if (!addButton || !template) {
    return;
  }

  function nextIndex() {
    return list.querySelectorAll(".invoice-item-input").length;
  }

  function syncRemoveButtons() {
    const rows = list.querySelectorAll(".invoice-item-input");
    rows.forEach((row) => {
      const button = row.querySelector(".remove-invoice-item");
      if (button) {
        button.disabled = rows.length <= 1;
      }
    });
  }

  function reindexRows() {
    list.querySelectorAll(".invoice-item-input").forEach((row, index) => {
      row.querySelectorAll("input").forEach((input) => {
        input.name = input.name.replace(/Items\[\d+\]/, `Items[${index}]`);
        input.id = input.id.replace(/Items_\d+__/, `Items_${index}__`);
      });
    });
  }

  addButton.addEventListener("click", () => {
    const index = nextIndex();
    const html = template.innerHTML.replaceAll("__index__", index.toString());
    list.insertAdjacentHTML("beforeend", html);
    syncRemoveButtons();
  });

  list.addEventListener("click", (event) => {
    const button = event.target.closest(".remove-invoice-item");
    if (!button) {
      return;
    }

    const rows = list.querySelectorAll(".invoice-item-input");
    if (rows.length <= 1) {
      return;
    }

    button.closest(".invoice-item-input")?.remove();
    reindexRows();
    syncRemoveButtons();
  });

  syncRemoveButtons();
})();
