using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Server;

// ---------------------------------------------------------------------------
// DTO for a single invoice line item – kept internal and simple for AOT safety
// ---------------------------------------------------------------------------

internal sealed class InvoiceItemDto
{
    /// <summary>Item description text.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Quantity.</summary>
    public double Quantity { get; set; } = 1;

    /// <summary>Unit price (without VAT).</summary>
    public double UnitPrice { get; set; }

    /// <summary>VAT rate: none | low | high | third. Defaults to "none".</summary>
    public string RateVAT { get; set; } = "none";

    /// <summary>Optional unit of measure (e.g. "ks", "hod").</summary>
    public string? Unit { get; set; }
}

// Source-generation context required for native-AOT JSON deserialization
[JsonSerializable(typeof(InvoiceItemDto[]))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class InvoiceJsonContext : JsonSerializerContext { }

internal sealed record InvoiceImportRequest(
    string InvoiceType,
    string? Number,
    string? Date,
    string? DateTax,
    string? DateDue,
    string? Text,
    string? PartnerCompany,
    string? PartnerStreet,
    string? PartnerCity,
    string? PartnerZip,
    string? PartnerCountry,
    string? PartnerIco,
    string? PartnerDic,
    string? SymVar,
    string? SymConst,
    string? Note,
    string? InvoiceItemsJson);

// ---------------------------------------------------------------------------

/// <summary>
/// MCP tools for the Pohoda invoice (Faktura) XML import API.
/// Wraps the inv:invoice element in a dat:dataPack envelope and POSTs it to the configured Pohoda XML endpoint.
/// Schema: https://www.stormware.sk/xml/schema/version_2/invoice.xsd
/// </summary>
internal sealed class InvoiceTools(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    : PohodaToolBase(httpClientFactory, configuration)
{
    private const string DataNs = "http://www.stormware.cz/schema/version_2/data.xsd";
    private const string InvNs  = "http://www.stormware.cz/schema/version_2/invoice.xsd";
    private const string TypNs  = "http://www.stormware.cz/schema/version_2/type.xsd";
    private const string ListNs = "http://www.stormware.cz/schema/version_2/list.xsd";

    private const string PartnerIcoFilterDescription = "Optional partner ICO filter (exact match).";
    private const string NumberContainsDescription = "Optional document number filter (case-insensitive contains).";
    private const string NumberDescription = "Document number (optional; Pohoda assigns the next number in the series if omitted).";
    private const string DateDescription = "Date of issue (yyyy-MM-dd). Defaults to today.";
    private const string DateTaxDescription = "Date of taxable supply (yyyy-MM-dd). Defaults to date of issue.";
    private const string DateDueDescription = "Due date (yyyy-MM-dd).";
    private const string PartnerCompanyDescription = "Partner company name.";
    private const string PartnerStreetDescription = "Partner street address.";
    private const string PartnerCityDescription = "Partner city.";
    private const string PartnerZipDescription = "Partner ZIP / postal code.";
    private const string PartnerCountryDescription = "Two-letter partner country code, e.g. 'CZ' or 'SK'.";
    private const string PartnerIcoDescription = "Partner company registration number (IČO).";
    private const string PartnerDicDescription = "Partner VAT registration number (DIČ).";
    private const string SymVarDescription = "Variable symbol (reference number shown on the payment).";
    private const string SymConstDescription = "Constant symbol.";
    private const string NoteDescription = "Internal note.";
    private const string InvoiceItemsDescription =
        "Line items as a JSON array. Each object: " +
        "{\"text\":\"…\",\"quantity\":1,\"unitPrice\":100.0,\"rateVAT\":\"high\",\"unit\":\"ks\"}. " +
        "'rateVAT' values: none | low | high | third.";

    [McpServerTool]
    [Description("Returns received invoices from Pohoda as JSON. Optional filters can be applied by partner ICO and/or invoice number substring.")]
    public Task<string> ListReceivedInvoices(
        [Description(PartnerIcoFilterDescription)]
        string? partnerIco = null,
        [Description(NumberContainsDescription)]
        string? numberContains = null)
        => ListInvoicesAsync("receivedInvoice", partnerIco, numberContains);

    [McpServerTool]
    [Description("Returns issued invoices from Pohoda as JSON. Optional filters can be applied by partner ICO and/or invoice number substring.")]
    public Task<string> ListIssuedInvoices(
        [Description(PartnerIcoFilterDescription)]
        string? partnerIco = null,
        [Description(NumberContainsDescription)]
        string? numberContains = null)
        => ListInvoicesAsync("issuedInvoice", partnerIco, numberContains);

    [McpServerTool]
    [Description("Returns commitments (ostatne zavazky) from Pohoda as JSON. Optional filters can be applied by partner ICO and/or document number substring.")]
    public Task<string> ListCommitments(
        [Description(PartnerIcoFilterDescription)]
        string? partnerIco = null,
        [Description(NumberContainsDescription)]
        string? numberContains = null)
        => ListInvoicesAsync("commitment", partnerIco, numberContains);

    [McpServerTool]
    [Description("Returns receivables (ostatne pohladavky) from Pohoda as JSON. Optional filters can be applied by partner ICO and/or document number substring.")]
    public Task<string> ListReceivables(
        [Description(PartnerIcoFilterDescription)]
        string? partnerIco = null,
        [Description(NumberContainsDescription)]
        string? numberContains = null)
        => ListInvoicesAsync("receivable", partnerIco, numberContains);

    [McpServerTool]
    [Description(
        "Creates an invoice in Pohoda. " +
        "Supply 'invoiceItemsJson' as a JSON array of objects with fields: " +
        "text (string), quantity (number), unitPrice (number), rateVAT ('none'|'low'|'high'|'third'), unit (string, optional). " +
        "Example: [{\"text\":\"Service\",\"quantity\":1,\"unitPrice\":1000,\"rateVAT\":\"high\"}]")]
    public Task<string> ImportInvoice(
        [Description(
            "Invoice type. One of: issuedInvoice (default), receivedInvoice, " +
            "issuedAdvanceInvoice, receivedAdvanceInvoice, " +
            "issuedCreditNotice, receivedCreditNotice, " +
            "issuedDebitNotice, receivedDebitNotice, " +
            "receivable, commitment.")] string invoiceType = "issuedInvoice",
        [Description(NumberDescription)] string? number = null,
        [Description(DateDescription)] string? date = null,
        [Description(DateTaxDescription)] string? dateTax = null,
        [Description(DateDueDescription)] string? dateDue = null,
        [Description("Invoice description / header text.")] string? text = null,
        [Description(PartnerCompanyDescription)] string? partnerCompany = null,
        [Description(PartnerStreetDescription)] string? partnerStreet = null,
        [Description(PartnerCityDescription)] string? partnerCity = null,
        [Description(PartnerZipDescription)] string? partnerZip = null,
        [Description(PartnerCountryDescription)] string? partnerCountry = null,
        [Description(PartnerIcoDescription)] string? partnerIco = null,
        [Description(PartnerDicDescription)] string? partnerDic = null,
        [Description(SymVarDescription)] string? symVar = null,
        [Description(SymConstDescription)] string? symConst = null,
        [Description(NoteDescription)] string? note = null,
        [Description(InvoiceItemsDescription)] string? invoiceItemsJson = null)
        => ImportInvoiceAsync(new InvoiceImportRequest(
            invoiceType,
            number,
            date,
            dateTax,
            dateDue,
            text,
            partnerCompany,
            partnerStreet,
            partnerCity,
            partnerZip,
            partnerCountry,
            partnerIco,
            partnerDic,
            symVar,
            symConst,
            note,
            invoiceItemsJson));

    [McpServerTool]
    [Description(
        "Creates a commitment (ostatny zavazok) in Pohoda using the invoice schema with invoiceType 'commitment'. " +
        "Supply 'invoiceItemsJson' as a JSON array of objects with fields: " +
        "text (string), quantity (number), unitPrice (number), rateVAT ('none'|'low'|'high'|'third'), unit (string, optional). " +
        "If partner details are provided, the tool first looks for an address book entry and binds it; if none exists, it creates one and then binds the commitment to it.")]
    public Task<string> ImportCommitment(
        [Description(NumberDescription)]
        string? number = null,
        [Description(DateDescription)]
        string? date = null,
        [Description(DateTaxDescription)]
        string? dateTax = null,
        [Description(DateDueDescription)]
        string? dateDue = null,
        [Description("Commitment description / header text.")]
        string? text = null,
        [Description(PartnerCompanyDescription)]
        string? partnerCompany = null,
        [Description(PartnerStreetDescription)]
        string? partnerStreet = null,
        [Description(PartnerCityDescription)]
        string? partnerCity = null,
        [Description(PartnerZipDescription)]
        string? partnerZip = null,
        [Description(PartnerCountryDescription)]
        string? partnerCountry = null,
        [Description(PartnerIcoDescription)]
        string? partnerIco = null,
        [Description(PartnerDicDescription)]
        string? partnerDic = null,
        [Description(SymVarDescription)]
        string? symVar = null,
        [Description(SymConstDescription)]
        string? symConst = null,
        [Description(NoteDescription)]
        string? note = null,
        [Description(InvoiceItemsDescription)]
        string? invoiceItemsJson = null)
        => ImportInvoiceAsync(new InvoiceImportRequest(
            "commitment",
            number,
            date,
            dateTax,
            dateDue,
            text,
            partnerCompany,
            partnerStreet,
            partnerCity,
            partnerZip,
            partnerCountry,
            partnerIco,
            partnerDic,
            symVar,
            symConst,
            note,
            invoiceItemsJson));

    [McpServerTool]
    [Description(
        "Creates a receivable (ostatna pohladavka) in Pohoda using the invoice schema with invoiceType 'receivable'. " +
        "Supply 'invoiceItemsJson' as a JSON array of objects with fields: " +
        "text (string), quantity (number), unitPrice (number), rateVAT ('none'|'low'|'high'|'third'), unit (string, optional). " +
        "If partner details are provided, the tool first looks for an address book entry and binds it; if none exists, it creates one and then binds the receivable to it.")]
    public Task<string> ImportReceivable(
        [Description(NumberDescription)]
        string? number = null,
        [Description(DateDescription)]
        string? date = null,
        [Description(DateTaxDescription)]
        string? dateTax = null,
        [Description(DateDueDescription)]
        string? dateDue = null,
        [Description("Receivable description / header text.")]
        string? text = null,
        [Description(PartnerCompanyDescription)]
        string? partnerCompany = null,
        [Description(PartnerStreetDescription)]
        string? partnerStreet = null,
        [Description(PartnerCityDescription)]
        string? partnerCity = null,
        [Description(PartnerZipDescription)]
        string? partnerZip = null,
        [Description(PartnerCountryDescription)]
        string? partnerCountry = null,
        [Description(PartnerIcoDescription)]
        string? partnerIco = null,
        [Description(PartnerDicDescription)]
        string? partnerDic = null,
        [Description(SymVarDescription)]
        string? symVar = null,
        [Description(SymConstDescription)]
        string? symConst = null,
        [Description(NoteDescription)]
        string? note = null,
        [Description(InvoiceItemsDescription)]
        string? invoiceItemsJson = null)
        => ImportInvoiceAsync(new InvoiceImportRequest(
            "receivable",
            number,
            date,
            dateTax,
            dateDue,
            text,
            partnerCompany,
            partnerStreet,
            partnerCity,
            partnerZip,
            partnerCountry,
            partnerIco,
            partnerDic,
            symVar,
            symConst,
            note,
            invoiceItemsJson));

    [McpServerTool]
    [Description(
        "Cancels (stornos) an invoice in Pohoda by creating a reversal document. " +
        "Supply either 'id' (Pohoda internal record ID) or 'number' (document number) to identify the invoice to cancel. " +
        "'invoiceType' must match the type of the original document.")]
    public async Task<string> CancelInvoice(
        [Description("Invoice type of the document to cancel: issuedInvoice | receivedInvoice | issuedAdvanceInvoice | receivedAdvanceInvoice | issuedCreditNotice | receivedCreditNotice | issuedDebitNotice | receivedDebitNotice | receivable | commitment.")]
        string invoiceType,
        [Description("Pohoda internal record ID of the invoice to cancel.")]
        string? id = null,
        [Description("Document number of the invoice to cancel (e.g. 'F2024001').")]
        string? number = null)
    {
        if (string.IsNullOrWhiteSpace(invoiceType))
            throw new ArgumentException("A non-empty invoice type is required.", nameof(invoiceType));

        if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(number))
            throw new ArgumentException("Either 'id' or 'number' must be provided to identify the invoice to cancel.");

        invoiceType = invoiceType.Trim();

        var (serverUrl, username, password, companyIco, appName) = GetPohodaSettings();
        var xml = BuildCancelXml(invoiceType, id, number, companyIco, appName);
        return await SendAsync(xml, serverUrl, username, password);
    }

    // -------------------------------------------------------------------------

    private async Task<string> ListInvoicesAsync(string invoiceType, string? partnerIco, string? numberContains)
    {
        var (serverUrl, username, password, companyIco, appName) = GetPohodaSettings();

        var xml = BuildListInvoicesXml(invoiceType, companyIco, appName);
        var responseXml = await SendAsync(xml, serverUrl, username, password);

        if (responseXml.StartsWith("HTTP ", StringComparison.Ordinal))
            return responseXml;

        return ParseInvoices(responseXml, partnerIco, numberContains);
    }

    private async Task<string> ImportInvoiceAsync(InvoiceImportRequest request)
    {
        var (serverUrl, username, password, companyIco, appName) = GetPohodaSettings();

        var normalizedSymVar = NormalizeSymVar(request.Number, request.SymVar);
        var normalizedSymConst = NormalizeOptionalSymbol(nameof(request.SymConst), request.SymConst);

        var supplierAddressbookId = await EnsureSupplierAndGetAddressbookIdAsync(
            new SupplierInfo(
                request.PartnerCompany,
                null,
                request.PartnerStreet,
                request.PartnerCity,
                request.PartnerZip,
                request.PartnerCountry,
                request.PartnerIco,
                request.PartnerDic));

        var items = DeserializeInvoiceItems(request.InvoiceItemsJson);
        decimal? expectedTotal = items.Length > 0
            ? items.Sum(i => (decimal)i.Quantity * (decimal)i.UnitPrice)
            : null;

        if (normalizedSymVar is not null && expectedTotal is not null &&
            (!string.IsNullOrWhiteSpace(request.PartnerIco) || !string.IsNullOrWhiteSpace(request.PartnerCompany)))
        {
            var exists = await InvoiceExistsAsync(
                request.InvoiceType,
                normalizedSymVar,
                request.PartnerIco,
                request.PartnerCompany,
                expectedTotal.Value,
                serverUrl,
                username,
                password,
                companyIco,
                appName);

            if (exists)
            {
                return $"Skipped import: invoice with symVar '{normalizedSymVar}' for supplier already exists with total {expectedTotal.Value:F2}.";
            }
        }

        var xml = BuildXml(
            request.InvoiceType,
            request.Date,
            request.DateTax,
            request.DateDue,
            request.Text,
            request.PartnerCompany,
            request.PartnerStreet,
            request.PartnerCity,
            request.PartnerZip,
            request.PartnerCountry,
            request.PartnerIco,
            request.PartnerDic,
            supplierAddressbookId,
            normalizedSymVar,
            normalizedSymConst,
            request.Note,
            items,
            companyIco,
            appName);

        return await SendAsync(xml, serverUrl, username, password);
    }

    private static string? NormalizeSymVar(string? number, string? symVar)
    {
        if (!string.IsNullOrWhiteSpace(number))
        {
            var digitsOnly = new string(number.Where(char.IsDigit).ToArray());
            return digitsOnly.Length switch
            {
                0 => null,
                > 10 => digitsOnly[..10],
                _ => digitsOnly,
            };
        }

        return NormalizeOptionalSymbol(nameof(symVar), symVar);
    }

    private static InvoiceItemDto[] DeserializeInvoiceItems(string? invoiceItemsJson)
    {
        if (string.IsNullOrWhiteSpace(invoiceItemsJson))
            return [];

        try
        {
            return JsonSerializer.Deserialize(invoiceItemsJson, InvoiceJsonContext.Default.InvoiceItemDtoArray)
                   ?? [];
        }
        catch (JsonException ex)
        {
            throw new ArgumentException(
                "Parameter 'invoiceItemsJson' must be a JSON array of objects like " +
                "[{\"text\":\"Service\",\"quantity\":1,\"unitPrice\":1000,\"rateVAT\":\"high\",\"unit\":\"ks\"}].",
                nameof(invoiceItemsJson),
                ex);
        }
    }

    private static string BuildXml(
        string invoiceType,
        string? date, string? dateTax, string? dateDue,
        string? text,
        string? partnerCompany, string? partnerStreet, string? partnerCity,
        string? partnerZip, string? partnerCountry, string? partnerIco, string? partnerDic,
        string? partnerAddressbookId,
        string? symVar, string? symConst, string? note,
        InvoiceItemDto[] items,
        string companyIco, string appName)
    {
        var effectiveDate    = date    ?? DateTime.Today.ToString("yyyy-MM-dd");
        var effectiveDateTax = dateTax ?? effectiveDate;

        var ms = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        using (var w = XmlWriter.Create(ms, settings))
        {
            // <dat:dataPack …>
            w.WriteStartElement("dat", "dataPack", DataNs);
            w.WriteAttributeString("id",          "001");
            w.WriteAttributeString("ico",         companyIco);
            w.WriteAttributeString("application", appName);
            w.WriteAttributeString("version",     "2.0");
            w.WriteAttributeString("note",        string.Empty);

            // <dat:dataPackItem …>
            w.WriteStartElement("dat", "dataPackItem", DataNs);
            w.WriteAttributeString("id",      "001");
            w.WriteAttributeString("version", "2.0");

            // <inv:invoice version="2.0">
            w.WriteStartElement("inv", "invoice", InvNs);
            w.WriteAttributeString("version", "2.0");

            // ── invoiceHeader ─────────────────────────────────────────────
            w.WriteStartElement("inv", "invoiceHeader", InvNs);

            w.WriteElementString("inv", "invoiceType", InvNs, invoiceType);

            w.WriteElementString("inv", "date",    InvNs, effectiveDate);
            w.WriteElementString("inv", "dateTax", InvNs, effectiveDateTax);

            if (dateDue is not null)
                w.WriteElementString("inv", "dateDue", InvNs, dateDue);

            if (text is not null)
                w.WriteElementString("inv", "text", InvNs, text);

            if (symVar is not null)
                w.WriteElementString("inv", "symVar", InvNs, symVar);

            if (symConst is not null)
                w.WriteElementString("inv", "symConst", InvNs, symConst);

            // <inv:partnerIdentity>
            bool hasPartner = partnerAddressbookId is not null || partnerCompany is not null || partnerStreet is not null
                           || partnerCity is not null    || partnerZip is not null
                           || partnerCountry is not null || partnerIco is not null
                           || partnerDic is not null;
            if (hasPartner)
            {
                w.WriteStartElement("inv", "partnerIdentity", InvNs);
                if (partnerAddressbookId is not null)
                {
                    w.WriteElementString("typ", "id", TypNs, partnerAddressbookId);
                }
                else
                {
                    w.WriteStartElement("typ", "address", TypNs);
                    WriteOptional(w, "typ", "company", TypNs, partnerCompany);
                    WriteOptional(w, "typ", "street",  TypNs, partnerStreet);
                    WriteOptional(w, "typ", "city",    TypNs, partnerCity);
                    WriteOptional(w, "typ", "zip",     TypNs, partnerZip);
                    if (partnerCountry is not null)
                    {
                        w.WriteStartElement("typ", "country", TypNs);
                        w.WriteElementString("typ", "ids", TypNs, partnerCountry);
                        w.WriteEndElement();
                    }
                    WriteOptional(w, "typ", "ico", TypNs, partnerIco);
                    WriteOptional(w, "typ", "dic", TypNs, partnerDic);
                    w.WriteEndElement(); // address
                }
                w.WriteEndElement(); // partnerIdentity
            }

            if (note is not null)
                w.WriteElementString("inv", "note", InvNs, note);

            w.WriteEndElement(); // invoiceHeader

            // ── invoiceDetail ─────────────────────────────────────────────
            if (items.Length > 0)
            {
                w.WriteStartElement("inv", "invoiceDetail", InvNs);
                foreach (var item in items)
                {
                    w.WriteStartElement("inv", "invoiceItem", InvNs);
                    w.WriteElementString("inv", "text",     InvNs, item.Text);
                    w.WriteElementString("inv", "quantity", InvNs, item.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (item.Unit is not null)
                        w.WriteElementString("inv", "unit", InvNs, item.Unit);
                    w.WriteElementString("inv", "rateVAT", InvNs, item.RateVAT);
                    w.WriteStartElement("inv", "homeCurrency", InvNs);
                    w.WriteElementString("typ", "unitPrice", TypNs,
                        item.UnitPrice.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                    w.WriteEndElement(); // homeCurrency
                    w.WriteEndElement(); // invoiceItem
                }
                w.WriteEndElement(); // invoiceDetail
            }

            w.WriteEndElement(); // invoice
            w.WriteEndElement(); // dataPackItem
            w.WriteEndElement(); // dataPack
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string BuildCancelXml(
        string invoiceType,
        string? id,
        string? number,
        string companyIco,
        string appName)
    {
        var ms = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        using (var w = XmlWriter.Create(ms, settings))
        {
            w.WriteStartElement("dat", "dataPack", DataNs);
            w.WriteAttributeString("id",          "001");
            w.WriteAttributeString("ico",         companyIco);
            w.WriteAttributeString("application", appName);
            w.WriteAttributeString("version",     "2.0");
            w.WriteAttributeString("note",        string.Empty);

            w.WriteStartElement("dat", "dataPackItem", DataNs);
            w.WriteAttributeString("id",      "001");
            w.WriteAttributeString("version", "2.0");

            w.WriteStartElement("inv", "invoice", InvNs);
            w.WriteAttributeString("version", "2.0");

            // cancelDocument identifies the document to be storno-ed
            w.WriteStartElement("inv", "cancelDocument", InvNs);
            w.WriteStartElement("typ", "sourceDocument", TypNs);
            if (!string.IsNullOrWhiteSpace(id))
                w.WriteElementString("typ", "id", TypNs, id);
            if (!string.IsNullOrWhiteSpace(number))
                w.WriteElementString("typ", "number", TypNs, number);
            w.WriteEndElement(); // typ:sourceDocument
            w.WriteEndElement(); // inv:cancelDocument

            // invoiceHeader is required to specify the invoice type
            w.WriteStartElement("inv", "invoiceHeader", InvNs);
            w.WriteElementString("inv", "invoiceType", InvNs, invoiceType);
            w.WriteEndElement(); // inv:invoiceHeader

            w.WriteEndElement(); // inv:invoice
            w.WriteEndElement(); // dat:dataPackItem
            w.WriteEndElement(); // dat:dataPack
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private async Task<bool> InvoiceExistsAsync(
        string invoiceType,
        string symVar,
        string? partnerIco,
        string? partnerCompany,
        decimal expectedTotal,
        string serverUrl,
        string username,
        string password,
        string companyIco,
        string appName)
    {
        var xml = BuildListInvoicesXml(invoiceType, companyIco, appName);
        var responseXml = await SendAsync(xml, serverUrl, username, password);
        if (responseXml.StartsWith("HTTP ", StringComparison.Ordinal))
            throw new InvalidOperationException($"Invoice lookup failed: {responseXml}");

        var rows = ParseInvoiceRows(responseXml);
        return rows.Any(x =>
            string.Equals(x.SymVar, symVar, StringComparison.OrdinalIgnoreCase) &&
            SupplierMatches(x, partnerIco, partnerCompany) &&
            x.TotalHome is not null &&
            Math.Abs(x.TotalHome.Value - expectedTotal) <= 0.01m);
    }

    private static bool SupplierMatches(InvoiceRow row, string? partnerIco, string? partnerCompany)
    {
        if (!string.IsNullOrWhiteSpace(partnerIco))
            return string.Equals(row.PartnerIco, partnerIco, StringComparison.OrdinalIgnoreCase);

        return !string.IsNullOrWhiteSpace(partnerCompany) &&
               string.Equals(row.PartnerCompany, partnerCompany, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildListInvoicesXml(string invoiceType, string companyIco, string appName)
    {
        var ms = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        using (var w = XmlWriter.Create(ms, settings))
        {
            w.WriteStartElement("dat", "dataPack", DataNs);
            w.WriteAttributeString("id", $"list-{invoiceType}");
            w.WriteAttributeString("ico", companyIco);
            w.WriteAttributeString("application", appName);
            w.WriteAttributeString("version", "2.0");
            w.WriteAttributeString("note", $"list-{invoiceType}");

            w.WriteStartElement("dat", "dataPackItem", DataNs);
            w.WriteAttributeString("id", "1");
            w.WriteAttributeString("version", "2.0");

            w.WriteStartElement("lst", "listInvoiceRequest", ListNs);
            w.WriteAttributeString("version", "2.0");
            w.WriteAttributeString("invoiceType", invoiceType);
            w.WriteAttributeString("invoiceVersion", "2.0");

            w.WriteStartElement("lst", "requestInvoice", ListNs);
            w.WriteEndElement(); // requestInvoice

            w.WriteEndElement(); // listInvoiceRequest
            w.WriteEndElement(); // dataPackItem
            w.WriteEndElement(); // dataPack
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string ParseReceivedInvoices(string responseXml, string? partnerIcoFilter, string? numberContainsFilter)
        => ParseInvoices(responseXml, partnerIcoFilter, numberContainsFilter);

    private static string ParseInvoices(string responseXml, string? partnerIcoFilter, string? numberContainsFilter)
    {
        var items = ParseInvoiceRows(responseXml)
            .Where(x => string.IsNullOrWhiteSpace(partnerIcoFilter) || string.Equals(x.partnerIco, partnerIcoFilter, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(numberContainsFilter) ||
                        (!string.IsNullOrWhiteSpace(x.number) && x.number.Contains(numberContainsFilter, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("[");

        for (var i = 0; i < items.Count; i++)
        {
            var x = items[i];
            sb.AppendLine("  {");
            sb.AppendLine($"    \"id\": {ToJsonString(x.id)},");
            sb.AppendLine($"    \"number\": {ToJsonString(x.number)},");
            sb.AppendLine($"    \"date\": {ToJsonString(x.date)},");
            sb.AppendLine($"    \"dateDue\": {ToJsonString(x.dateDue)},");
            sb.AppendLine($"    \"text\": {ToJsonString(x.text)},");
            sb.AppendLine($"    \"symVar\": {ToJsonString(x.symVar)},");
            sb.AppendLine($"    \"partnerCompany\": {ToJsonString(x.partnerCompany)},");
            sb.AppendLine($"    \"partnerIco\": {ToJsonString(x.partnerIco)},");
            sb.AppendLine($"    \"sumWithoutVat\": {ToJsonString(x.sumWithoutVat)},");
            sb.AppendLine($"    \"vat\": {ToJsonString(x.vat)},");
            sb.AppendLine($"    \"totalHome\": {ToJsonString(x.totalHome)}");
            sb.Append("  }");
            if (i < items.Count - 1)
                sb.Append(',');
            sb.AppendLine();
        }

        sb.Append(']');
        return sb.ToString();
    }

    private sealed record InvoiceRow(
        string? id,
        string? number,
        string? date,
        string? dateDue,
        string? text,
        string? symVar,
        string? partnerCompany,
        string? partnerIco,
        string? sumWithoutVat,
        string? vat,
        string? totalHome,
        decimal? SumWithoutVat,
        decimal? Vat,
        decimal? TotalHome)
    {
        public string? Number => number;
        public string? SymVar => symVar;
        public string? PartnerCompany => partnerCompany;
        public string? PartnerIco => partnerIco;
    }

    private static List<InvoiceRow> ParseInvoiceRows(string responseXml)
    {
        var doc = XDocument.Parse(responseXml);

        XNamespace lst = ListNs;
        XNamespace inv = InvNs;
        XNamespace typ = TypNs;

        return doc
            .Descendants(lst + "invoice")
            .Select(invoice =>
            {
                var header = invoice.Element(inv + "invoiceHeader");
                var partnerAddress = header
                    ?.Element(inv + "partnerIdentity")
                    ?.Element(typ + "address");

                var numberNode = header?.Element(inv + "number");
                var number = (string?)numberNode?.Element(typ + "numberRequested")
                          ?? (string?)numberNode?.Element(typ + "numberIssued")
                          ?? (string?)numberNode?.Element(typ + "numberReceived")
                          ?? (string?)numberNode?.Element(typ + "number")
                          ?? numberNode?.Value;

                var summary = invoice.Element(inv + "invoiceSummary");
                var homeCurrency = summary?.Element(inv + "homeCurrency");
                var sumWithoutVatValue = SumCurrencyValues(
                    homeCurrency,
                    typ + "priceNone",
                    typ + "priceLow",
                    typ + "priceHigh",
                    typ + "price3");

                var vatValue = SumCurrencyValues(
                    homeCurrency,
                    typ + "priceLowVAT",
                    typ + "priceHighVAT",
                    typ + "price3VAT");

                var sumWithVatBuckets = SumCurrencyValues(
                    homeCurrency,
                    typ + "priceNone",
                    typ + "priceLowSum",
                    typ + "priceHighSum",
                    typ + "price3Sum");

                var totalValue = sumWithVatBuckets ??
                                 ((sumWithoutVatValue ?? 0m) + (vatValue ?? 0m));

                var sumWithoutVat = ToInvariantString(sumWithoutVatValue);
                var vat = ToInvariantString(vatValue);
                var total = ToInvariantString(totalValue);

                return new InvoiceRow(
                    (string?)header?.Element(inv + "id"),
                    number,
                    (string?)header?.Element(inv + "date"),
                    (string?)header?.Element(inv + "dateDue"),
                    (string?)header?.Element(inv + "text"),
                    (string?)header?.Element(inv + "symVar"),
                    (string?)partnerAddress?.Element(typ + "company"),
                    (string?)partnerAddress?.Element(typ + "ico"),
                    sumWithoutVat,
                    vat,
                    total,
                    sumWithoutVatValue,
                    vatValue,
                    ParseDecimalSafe(total));
            })
            .ToList();
    }

    private static decimal? SumCurrencyValues(XElement? homeCurrency, params XName[] elementNames)
    {
        if (homeCurrency is null)
            return null;

        decimal sum = 0m;
        var hasAnyValue = false;

        foreach (var elementName in elementNames)
        {
            var value = ParseDecimalSafe((string?)homeCurrency.Element(elementName));
            if (value is null)
                continue;

            hasAnyValue = true;
            sum += value.Value;
        }

        return hasAnyValue ? sum : null;
    }

    private static string? ToInvariantString(decimal? value)
        => value?.ToString("0.##", CultureInfo.InvariantCulture);

    private static decimal? ParseDecimalSafe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Replace(" ", string.Empty).Replace(',', '.');
        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        return null;
    }

}
