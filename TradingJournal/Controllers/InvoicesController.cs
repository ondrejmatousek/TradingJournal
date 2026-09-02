using System.Globalization;
using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using QRCoder;
using System.Text.Json;
using TradingJournal.Data;
using TradingJournal.Models;
using TradingJournal.ViewModels;

namespace TradingJournal.Controllers
{
    [Authorize]
    public class InvoicesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;

        public InvoicesController(ApplicationDbContext context, IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
        }

        private string CurrentUserId =>
            User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        public async Task<IActionResult> Index(
            string? search,
            InvoiceStatus? status,
            int? customerId,
            DateTime? dateFrom,
            DateTime? dateTo)
        {
            var userId = CurrentUserId;

            var query = _context.Invoices
                .AsNoTracking()
                .Include(i => i.SupplierCompany)
                .Include(i => i.CustomerCompany)
                .Include(i => i.Items)
                .Where(i => i.UserId == userId);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(i =>
                    i.InvoiceNumber.Contains(term) ||
                    i.CustomerCompany!.Name.Contains(term) ||
                    i.SupplierCompany!.Name.Contains(term));
            }

            if (status.HasValue)
                query = query.Where(i => i.Status == status.Value);

            if (customerId.HasValue)
                query = query.Where(i => i.CustomerCompanyId == customerId.Value);

            if (dateFrom.HasValue)
                query = query.Where(i => i.IssueDate >= dateFrom.Value.Date);

            if (dateTo.HasValue)
                query = query.Where(i => i.IssueDate <= dateTo.Value.Date);

            var invoices = await query
                .OrderByDescending(i => i.IssueDate)
                .ThenByDescending(i => i.Id)
                .ToListAsync();

            var customers = await _context.InvoiceCompanies
                .AsNoTracking()
                .Where(c => c.UserId == userId)
                .OrderBy(c => c.Name)
                .ToListAsync();

            var model = new InvoiceIndexViewModel
            {
                Invoices = invoices,
                Filters = new InvoiceFilters
                {
                    Search = search,
                    Status = status,
                    CustomerId = customerId,
                    DateFrom = dateFrom,
                    DateTo = dateTo
                },
                Stats = new InvoiceStats
                {
                    TotalInvoices = invoices.Count,
                    DraftInvoices = invoices.Count(i => i.Status == InvoiceStatus.Draft),
                    PaidInvoices = invoices.Count(i => i.Status == InvoiceStatus.Paid),
                    TotalIssued = invoices.Where(i => i.Status != InvoiceStatus.Cancelled).Sum(i => i.TotalGross),
                    TotalPaid = invoices.Where(i => i.Status == InvoiceStatus.Paid).Sum(i => i.TotalGross),
                    Outstanding = invoices.Where(i => i.Status is InvoiceStatus.Issued or InvoiceStatus.Overdue).Sum(i => i.TotalGross)
                },
                CustomerOptions = customers
                    .Select(c => new SelectListItem(c.Name, c.Id.ToString(CultureInfo.InvariantCulture), c.Id == customerId))
                    .ToList(),
                StatusOptions = Enum.GetValues<InvoiceStatus>()
                    .Select(value => new SelectListItem(value.ToString(), value.ToString(), value == status))
                    .ToList()
            };

