using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
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

// ---------------------------------------------------------------------------

/// <summary>
/// MCP tools for the Pohoda invoice (Faktura) XML import API.
/// Wraps the inv:invoice element in a dat:dataPack envelope and POSTs it to the configured Pohoda XML endpoint.
/// Schema: https://www.stormware.sk/xml/schema/version_2/invoice.xsd
/// </summary>
internal sealed class InvoiceTools(IHttpClientFactory httpClientFactory, IConfiguration configuration)
{
    private const string DataNs = "http://www.stormware.cz/schema/version_2/data.xsd";
    private const string InvNs  = "http://www.stormware.cz/schema/version_2/invoice.xsd";
    private const string TypNs  = "http://www.stormware.cz/schema/version_2/type.xsd";

    [McpServerTool]
    [Description(
        "Creates an invoice in Pohoda. " +
        "Supply 'invoiceItemsJson' as a JSON array of objects with fields: " +
        "text (string), quantity (number), unitPrice (number), rateVAT ('none'|'low'|'high'|'third'), unit (string, optional). " +
        "Example: [{\"text\":\"Service\",\"quantity\":1,\"unitPrice\":1000,\"rateVAT\":\"high\"}]")]
    public async Task<string> ImportInvoice(
        [Description(
            "Invoice type. One of: issuedInvoice (default), receivedInvoice, " +
            "issuedAdvanceInvoice, receivedAdvanceInvoice, " +
            "issuedCreditNotice, receivedCreditNotice, " +
            "issuedDebitNotice, receivedDebitNotice, " +
            "receivable, commitment.")] string invoiceType = "issuedInvoice",
        [Description("Invoice number (optional; Pohoda assigns the next number in the series if omitted).")] string? number = null,
        [Description("Date of issue (yyyy-MM-dd). Defaults to today.")] string? date = null,
        [Description("Date of taxable supply (yyyy-MM-dd). Defaults to date of issue.")] string? dateTax = null,
        [Description("Due date (yyyy-MM-dd).")] string? dateDue = null,
        [Description("Invoice description / header text.")] string? text = null,
        [Description("Partner company name.")] string? partnerCompany = null,
        [Description("Partner street address.")] string? partnerStreet = null,
        [Description("Partner city.")] string? partnerCity = null,
        [Description("Partner ZIP / postal code.")] string? partnerZip = null,
        [Description("Two-letter partner country code, e.g. 'CZ' or 'SK'.")] string? partnerCountry = null,
        [Description("Partner company registration number (IČO).")] string? partnerIco = null,
        [Description("Partner VAT registration number (DIČ).")] string? partnerDic = null,
        [Description("Variable symbol (reference number shown on the payment).")] string? symVar = null,
        [Description("Constant symbol.")] string? symConst = null,
        [Description("Internal note.")] string? note = null,
        [Description(
            "Line items as a JSON array. Each object: " +
            "{\"text\":\"…\",\"quantity\":1,\"unitPrice\":100.0,\"rateVAT\":\"high\",\"unit\":\"ks\"}. " +
            "'rateVAT' values: none | low | high | third.")] string? invoiceItemsJson = null)
    {
        var serverUrl  = configuration["Pohoda:ServerUrl"]   ?? throw new InvalidOperationException("Pohoda:ServerUrl is not configured.");
        var username   = configuration["Pohoda:Username"]    ?? string.Empty;
        var password   = configuration["Pohoda:Password"]    ?? string.Empty;
        var companyIco = configuration["Pohoda:Ico"]         ?? string.Empty;
        var appName    = configuration["Pohoda:Application"] ?? "MCP Server";

        InvoiceItemDto[] items = [];
        if (!string.IsNullOrWhiteSpace(invoiceItemsJson))
        {
            items = JsonSerializer.Deserialize(invoiceItemsJson, InvoiceJsonContext.Default.InvoiceItemDtoArray)
                    ?? [];
        }

        var xml = BuildXml(invoiceType, number, date, dateTax, dateDue, text,
                           partnerCompany, partnerStreet, partnerCity, partnerZip, partnerCountry,
                           partnerIco, partnerDic, symVar, symConst, note, items,
                           companyIco, appName);

        return await SendAsync(xml, serverUrl, username, password);
    }

    // -------------------------------------------------------------------------

    private static string BuildXml(
        string invoiceType,
        string? number, string? date, string? dateTax, string? dateDue,
        string? text,
        string? partnerCompany, string? partnerStreet, string? partnerCity,
        string? partnerZip, string? partnerCountry, string? partnerIco, string? partnerDic,
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

            if (number is not null)
            {
                w.WriteStartElement("inv", "number", InvNs);
                w.WriteElementString("typ", "numberRequested", TypNs, number);
                w.WriteEndElement();
            }

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
            bool hasPartner = partnerCompany is not null || partnerStreet is not null
                           || partnerCity is not null    || partnerZip is not null
                           || partnerCountry is not null || partnerIco is not null
                           || partnerDic is not null;
            if (hasPartner)
            {
                w.WriteStartElement("inv", "partnerIdentity", InvNs);
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

    private static void WriteOptional(XmlWriter w, string prefix, string local, string ns, string? value)
    {
        if (value is not null)
            w.WriteElementString(prefix, local, ns, value);
    }

    private async Task<string> SendAsync(string xml, string serverUrl, string username, string password)
    {
        using var client = httpClientFactory.CreateClient();

        if (!string.IsNullOrEmpty(username))
        {
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        using var content = new StringContent(xml, Encoding.UTF8, "text/xml");
        var response = await client.PostAsync(serverUrl, content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            return $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}\n{responseBody}";

        return responseBody;
    }
}
