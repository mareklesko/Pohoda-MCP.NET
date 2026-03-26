using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Server;

internal sealed class InternalDocumentItemDto
{
    public string Text { get; set; } = string.Empty;

    public double Quantity { get; set; } = 1;

    public double UnitPrice { get; set; }

    public string RateVAT { get; set; } = "none";

    public string? Unit { get; set; }

    public string? Note { get; set; }

    public string? Accounting { get; set; }

    public string? ClassificationVatType { get; set; }

    public string? SymPar { get; set; }

    public string? Centre { get; set; }

    public string? Activity { get; set; }

    public string? Contract { get; set; }
}

[JsonSerializable(typeof(InternalDocumentItemDto[]))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class InternalDocumentJsonContext : JsonSerializerContext { }

/// <summary>
/// MCP tools for the Pohoda internal document XML import/list API.
/// Import schema namespace: https://www.stormware.cz/schema/version_2/intDoc.xsd
/// List schema namespace: https://www.stormware.cz/schema/version_2/list.xsd
/// </summary>
internal sealed class InternalDocumentTools(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    : PohodaToolBase(httpClientFactory, configuration)
{
    private const string DataNs = "http://www.stormware.cz/schema/version_2/data.xsd";
    private const string IntNs = "http://www.stormware.cz/schema/version_2/intDoc.xsd";
    private const string TypNs = "http://www.stormware.cz/schema/version_2/type.xsd";
    private const string ListNs = "http://www.stormware.cz/schema/version_2/list.xsd";

    [McpServerTool]
    [Description("Returns internal documents from Pohoda as JSON. Optional filters can be applied by partner ICO, document number substring, and text substring.")]
    public async Task<string> ListInternalDocuments(
        [Description("Optional partner ICO filter (exact match).")]
        string? partnerIco = null,
        [Description("Optional document number filter (case-insensitive contains).")]
        string? numberContains = null,
        [Description("Optional document text filter (case-insensitive contains).")]
        string? textContains = null)
    {
        var (serverUrl, username, password, companyIco, appName) = GetPohodaSettings();

        var xml = BuildListInternalDocumentsXml(companyIco, appName);
        var responseXml = await SendAsync(xml, serverUrl, username, password);

        if (responseXml.StartsWith("HTTP ", StringComparison.Ordinal))
            return responseXml;

        return ParseInternalDocuments(responseXml, partnerIco, numberContains, textContains);
    }

    [McpServerTool]
    [Description(
        "Creates an internal document in Pohoda. " +
        "If supplier/customer details are provided, the tool first looks for an address book entry and binds it; if none exists, it creates one and then binds the document to it. " +
        "Supply 'internalDocumentItemsJson' as a JSON array of objects with fields: " +
        "text (string), quantity (number), unitPrice (number), rateVAT ('none'|'low'|'high'|'third'), unit (string, optional), note (string, optional), accounting (string, optional). " +
        "Example: [{\"text\":\"Travel settlement\",\"quantity\":1,\"unitPrice\":150,\"rateVAT\":\"none\"}]")]
    public async Task<string> ImportInternalDocument(
        [Description("Document number (optional; Pohoda assigns the next number in series if omitted).")]
        string? number = null,
        [Description("Date of issue (yyyy-MM-dd). Defaults to today.")]
        string? date = null,
        [Description("Date of taxable supply (yyyy-MM-dd). Defaults to date.")]
        string? dateTax = null,
        [Description("Accounting date (yyyy-MM-dd). Defaults to date.")]
        string? dateAccounting = null,
        [Description("Header text / description.")]
        string? text = null,
        [Description("Partner company name.")]
        string? partnerCompany = null,
        [Description("Partner person name.")]
        string? partnerName = null,
        [Description("Partner street address.")]
        string? partnerStreet = null,
        [Description("Partner city.")]
        string? partnerCity = null,
        [Description("Partner ZIP / postal code.")]
        string? partnerZip = null,
        [Description("Two-letter partner country code, e.g. 'CZ' or 'SK'.")]
        string? partnerCountry = null,
        [Description("Partner company registration number (IČO).")]
        string? partnerIco = null,
        [Description("Partner VAT registration number (DIČ).")]
        string? partnerDic = null,
        [Description("Accounting preset code (typ:ids), e.g. '9Int'.")]
        string? accounting = null,
        [Description("VAT classification type, e.g. 'nonSubsume' or 'inland'.")]
        string? classificationVatType = null,
        [Description("Variable symbol.")]
        string? symVar = null,
        [Description("Pairing symbol.")]
        string? symPar = null,
        [Description("Centre code (typ:ids).")]
        string? centre = null,
        [Description("Activity code (typ:ids).")]
        string? activity = null,
        [Description("Contract code (typ:ids).")]
        string? contract = null,
        [Description("Optional source liquidation ID for tax-document linkage, as used in liquidation-linked internal documents.")]
        string? sourceLiquidationId = null,
        [Description("Public note.")]
        string? note = null,
        [Description("Internal note.")]
        string? intNote = null,
        [Description("Optional header summary amount in home currency (written to intDocSummary/homeCurrency/priceNone).")]
        double? homeAmount = null,
        [Description("Optional foreign currency code for header-only foreign-currency summary, e.g. 'EUR'.")]
        string? foreignCurrencyCode = null,
        [Description("Optional foreign currency amount factor, e.g. 1.")]
        double? foreignCurrencyAmount = null,
        [Description("Optional foreign currency total amount (written to intDocSummary/foreignCurrency/priceSum).")]
        double? foreignCurrencyPriceSum = null,
        [Description(
            "Internal document items as JSON array. Each object: " +
            "{\"text\":\"…\",\"quantity\":1,\"unitPrice\":100.0,\"rateVAT\":\"none\",\"unit\":\"ks\",\"accounting\":\"1Int\",\"note\":\"…\"}. " +
            "'rateVAT' values: none | low | high | third.")]
        string? internalDocumentItemsJson = null)
    {
        var (serverUrl, username, password, companyIco, appName) = GetPohodaSettings();

        symVar = NormalizeOptionalSymbol(nameof(symVar), symVar);
        symPar = NormalizeOptionalSymbol(nameof(symPar), symPar);

        var partnerAddressbookId = await EnsureSupplierAndGetAddressbookIdAsync(
            new SupplierInfo(
                partnerCompany,
                partnerName,
                partnerStreet,
                partnerCity,
                partnerZip,
                partnerCountry,
                partnerIco,
                partnerDic));

        InternalDocumentItemDto[] items = [];
        if (!string.IsNullOrWhiteSpace(internalDocumentItemsJson))
        {
            try
            {
                items = JsonSerializer.Deserialize(
                            internalDocumentItemsJson,
                            InternalDocumentJsonContext.Default.InternalDocumentItemDtoArray)
                        ?? [];
            }
            catch (JsonException ex)
            {
                throw new ArgumentException(
                    "Parameter 'internalDocumentItemsJson' must be a JSON array like " +
                    "[{\"text\":\"Travel settlement\",\"quantity\":1,\"unitPrice\":150,\"rateVAT\":\"none\"}].",
                    nameof(internalDocumentItemsJson),
                    ex);
            }
        }

        var xml = BuildImportXml(
            number,
            date,
            dateTax,
            dateAccounting,
            text,
            partnerCompany,
            partnerName,
            partnerStreet,
            partnerCity,
            partnerZip,
            partnerCountry,
            partnerIco,
            partnerDic,
            partnerAddressbookId,
            accounting,
            classificationVatType,
            symVar,
            symPar,
            centre,
            activity,
            contract,
            sourceLiquidationId,
            note,
            intNote,
            homeAmount,
            foreignCurrencyCode,
            foreignCurrencyAmount,
            foreignCurrencyPriceSum,
            items,
            companyIco,
            appName);

        return await SendAsync(xml, serverUrl, username, password);
    }

    private static string BuildImportXml(
        string? number,
        string? date,
        string? dateTax,
        string? dateAccounting,
        string? text,
        string? partnerCompany,
        string? partnerName,
        string? partnerStreet,
        string? partnerCity,
        string? partnerZip,
        string? partnerCountry,
        string? partnerIco,
        string? partnerDic,
        string? partnerAddressbookId,
        string? accounting,
        string? classificationVatType,
        string? symVar,
        string? symPar,
        string? centre,
        string? activity,
        string? contract,
        string? sourceLiquidationId,
        string? note,
        string? intNote,
        double? homeAmount,
        string? foreignCurrencyCode,
        double? foreignCurrencyAmount,
        double? foreignCurrencyPriceSum,
        InternalDocumentItemDto[] items,
        string companyIco,
        string appName)
    {
        var effectiveDate = date ?? DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var effectiveDateTax = dateTax ?? effectiveDate;
        var effectiveDateAccounting = dateAccounting ?? effectiveDate;

        var ms = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        using (var w = XmlWriter.Create(ms, settings))
        {
            w.WriteStartElement("dat", "dataPack", DataNs);
            w.WriteAttributeString("id", "001");
            w.WriteAttributeString("ico", companyIco);
            w.WriteAttributeString("application", appName);
            w.WriteAttributeString("version", "2.0");
            w.WriteAttributeString("note", string.Empty);

            w.WriteStartElement("dat", "dataPackItem", DataNs);
            w.WriteAttributeString("id", "001");
            w.WriteAttributeString("version", "2.0");

            w.WriteStartElement("int", "intDoc", IntNs);
            w.WriteAttributeString("version", "2.0");

            if (!string.IsNullOrWhiteSpace(sourceLiquidationId))
            {
                w.WriteStartElement("int", "taxDocument", IntNs);
                w.WriteStartElement("int", "sourceLiquidation", IntNs);
                w.WriteElementString("typ", "sourceItemId", TypNs, sourceLiquidationId);
                w.WriteEndElement();
                w.WriteEndElement();
            }

            w.WriteStartElement("int", "intDocHeader", IntNs);

            if (number is not null)
            {
                w.WriteStartElement("int", "number", IntNs);
                w.WriteElementString("typ", "numberRequested", TypNs, number);
                w.WriteEndElement();
            }

            WriteOptional(w, "int", "symVar", IntNs, symVar);
            WriteOptional(w, "int", "symPar", IntNs, symPar);
            w.WriteElementString("int", "date", IntNs, effectiveDate);
            w.WriteElementString("int", "dateTax", IntNs, effectiveDateTax);
            w.WriteElementString("int", "dateAccounting", IntNs, effectiveDateAccounting);

            if (accounting is not null)
            {
                w.WriteStartElement("int", "accounting", IntNs);
                w.WriteElementString("typ", "ids", TypNs, accounting);
                w.WriteEndElement();
            }

            if (classificationVatType is not null)
            {
                w.WriteStartElement("int", "classificationVAT", IntNs);
                w.WriteElementString("typ", "classificationVATType", TypNs, classificationVatType);
                w.WriteEndElement();
            }

            WriteOptional(w, "int", "text", IntNs, text);

            bool hasPartner = partnerAddressbookId is not null || partnerCompany is not null || partnerName is not null ||
                              partnerStreet is not null || partnerCity is not null || partnerZip is not null ||
                              partnerCountry is not null || partnerIco is not null || partnerDic is not null;
            if (hasPartner)
            {
                w.WriteStartElement("int", "partnerIdentity", IntNs);
                if (partnerAddressbookId is not null)
                {
                    w.WriteElementString("typ", "id", TypNs, partnerAddressbookId);
                }
                else
                {
                    w.WriteStartElement("typ", "address", TypNs);
                    WriteOptional(w, "typ", "company", TypNs, partnerCompany);
                    WriteOptional(w, "typ", "name", TypNs, partnerName);
                    WriteOptional(w, "typ", "street", TypNs, partnerStreet);
                    WriteOptional(w, "typ", "city", TypNs, partnerCity);
                    WriteOptional(w, "typ", "zip", TypNs, partnerZip);
                    if (partnerCountry is not null)
                    {
                        w.WriteStartElement("typ", "country", TypNs);
                        w.WriteElementString("typ", "ids", TypNs, partnerCountry);
                        w.WriteEndElement();
                    }
                    WriteOptional(w, "typ", "ico", TypNs, partnerIco);
                    WriteOptional(w, "typ", "dic", TypNs, partnerDic);
                    w.WriteEndElement();
                }
                w.WriteEndElement();
            }

            WriteOptionalRef(w, "int", "centre", IntNs, centre);
            WriteOptionalRef(w, "int", "activity", IntNs, activity);
            WriteOptionalRef(w, "int", "contract", IntNs, contract);
            WriteOptional(w, "int", "note", IntNs, note);
            WriteOptional(w, "int", "intNote", IntNs, intNote);
            w.WriteEndElement();

            if (items.Length > 0)
            {
                w.WriteStartElement("int", "intDocDetail", IntNs);
                foreach (var item in items)
                {
                    w.WriteStartElement("int", "intDocItem", IntNs);
                    w.WriteElementString("int", "text", IntNs, item.Text);
                    w.WriteElementString("int", "quantity", IntNs, item.Quantity.ToString(CultureInfo.InvariantCulture));
                    WriteOptional(w, "int", "unit", IntNs, item.Unit);
                    w.WriteElementString("int", "rateVAT", IntNs, item.RateVAT);

                    w.WriteStartElement("int", "homeCurrency", IntNs);
                    w.WriteElementString("typ", "unitPrice", TypNs, item.UnitPrice.ToString("F2", CultureInfo.InvariantCulture));
                    w.WriteElementString("typ", "priceVAT", TypNs, "0");
                    w.WriteEndElement();

                    WriteOptional(w, "int", "note", IntNs, item.Note);
                    WriteOptional(w, "int", "symPar", IntNs, item.SymPar);

                    if (item.Accounting is not null)
                    {
                        w.WriteStartElement("int", "accounting", IntNs);
                        w.WriteElementString("typ", "ids", TypNs, item.Accounting);
                        w.WriteEndElement();
                    }

                    if (item.ClassificationVatType is not null)
                    {
                        w.WriteStartElement("int", "classificationVAT", IntNs);
                        w.WriteElementString("typ", "classificationVATType", TypNs, item.ClassificationVatType);
                        w.WriteEndElement();
                    }

                    WriteOptionalRef(w, "int", "centre", IntNs, item.Centre);
                    WriteOptionalRef(w, "int", "activity", IntNs, item.Activity);
                    WriteOptionalRef(w, "int", "contract", IntNs, item.Contract);
                    w.WriteEndElement();
                }
                w.WriteEndElement();
            }

            if (homeAmount is not null)
            {
                w.WriteStartElement("int", "intDocSummary", IntNs);
                w.WriteStartElement("int", "homeCurrency", IntNs);
                w.WriteElementString("typ", "priceNone", TypNs, homeAmount.Value.ToString(CultureInfo.InvariantCulture));
                w.WriteEndElement();
                w.WriteEndElement();
            }
            else if (!string.IsNullOrWhiteSpace(foreignCurrencyCode) && foreignCurrencyAmount is not null && foreignCurrencyPriceSum is not null)
            {
                w.WriteStartElement("int", "intDocSummary", IntNs);
                w.WriteStartElement("int", "foreignCurrency", IntNs);
                w.WriteStartElement("typ", "currency", TypNs);
                w.WriteElementString("typ", "ids", TypNs, foreignCurrencyCode);
                w.WriteEndElement();
                w.WriteElementString("typ", "amount", TypNs, foreignCurrencyAmount.Value.ToString(CultureInfo.InvariantCulture));
                w.WriteElementString("typ", "priceSum", TypNs, foreignCurrencyPriceSum.Value.ToString(CultureInfo.InvariantCulture));
                w.WriteEndElement();
                w.WriteEndElement();
            }

            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string BuildListInternalDocumentsXml(string companyIco, string appName)
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
            w.WriteAttributeString("id", "list-internal-documents");
            w.WriteAttributeString("ico", companyIco);
            w.WriteAttributeString("application", appName);
            w.WriteAttributeString("version", "2.0");
            w.WriteAttributeString("note", "list-internal-documents");

            w.WriteStartElement("dat", "dataPackItem", DataNs);
            w.WriteAttributeString("id", "1");
            w.WriteAttributeString("version", "2.0");

            w.WriteStartElement("lst", "listIntDocRequest", ListNs);
            w.WriteAttributeString("version", "2.0");
            w.WriteAttributeString("intDocVersion", "2.0");
            w.WriteStartElement("lst", "requestIntDoc", ListNs);
            w.WriteEndElement();
            w.WriteEndElement();

            w.WriteEndElement();
            w.WriteEndElement();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string ParseInternalDocuments(string responseXml, string? partnerIcoFilter, string? numberContainsFilter, string? textContainsFilter)
    {
        var doc = XDocument.Parse(responseXml);

        XNamespace lst = ListNs;
        XNamespace intNs = IntNs;
        XNamespace typ = TypNs;

        var items = doc
            .Descendants(lst + "intDoc")
            .Select(node =>
            {
                var header = node.Element(intNs + "intDocHeader");
                var partnerAddress = header
                    ?.Element(intNs + "partnerIdentity")
                    ?.Element(typ + "address");
                var numberNode = header?.Element(intNs + "number");
                var number = (string?)numberNode?.Element(typ + "numberRequested")
                          ?? (string?)numberNode?.Element(typ + "numberIssued")
                          ?? (string?)numberNode?.Element(typ + "numberReceived")
                          ?? (string?)numberNode?.Element(typ + "number")
                          ?? numberNode?.Value;

                var summaryHome = node
                    .Element(intNs + "intDocSummary")
                    ?.Element(intNs + "homeCurrency");
                var summaryForeign = node
                    .Element(intNs + "intDocSummary")
                    ?.Element(intNs + "foreignCurrency");
                var total = (string?)summaryHome?.Element(typ + "priceNone")
                         ?? (string?)summaryHome?.Element(typ + "priceLow")
                         ?? (string?)summaryHome?.Element(typ + "priceHigh")
                         ?? (string?)summaryHome?.Element(typ + "price3")
                         ?? (string?)summaryHome?.Element(typ + "price");

                return new
                {
                    id = (string?)header?.Element(intNs + "id"),
                    number,
                    date = (string?)header?.Element(intNs + "date"),
                    text = (string?)header?.Element(intNs + "text"),
                    symVar = (string?)header?.Element(intNs + "symVar"),
                    symPar = (string?)header?.Element(intNs + "symPar"),
                    partnerCompany = (string?)partnerAddress?.Element(typ + "company"),
                    partnerName = (string?)partnerAddress?.Element(typ + "name"),
                    partnerIco = (string?)partnerAddress?.Element(typ + "ico"),
                    note = (string?)header?.Element(intNs + "note"),
                    intNote = (string?)header?.Element(intNs + "intNote"),
                    foreignCurrencyCode = (string?)summaryForeign?.Element(typ + "currency")?.Element(typ + "ids"),
                    foreignCurrencyAmount = (string?)summaryForeign?.Element(typ + "amount"),
                    foreignCurrencyPriceSum = (string?)summaryForeign?.Element(typ + "priceSum"),
                    totalHome = total,
                    totalHomeValue = ParseDecimalSafe(total),
                };
            })
            .Where(x => string.IsNullOrWhiteSpace(partnerIcoFilter) || string.Equals(x.partnerIco, partnerIcoFilter, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(numberContainsFilter) ||
                        (!string.IsNullOrWhiteSpace(x.number) && x.number.Contains(numberContainsFilter, StringComparison.OrdinalIgnoreCase)))
            .Where(x => string.IsNullOrWhiteSpace(textContainsFilter) ||
                        (!string.IsNullOrWhiteSpace(x.text) && x.text.Contains(textContainsFilter, StringComparison.OrdinalIgnoreCase)))
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
            sb.AppendLine($"    \"text\": {ToJsonString(x.text)},");
            sb.AppendLine($"    \"symVar\": {ToJsonString(x.symVar)},");
            sb.AppendLine($"    \"symPar\": {ToJsonString(x.symPar)},");
            sb.AppendLine($"    \"partnerCompany\": {ToJsonString(x.partnerCompany)},");
            sb.AppendLine($"    \"partnerName\": {ToJsonString(x.partnerName)},");
            sb.AppendLine($"    \"partnerIco\": {ToJsonString(x.partnerIco)},");
            sb.AppendLine($"    \"note\": {ToJsonString(x.note)},");
            sb.AppendLine($"    \"intNote\": {ToJsonString(x.intNote)},");
            sb.AppendLine($"    \"foreignCurrencyCode\": {ToJsonString(x.foreignCurrencyCode)},");
            sb.AppendLine($"    \"foreignCurrencyAmount\": {ToJsonString(x.foreignCurrencyAmount)},");
            sb.AppendLine($"    \"foreignCurrencyPriceSum\": {ToJsonString(x.foreignCurrencyPriceSum)},");
            sb.AppendLine($"    \"totalHome\": {ToJsonString(x.totalHome)},");
            sb.AppendLine($"    \"totalHomeValue\": {ToJsonString(x.totalHomeValue?.ToString(CultureInfo.InvariantCulture))}");
            sb.Append("  }");
            if (i < items.Count - 1)
                sb.Append(',');
            sb.AppendLine();
        }

        sb.Append(']');
        return sb.ToString();
    }

    private static decimal? ParseDecimalSafe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Replace(" ", string.Empty).Replace(',', '.');
        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        return null;
    }

    private static void WriteOptionalRef(XmlWriter w, string prefix, string localName, string ns, string? ids)
    {
        if (string.IsNullOrWhiteSpace(ids))
            return;

        w.WriteStartElement(prefix, localName, ns);
        w.WriteElementString("typ", "ids", TypNs, ids);
        w.WriteEndElement();
    }
}