            return View(model);
        }

        public async Task<IActionResult> Details(int id)
        {
            var userId = CurrentUserId; 

            var invoice = await _context.Invoices
                .AsNoTracking()
                .Include(i => i.SupplierCompany)
                .Include(i => i.CustomerCompany)
                .Include(i => i.Items)
                .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId);

            if (invoice == null)
                return NotFound();

            return View(invoice);
        }

        public async Task<IActionResult> Print(int id)
        {
            var userId = CurrentUserId;

            var invoice = await _context.Invoices
                .AsNoTracking()
                .Include(i => i.SupplierCompany)
                .Include(i => i.CustomerCompany)
                .Include(i => i.Items)
                .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId);

            if (invoice == null)
                return NotFound();

            ViewBag.PaymentQrCodeDataUri = CreatePaymentQrCodeDataUri(invoice);
            return View(invoice);
        }

        public async Task<IActionResult> Create()
        {
            var model = new InvoiceFormViewModel
            {
                SupplierCompanyId = await DefaultSupplierIdAsync()
            };
            await PopulateInvoiceFormOptionsAsync(model);
            ViewData["FormAction"] = nameof(Create);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(InvoiceFormViewModel model)
        {
            NormalizeInvoiceDecimalFieldsFromForm(model);
            NormalizeItems(model);
            await PopulateInvoiceFormOptionsAsync(model);
            ApplyVatRegime(model);

            if (!model.Items.Any())
                ModelState.AddModelError("", "Faktura musí­ obsahovat alespoň jednu položku.");

            if (model.SupplierCompanyId == model.CustomerCompanyId && model.SupplierCompanyId.HasValue)
                ModelState.AddModelError("", "Dodavatel a odběratel by měli být dvě rozdílné firmy.");

            if (!await CompanyBelongsToUserAsync(model.SupplierCompanyId))
                ModelState.AddModelError(nameof(model.SupplierCompanyId), "Vybraný˝ dodavatel neexistuje.");

            if (!await CompanyBelongsToUserAsync(model.CustomerCompanyId))
                ModelState.AddModelError(nameof(model.CustomerCompanyId), "Vybraný˝ odběratel neexistuje.");

            if (!ModelState.IsValid)
            {
                ViewData["FormAction"] = nameof(Create);
                ViewData["IsTimesheetImport"] = model.Items.All(i => i.Unit == "hod");
                return View(model);
            }

            ApplyTotalHoursOverride(model);

            var invoice = new Invoice
            {
                UserId = CurrentUserId,
                InvoiceNumber = await GenerateInvoiceNumberAsync(),
                SupplierCompanyId = model.SupplierCompanyId!.Value,
                CustomerCompanyId = model.CustomerCompanyId!.Value,
                IssueDate = model.IssueDate.Date,
                DueDate = model.DueDate.Date,
                TaxableSupplyDate = model.IsSupplierVatPayer ? model.TaxableSupplyDate?.Date : null,
                Currency = model.Currency.Trim().ToUpperInvariant(),
                Status = InvoiceStatus.Draft,
                Notes = model.Notes,
                CreatedAt = DateTimeOffset.UtcNow,
                Items = model.Items.Select(item => new InvoiceItem
                {
                    Description = item.Description.Trim(),
                    Unit = string.IsNullOrWhiteSpace(item.Unit) ? "ks" : item.Unit.Trim(),
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    VatRate = model.IsSupplierVatPayer ? item.VatRate : 0m
                }).ToList()
            };

            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Details), new { id = invoice.Id });
        }

        public async Task<IActionResult> Edit(int id)
        {
            var invoice = await _context.Invoices
                .AsNoTracking()
                .Include(i => i.Items)
                .FirstOrDefaultAsync(i => i.Id == id && i.UserId == CurrentUserId);

            if (invoice == null)
                return NotFound();

            var model = new InvoiceFormViewModel
            {
                Id = invoice.Id,
                InvoiceNumber = invoice.InvoiceNumber,
                SupplierCompanyId = invoice.SupplierCompanyId,
                CustomerCompanyId = invoice.CustomerCompanyId,
                IssueDate = invoice.IssueDate,
                DueDate = invoice.DueDate,
                TaxableSupplyDate = invoice.TaxableSupplyDate,
                Currency = invoice.Currency,
                Status = invoice.Status,
                Notes = invoice.Notes,
                TotalHoursOverride = invoice.Items.All(i => string.Equals(i.Unit, "hod", StringComparison.OrdinalIgnoreCase))
                    ? invoice.Items.Sum(i => i.Quantity)
                    : null,
                Items = invoice.Items.Select(item => new InvoiceItemInput
                {
                    Description = item.Description,
                    Quantity = item.Quantity,
                    Unit = item.Unit,
                    UnitPrice = item.UnitPrice,
                    VatRate = item.VatRate
                }).ToList()
            };

            await PopulateInvoiceFormOptionsAsync(model);
            ViewData["FormAction"] = nameof(Edit);
            ViewData["IsTimesheetImport"] = model.Items.All(i => i.Unit == "hod");
            return View("Create", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, InvoiceFormViewModel model)
        {
            if (model.Id.HasValue && model.Id.Value != id)
                return BadRequest();

            NormalizeInvoiceDecimalFieldsFromForm(model);
            NormalizeItems(model);
            await PopulateInvoiceFormOptionsAsync(model);
            ApplyVatRegime(model);

            if (!model.Items.Any())
                ModelState.AddModelError("", "Faktura musí obsahovat alespoň jednu položku.");

            if (model.SupplierCompanyId == model.CustomerCompanyId && model.SupplierCompanyId.HasValue)
                ModelState.AddModelError("", "Dodavatel a odběratel by měli být dvě rozdílné firmy.");

            if (!await CompanyBelongsToUserAsync(model.SupplierCompanyId))
                ModelState.AddModelError(nameof(model.SupplierCompanyId), "Vybraný dodavatel neexistuje.");

            if (!await CompanyBelongsToUserAsync(model.CustomerCompanyId))
                ModelState.AddModelError(nameof(model.CustomerCompanyId), "Vybraný odběratel neexistuje.");

            if (!ModelState.IsValid)
            {
                ViewData["FormAction"] = nameof(Edit);
                ViewData["IsTimesheetImport"] = model.Items.All(i => i.Unit == "hod");
                return View("Create", model);
            }

            var invoice = await _context.Invoices
                .Include(i => i.Items)
                .FirstOrDefaultAsync(i => i.Id == id && i.UserId == CurrentUserId);

            if (invoice == null)
                return NotFound();

            ApplyTotalHoursOverride(model);

            invoice.SupplierCompanyId = model.SupplierCompanyId!.Value;
            invoice.CustomerCompanyId = model.CustomerCompanyId!.Value;
            invoice.IssueDate = model.IssueDate.Date;
            invoice.DueDate = model.DueDate.Date;
            invoice.TaxableSupplyDate = model.IsSupplierVatPayer ? model.TaxableSupplyDate?.Date : null;
            invoice.Currency = model.Currency.Trim().ToUpperInvariant();
            invoice.Notes = model.Notes;

            _context.InvoiceItems.RemoveRange(invoice.Items);
            invoice.Items = model.Items.Select(item => new InvoiceItem
            {
                Description = item.Description.Trim(),
                Unit = string.IsNullOrWhiteSpace(item.Unit) ? "ks" : item.Unit.Trim(),
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                VatRate = model.IsSupplierVatPayer ? item.VatRate : 0m
            }).ToList();

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Details), new { id = invoice.Id });
        }

        public async Task<IActionResult> Delete(int id)
        {
            var invoice = await _context.Invoices
                .AsNoTracking()
                .Include(i => i.CustomerCompany)
                .Include(i => i.Items)
                .FirstOrDefaultAsync(i => i.Id == id && i.UserId == CurrentUserId);

            if (invoice == null)
                return NotFound();

            return View(invoice);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var invoice = await _context.Invoices
                .Include(i => i.Items)
                .FirstOrDefaultAsync(i => i.Id == id && i.UserId == CurrentUserId);

            if (invoice == null)
                return NotFound();

            _context.InvoiceItems.RemoveRange(invoice.Items);
            _context.Invoices.Remove(invoice);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Import()
        {
            var model = new InvoiceImportViewModel
            {
                SupplierCompanyId = await DefaultSupplierIdAsync()
            };
            await PopulateImportOptionsAsync(model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Import(InvoiceImportViewModel model, IFormFile? excelFile)
        {
            NormalizeHourlyRateFromForm(model);
            await PopulateImportOptionsAsync(model);

            if (excelFile == null || excelFile.Length == 0)
                ModelState.AddModelError("", "Vyber Excel soubor ve formátu .xlsx.");

            if (!await CompanyBelongsToUserAsync(model.SupplierCompanyId))
                ModelState.AddModelError(nameof(model.SupplierCompanyId), "Vybraný˝ dodavatel neexistuje.");

            if (!await CompanyBelongsToUserAsync(model.CustomerCompanyId))
                ModelState.AddModelError(nameof(model.CustomerCompanyId), "Vybraný˝ odbÄ›ratel neexistuje.");

            model.HourlyRate ??= await LastHourlyRateForCustomerAsync(model.CustomerCompanyId);

            if (!model.HourlyRate.HasValue || model.HourlyRate.Value <= 0)
                ModelState.AddModelError(nameof(model.HourlyRate), "Hodinováˇ sazba musí­ být větší­ než 0.");

            if (!ModelState.IsValid)
                return View(model);

            var supplierIsVatPayer = await IsVatPayerAsync(model.SupplierCompanyId);

            List<InvoiceItemInput> items;
            try
            {
                await using var stream = excelFile!.OpenReadStream();
                items = ParseInvoiceItemsFromXlsx(stream, model.HourlyRate ?? 0, supplierIsVatPayer);
            }
            catch
            {
                ModelState.AddModelError("", "Soubor se nepodařilo převé­st. Zkus prosí­m .xlsx s běžnou tabulkou položek.");
                return View(model);
            }

            if (!items.Any())
            {
                ModelState.AddModelError("", "V Excelu jsem nenačel tyto položky. Použij sloupce Popis, Množství­, Jednotka, Cena, DPH.");
                return View(model);
            }

            if (model.HourlyRate.HasValue && model.HourlyRate.Value > 0)
                await RememberHourlyRateAsync(model.CustomerCompanyId!.Value, model.HourlyRate.Value);

            var invoice = new InvoiceFormViewModel
            {
                SupplierCompanyId = model.SupplierCompanyId,
                CustomerCompanyId = model.CustomerCompanyId,
                IssueDate = model.IssueDate,
                DueDate = model.DueDate,
                TaxableSupplyDate = model.IsSupplierVatPayer ? model.TaxableSupplyDate : null,
                Currency = model.Currency,
                Status = InvoiceStatus.Draft,
                TotalHoursOverride = items.Sum(i => i.Quantity),
                Items = items
            };

            await PopulateInvoiceFormOptionsAsync(invoice);
            ViewData["ImportNotice"] = $"NaimportovĂˇno {items.Count} schvĂˇlenĂ˝ch Ĺ™ĂˇdkĹŻ. Zkontroluj souÄŤet hodin a uloĹľ fakturu jako koncept.";
            ViewData["IsTimesheetImport"] = items.All(i => i.Unit == "hod");
            return View("Create", invoice);
        }

        public async Task<IActionResult> Companies()
        {
            var companies = await _context.InvoiceCompanies
                .AsNoTracking()
                .Where(c => c.UserId == CurrentUserId)
                .OrderByDescending(c => c.IsDefaultSupplier)
                .ThenBy(c => c.Name)
                .ToListAsync();

            return View(companies);
        }

        public IActionResult CreateCompany()
        {
            return View(new InvoiceCompanyFormViewModel());
        }

        [HttpGet]
        public async Task<IActionResult> LookupAres(string ico)
        {
            var cleanIco = new string((ico ?? string.Empty).Where(char.IsDigit).ToArray());
            if (cleanIco.Length != 8)
                return BadRequest(new { message = "IÄŚO musĂ­ mĂ­t 8 ÄŤĂ­slic." });

            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            using var response = await client.GetAsync($"https://ares.gov.cz/ekonomicke-subjekty-v-be/rest/ekonomicke-subjekty/{cleanIco}");
            if (!response.IsSuccessStatusCode)
            {
                return response.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? NotFound(new { message = "Firma s tímto IčO nebyla v ARES nalezena." })
                    : StatusCode(503, new { message = "ARES teď neodpoví­dá. Zkus to prosí­m za chví­li." });
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            var root = document.RootElement;
            var address = root.TryGetProperty("sidlo", out var sidlo) ? sidlo : default;

            var result = new
            {
                name = GetString(root, "obchodniJmeno"),
                registrationNumber = GetString(root, "ico") ?? cleanIco,
                taxNumber = GetString(root, "dic"),
                street = BuildAresStreet(address),
                city = GetString(address, "nazevObce"),
                zipCode = GetString(address, "psc"),
                country = GetString(address, "nazevStatu") ?? "Česká republika"
            };

            return Json(result);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateCompany(InvoiceCompanyFormViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            if (model.IsDefaultSupplier)
            {
                var currentDefaults = await _context.InvoiceCompanies
                    .Where(c => c.UserId == CurrentUserId && c.IsDefaultSupplier)
                    .ToListAsync();
                currentDefaults.ForEach(c => c.IsDefaultSupplier = false);
            }

            _context.InvoiceCompanies.Add(ApplyCompany(new InvoiceCompany
            {
                UserId = CurrentUserId,
                CreatedAt = DateTimeOffset.UtcNow
            }, model));

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Companies));
        }

        public async Task<IActionResult> EditCompany(int id)
        {
            var company = await _context.InvoiceCompanies
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == CurrentUserId);

            if (company == null)
                return NotFound();

            return View("CreateCompany", new InvoiceCompanyFormViewModel
            {
                Id = company.Id,
                Name = company.Name,
                RegistrationNumber = company.RegistrationNumber,
                TaxNumber = company.TaxNumber,
                IsVatPayer = company.IsVatPayer,
                Street = company.Street,
                City = company.City,
                ZipCode = company.ZipCode,
                Country = company.Country,
                Email = company.Email,
                Phone = company.Phone,
                BankAccount = company.BankAccount,
                Iban = company.Iban,
                IsDefaultSupplier = company.IsDefaultSupplier
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditCompany(int id, InvoiceCompanyFormViewModel model)
        {
            if (!ModelState.IsValid)
                return View("CreateCompany", model);

            var company = await _context.InvoiceCompanies
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == CurrentUserId);

            if (company == null)
                return NotFound();

            if (model.IsDefaultSupplier)
            {
                var currentDefaults = await _context.InvoiceCompanies
                    .Where(c => c.UserId == CurrentUserId && c.IsDefaultSupplier && c.Id != id)
                    .ToListAsync();
                currentDefaults.ForEach(c => c.IsDefaultSupplier = false);
            }

            ApplyCompany(company, model);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Companies));
        }

        public async Task<IActionResult> DeleteCompany(int id)
        {
            var company = await _context.InvoiceCompanies
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == CurrentUserId);

            if (company == null)
                return NotFound();

            var isUsed = await _context.Invoices
                .AnyAsync(i => i.UserId == CurrentUserId && (i.SupplierCompanyId == id || i.CustomerCompanyId == id));

            ViewData["IsUsed"] = isUsed;
            return View(company);
        }

        [HttpPost, ActionName("DeleteCompany")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteCompanyConfirmed(int id)
        {
            var company = await _context.InvoiceCompanies
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == CurrentUserId);

            if (company == null)
                return NotFound();

            var isUsed = await _context.Invoices
                .AnyAsync(i => i.UserId == CurrentUserId && (i.SupplierCompanyId == id || i.CustomerCompanyId == id));

            if (isUsed)
            {
                ModelState.AddModelError("", "Firmu nejde smazat, protoĹľe je pouĹľitĂˇ na faktuĹ™e.");
                ViewData["IsUsed"] = true;
                return View(company);
            }

            _context.InvoiceCompanies.Remove(company);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Companies));
        }

        private static InvoiceCompany ApplyCompany(InvoiceCompany company, InvoiceCompanyFormViewModel model)
        {
            company.Name = model.Name.Trim();
            company.RegistrationNumber = model.RegistrationNumber;
            company.TaxNumber = model.TaxNumber;
            company.IsVatPayer = model.IsVatPayer;
            company.Street = model.Street;
            company.City = model.City;
            company.ZipCode = model.ZipCode;
            company.Country = model.Country;
            company.Email = model.Email;
            company.Phone = model.Phone;
            company.BankAccount = model.BankAccount;
            company.Iban = model.Iban;
            company.IsDefaultSupplier = model.IsDefaultSupplier;
            return company;
        }

        private static string? CreatePaymentQrCodeDataUri(Invoice invoice)
        {
            var iban = NormalizeIban(invoice.SupplierCompany?.Iban);
            if (string.IsNullOrWhiteSpace(iban))
                iban = TryCreateCzechIban(invoice.SupplierCompany?.BankAccount);

            var supplierIsVatPayer = invoice.SupplierCompany?.IsVatPayer == true;
            var amountDue = supplierIsVatPayer ? invoice.TotalGross : invoice.TotalNet;

            if (string.IsNullOrWhiteSpace(iban) || amountDue <= 0)
                return null;

            var variableSymbol = DigitsOnly(invoice.InvoiceNumber);
            var amount = amountDue.ToString("0.00", CultureInfo.InvariantCulture);
            var currency = string.IsNullOrWhiteSpace(invoice.Currency) ? "CZK" : invoice.Currency.Trim().ToUpperInvariant();
            var message = SanitizeQrField($"Faktura {invoice.InvoiceNumber}");
            var payload = $"SPD*1.0*ACC:{iban}*AM:{amount}*CC:{currency}";

            if (!string.IsNullOrWhiteSpace(variableSymbol))
                payload += $"*X-VS:{variableSymbol}";

            if (!string.IsNullOrWhiteSpace(message))
                payload += $"*MSG:{message}";

            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
            var qr = new PngByteQRCode(data);
            var bytes = qr.GetGraphic(5);
            return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
        }

        private static string? NormalizeIban(string? iban)
        {
            var cleaned = new string((iban ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .ToArray())
                .ToUpperInvariant();

            return cleaned.StartsWith("CZ", StringComparison.Ordinal) && cleaned.Length == 24 ? cleaned : null;
        }

        private static string? TryCreateCzechIban(string? account)
        {
            if (string.IsNullOrWhiteSpace(account) || !account.Contains('/'))
                return null;

            var parts = account.Trim().Split('/', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
                return null;

            var bank = DigitsOnly(parts[1]);
            if (bank.Length != 4)
                return null;

            var accountPart = parts[0].Replace(" ", string.Empty, StringComparison.Ordinal);
            var accountParts = accountPart.Split('-', 2, StringSplitOptions.TrimEntries);
            var prefix = accountParts.Length == 2 ? DigitsOnly(accountParts[0]) : string.Empty;
            var number = DigitsOnly(accountParts.Length == 2 ? accountParts[1] : accountParts[0]);

            if (prefix.Length > 6 || number.Length is 0 or > 10)
                return null;

            var bban = bank + prefix.PadLeft(6, '0') + number.PadLeft(10, '0');
            var checkDigits = 98 - Mod97(bban + "123500");
            return "CZ" + checkDigits.ToString("00", CultureInfo.InvariantCulture) + bban;
        }

        private static int Mod97(string value)
        {
            var remainder = 0;
            foreach (var c in value)
                remainder = (remainder * 10 + (c - '0')) % 97;

            return remainder;
        }

        private static string DigitsOnly(string? value)
            => new((value ?? string.Empty).Where(char.IsDigit).ToArray());

        private static string SanitizeQrField(string value)
            => value.Replace("*", " ", StringComparison.Ordinal)
                .Replace(Environment.NewLine, " ", StringComparison.Ordinal)
                .Trim();

        private async Task PopulateInvoiceFormOptionsAsync(InvoiceFormViewModel model)
        {
            var companies = await _context.InvoiceCompanies
                .AsNoTracking()
                .Where(c => c.UserId == CurrentUserId)
                .OrderByDescending(c => c.IsDefaultSupplier)
                .ThenBy(c => c.Name)
                .ToListAsync();

            model.SupplierOptions = companies
                .Select(c => new SelectListItem(c.Name, c.Id.ToString(CultureInfo.InvariantCulture), c.Id == model.SupplierCompanyId))
                .ToList();
            model.SupplierVatPayerOptions = companies.ToDictionary(c => c.Id, c => c.IsVatPayer);

            model.CustomerOptions = companies
                .Where(c => !model.SupplierCompanyId.HasValue || c.Id != model.SupplierCompanyId.Value)
                .OrderBy(c => c.Name)
                .Select(c => new SelectListItem(c.Name, c.Id.ToString(CultureInfo.InvariantCulture), c.Id == model.CustomerCompanyId))
                .ToList();

            model.CustomerHourlyRates = companies
                .Where(c => c.LastHourlyRate.HasValue)
                .ToDictionary(c => c.Id, c => c.LastHourlyRate);
            var teamDelivery = companies.Where(c => IsTeamDeliverySolution(c.Name)).ToList();
            model.CustomerInvoiceDescriptions = teamDelivery.ToDictionary(c => c.Id, _ => "expertní vývojové práce MS .NET");
            model.CustomerInvoiceDefaultPrices = teamDelivery.ToDictionary(c => c.Id, _ => 750m);
            model.IsSupplierVatPayer = model.SupplierCompanyId.HasValue && companies.Any(c => c.Id == model.SupplierCompanyId.Value && c.IsVatPayer);
        }

        private async Task PopulateImportOptionsAsync(InvoiceImportViewModel model)
        {
            var form = new InvoiceFormViewModel
            {
                SupplierCompanyId = model.SupplierCompanyId,
                CustomerCompanyId = model.CustomerCompanyId
            };

            await PopulateInvoiceFormOptionsAsync(form);
            model.SupplierOptions = form.SupplierOptions;
            model.SupplierVatPayerOptions = form.SupplierVatPayerOptions;
            model.CustomerOptions = form.CustomerOptions;
            model.CustomerHourlyRates = form.CustomerHourlyRates;
            model.CustomerInvoiceDescriptions = form.CustomerInvoiceDescriptions;
            model.CustomerInvoiceDefaultPrices = form.CustomerInvoiceDefaultPrices;
        }

        private static bool IsTeamDeliverySolution(string? companyName)
        {
            var normalized = new string((companyName ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .ToArray())
                .ToLowerInvariant();
            return normalized.Contains("teamdeliverysolution", StringComparison.Ordinal);
        }

        private async Task<int?> DefaultSupplierIdAsync()
        {
            return await _context.InvoiceCompanies
                .AsNoTracking()
                .Where(c => c.UserId == CurrentUserId && c.IsDefaultSupplier)
                .Select(c => (int?)c.Id)
                .FirstOrDefaultAsync();
        }

        private async Task<bool> CompanyBelongsToUserAsync(int? companyId)
        {
            return companyId.HasValue && await _context.InvoiceCompanies
                .AnyAsync(c => c.Id == companyId.Value && c.UserId == CurrentUserId);
        }

        private async Task<decimal?> LastHourlyRateForCustomerAsync(int? customerCompanyId)
        {
            if (!customerCompanyId.HasValue)
                return null;

            return await _context.InvoiceCompanies
                .AsNoTracking()
                .Where(c => c.Id == customerCompanyId.Value && c.UserId == CurrentUserId)
                .Select(c => c.LastHourlyRate)
                .FirstOrDefaultAsync();
        }

        private async Task RememberHourlyRateAsync(int customerCompanyId, decimal hourlyRate)
        {
            var company = await _context.InvoiceCompanies
                .FirstOrDefaultAsync(c => c.Id == customerCompanyId && c.UserId == CurrentUserId);

            if (company == null)
                return;

            company.LastHourlyRate = hourlyRate;
            await _context.SaveChangesAsync();
        }

        private async Task<bool> IsVatPayerAsync(int? companyId)
        {
            if (!companyId.HasValue)
                return false;

            return await _context.InvoiceCompanies
                .AsNoTracking()
                .Where(c => c.Id == companyId.Value && c.UserId == CurrentUserId)
                .Select(c => c.IsVatPayer)
                .FirstOrDefaultAsync();
        }

        private void ApplyVatRegime(InvoiceFormViewModel model)
        {
            if (model.IsSupplierVatPayer)
                return;

            model.TaxableSupplyDate = null;
            foreach (var item in model.Items)
                item.VatRate = 0m;

            ModelState.Remove(nameof(InvoiceFormViewModel.TaxableSupplyDate));
            for (var i = 0; i < model.Items.Count; i++)
                ModelState.Remove($"Items[{i}].{nameof(InvoiceItemInput.VatRate)}");
        }

        private async Task<string> GenerateInvoiceNumberAsync()
        {
            var prefix = DateTime.Today.ToString("yyyy", CultureInfo.InvariantCulture);
            var count = await _context.Invoices
                .CountAsync(i => i.UserId == CurrentUserId && i.InvoiceNumber.StartsWith(prefix));

            string candidate;
            do
            {
                count++;
                candidate = prefix + count.ToString("0000", CultureInfo.InvariantCulture);
            }
            while (await _context.Invoices.AnyAsync(i => i.UserId == CurrentUserId && i.InvoiceNumber == candidate));

            return candidate;
        }

        private static void NormalizeItems(InvoiceFormViewModel model)
        {
            model.Items = model.Items
                .Where(i => !string.IsNullOrWhiteSpace(i.Description) || i.Quantity != 0 || i.UnitPrice != 0)
                .ToList();
        }

        private void NormalizeHourlyRateFromForm(InvoiceImportViewModel model)
        {
            if (!Request.HasFormContentType || !Request.Form.TryGetValue(nameof(model.HourlyRate), out var rawValues))
                return;

            var raw = rawValues.ToString();
            var parsed = ParseDecimal(raw);
            if (!parsed.HasValue)
                return;

            model.HourlyRate = parsed.Value;
            ModelState.Remove(nameof(model.HourlyRate));
        }

        private void NormalizeInvoiceDecimalFieldsFromForm(InvoiceFormViewModel model)
        {
            if (!Request.HasFormContentType)
                return;

            if (Request.Form.TryGetValue(nameof(model.TotalHoursOverride), out var totalHoursRaw))
            {
                var parsed = ParseDecimal(totalHoursRaw.ToString());
                if (parsed.HasValue)
                {
                    model.TotalHoursOverride = parsed.Value;
                    ModelState.Remove(nameof(model.TotalHoursOverride));
                }
            }

            for (var i = 0; i < model.Items.Count; i++)
            {
                NormalizeInvoiceItemDecimal(model, i, nameof(InvoiceItemInput.Quantity), value => model.Items[i].Quantity = value);
                NormalizeInvoiceItemDecimal(model, i, nameof(InvoiceItemInput.UnitPrice), value => model.Items[i].UnitPrice = value);
                NormalizeInvoiceItemDecimal(model, i, nameof(InvoiceItemInput.VatRate), value => model.Items[i].VatRate = value);
            }
        }

        private void NormalizeInvoiceItemDecimal(InvoiceFormViewModel model, int index, string propertyName, Action<decimal> assign)
        {
            var key = $"Items[{index}].{propertyName}";
            if (!Request.Form.TryGetValue(key, out var rawValues))
                return;

            var parsed = ParseDecimal(rawValues.ToString());
            if (!parsed.HasValue)
                return;

            assign(parsed.Value);
            ModelState.Remove(key);
        }

        private static void ApplyTotalHoursOverride(InvoiceFormViewModel model)
        {
            if (!model.TotalHoursOverride.HasValue || model.TotalHoursOverride.Value <= 0)
                return;

            if (!model.Items.Any() || model.Items.Any(i => !string.Equals(i.Unit, "hod", StringComparison.OrdinalIgnoreCase)))
                return;

            var first = model.Items.First();
            model.Items = new List<InvoiceItemInput>
            {
                new()
                {
                    Description = "Expertní vývojové práce MS.NET",
                    Quantity = model.TotalHoursOverride.Value,
                    Unit = "hod",
                    UnitPrice = first.UnitPrice,
                    VatRate = first.VatRate
                }
            };
        }

        private static List<InvoiceItemInput> ParseInvoiceItemsFromXlsx(Stream stream, decimal hourlyRate, bool supplierIsVatPayer)
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var sharedStrings = ReadSharedStrings(archive);
            var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")
                ?? archive.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase));

            if (sheetEntry == null)
                return new List<InvoiceItemInput>();

            using var sheetStream = sheetEntry.Open();
            var document = XDocument.Load(sheetStream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var rows = document.Descendants(ns + "row")
                .Select(row => row.Elements(ns + "c").ToDictionary(
                    cell => GetColumnIndex(cell.Attribute("r")?.Value),
                    cell => GetCellValue(cell, sharedStrings, ns)))
                .Where(row => row.Values.Any(value => !string.IsNullOrWhiteSpace(value)))
                .ToList();

            if (!rows.Any())
                return new List<InvoiceItemInput>();

            var timesheetItems = BuildTimesheetInvoiceItems(rows, hourlyRate, supplierIsVatPayer);
            if (timesheetItems.Any())
                return timesheetItems;

            var header = rows.First();
            var hasHeader = header.Values.Any(value => LooksLikeHeader(value));
            var map = hasHeader ? BuildHeaderMap(header) : new Dictionary<string, int>();
            var dataRows = hasHeader ? rows.Skip(1) : rows;

            var items = new List<InvoiceItemInput>();
            foreach (var row in dataRows)
            {
                var description = GetMappedValue(row, map, "description", 1);
                if (string.IsNullOrWhiteSpace(description))
                    continue;

                var quantity = ParseDecimal(GetMappedValue(row, map, "quantity", 2)) ?? 1;
                var unit = GetMappedValue(row, map, "unit", 3);
                var unitPrice = ParseDecimal(GetMappedValue(row, map, "unitPrice", 4)) ?? 0;
                var vatRate = supplierIsVatPayer ? ParseDecimal(GetMappedValue(row, map, "vatRate", 5)) ?? 21 : 0;

                items.Add(new InvoiceItemInput
                {
                    Description = description.Trim(),
                    Unit = string.IsNullOrWhiteSpace(unit) ? "ks" : unit.Trim(),
                    Quantity = quantity,
                    UnitPrice = unitPrice,
                    VatRate = vatRate
                });
            }

            return items;
        }

        private static List<InvoiceItemInput> BuildTimesheetInvoiceItems(List<Dictionary<int, string>> rows, decimal hourlyRate, bool supplierIsVatPayer)
        {
            if (hourlyRate <= 0)
                return new List<InvoiceItemInput>();

            var items = new List<InvoiceItemInput>();
            foreach (var row in rows.Skip(1))
            {
                var dateSerial = GetCellDecimal(row, 2);
                var statusCode = GetCellDecimal(row, 3);
                var approvedHours = GetCellDecimal(row, 10) ?? GetCellDecimal(row, 9);

                if (!dateSerial.HasValue || !approvedHours.HasValue || approvedHours.Value <= 0)
                    continue;

                if (statusCode.HasValue && statusCode.Value != 2)
                    continue;

                var date = DateTime.FromOADate((double)dateSerial.Value);
                if (date.Year is < 2000 or > 2100)
                    continue;

                items.Add(new InvoiceItemInput
                {
                    Description = date.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("cs-CZ")),
                    Unit = "hod",
                    Quantity = approvedHours.Value,
                    UnitPrice = hourlyRate,
                    VatRate = supplierIsVatPayer ? 21 : 0
                });
            }

            return items;
        }

        private static decimal? GetCellDecimal(Dictionary<int, string> row, int column)
        {
            return row.TryGetValue(column, out var value) ? ParseDecimal(value) : null;
        }
        private static List<string> ReadSharedStrings(ZipArchive archive)
        {
            var entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry == null)
                return new List<string>();

            using var stream = entry.Open();
            var document = XDocument.Load(stream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            return document.Descendants(ns + "si")
                .Select(si => string.Concat(si.Descendants(ns + "t").Select(t => t.Value)))
                .ToList();
        }

        private static string GetCellValue(XElement cell, IReadOnlyList<string> sharedStrings, XNamespace ns)
        {
            var type = cell.Attribute("t")?.Value;
            var value = cell.Element(ns + "v")?.Value;

            if (type == "s" && int.TryParse(value, out var index) && index >= 0 && index < sharedStrings.Count)
                return sharedStrings[index];

            if (type == "inlineStr")
                return string.Concat(cell.Descendants(ns + "t").Select(t => t.Value));

            return value ?? string.Empty;
        }

        private static int GetColumnIndex(string? cellReference)
        {
            if (string.IsNullOrWhiteSpace(cellReference))
                return 0;

            var index = 0;
            foreach (var ch in cellReference)
            {
                if (!char.IsLetter(ch))
                    break;

                index = index * 26 + char.ToUpperInvariant(ch) - 'A' + 1;
            }

            return index;
        }

        private static bool LooksLikeHeader(string value)
        {
            var normalized = NormalizeHeader(value);
            return normalized is "popis" or "description" or "polozka" or "item" or "mnozstvi" or "quantity" or "cena" or "price";
        }

        private static Dictionary<string, int> BuildHeaderMap(Dictionary<int, string> header)
        {
            var map = new Dictionary<string, int>();
            foreach (var pair in header)
            {
                var normalized = NormalizeHeader(pair.Value);
                var key = normalized switch
                {
                    "popis" or "description" or "polozka" or "item" or "nazev" => "description",
                    "mnozstvi" or "quantity" or "qty" or "pocet" => "quantity",
                    "jednotka" or "unit" or "mj" => "unit",
                    "cena" or "price" or "unitprice" or "jednotkovacena" or "cenazajednotku" => "unitPrice",
                    "dph" or "vat" or "vatrate" or "sazbadph" => "vatRate",
                    _ => string.Empty
                };

                if (!string.IsNullOrWhiteSpace(key))
                    map[key] = pair.Key;
            }

            return map;
        }

        private static string GetMappedValue(Dictionary<int, string> row, Dictionary<string, int> map, string key, int fallbackColumn)
        {
            var column = map.TryGetValue(key, out var mappedColumn) ? mappedColumn : fallbackColumn;
            return row.TryGetValue(column, out var value) ? value : string.Empty;
        }

        private static decimal? ParseDecimal(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var cleaned = new string(value
                    .Replace("%", "", StringComparison.Ordinal)
                    .Where(ch => char.IsDigit(ch) || ch is ',' or '.' or '-' or ' ')
                    .ToArray())
                .Trim();

            if (cleaned.Contains(',') && cleaned.Contains('.'))
                cleaned = cleaned.Replace(" ", "", StringComparison.Ordinal).Replace(".", "", StringComparison.Ordinal);

            var cultures = new[] { CultureInfo.GetCultureInfo("cs-CZ"), CultureInfo.InvariantCulture };

            foreach (var culture in cultures)
            {
                if (decimal.TryParse(cleaned, NumberStyles.Number, culture, out var result))
                    return result;
            }

            return null;
        }

        private static string NormalizeHeader(string value)
        {
            var normalized = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);

            foreach (var ch in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(ch))
                    builder.Append(ch);
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        private static string? GetString(JsonElement element, string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
                return null;

            return property.ValueKind switch
            {
                JsonValueKind.String => property.GetString(),
                JsonValueKind.Number => property.GetRawText(),
                _ => null
            };
        }

        private static string? BuildAresStreet(JsonElement address)
        {
            if (address.ValueKind != JsonValueKind.Object)
                return null;

            var street = GetString(address, "nazevUlice");
            var houseNumber = GetString(address, "cisloDomovni");
            var orientationNumber = GetString(address, "cisloOrientacni");

            var number = string.Join("/", new[] { houseNumber, orientationNumber }.Where(value => !string.IsNullOrWhiteSpace(value)));
            return string.Join(" ", new[] { street, number }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }
    }
}

