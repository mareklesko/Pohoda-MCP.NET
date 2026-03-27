using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Server;

internal sealed class VoucherItemDto
{
    /// <summary>Item description text.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Quantity.</summary>
    public double Quantity { get; set; } = 1;

    /// <summary>Unit price (home currency).</summary>
    public double UnitPrice { get; set; }

    /// <summary>VAT rate: none | low | high | third. Defaults to "none".</summary>
    public string RateVAT { get; set; } = "none";

    /// <summary>Optional accounting preset id.</summary>
    public string? Accounting { get; set; }

    /// <summary>Optional item note.</summary>
    public string? Note { get; set; }
}

[JsonSerializable(typeof(VoucherItemDto[]))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class VoucherJsonContext : JsonSerializerContext { }

/// <summary>
/// MCP tools for the Pohoda voucher (Pokladna) XML import/list API.
/// Import schema namespace: https://www.stormware.cz/schema/version_2/voucher.xsd
/// List schema namespace: https://www.stormware.cz/schema/version_2/list.xsd
/// </summary>
internal sealed class VoucherTools(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    : PohodaToolBase(httpClientFactory, configuration)
{
    private const string DataNs = "http://www.stormware.cz/schema/version_2/data.xsd";
    private const string VchNs = "http://www.stormware.cz/schema/version_2/voucher.xsd";
    private const string TypNs = "http://www.stormware.cz/schema/version_2/type.xsd";
    private const string ListNs = "http://www.stormware.cz/schema/version_2/list.xsd";

    [McpServerTool]
    [Description("Returns vouchers from Pohoda as JSON. Optional filters can be applied by partner ICO, voucher number substring, and voucher type.")]
    public async Task<string> ListVouchers(
        [Description("Optional partner ICO filter (exact match).")]
        string? partnerIco = null,
        [Description("Optional voucher number filter (case-insensitive contains).")]
        string? numberContains = null,
        [Description("Optional voucher type filter (exact match), e.g. 'expense' or 'receipt'.")]
        string? voucherType = null)
    {
        var (serverUrl, username, password, companyIco, appName) = GetPohodaSettings();

        var xml = BuildListVouchersXml(companyIco, appName);
        var responseXml = await SendAsync(xml, serverUrl, username, password);

        if (responseXml.StartsWith("HTTP ", StringComparison.Ordinal))
            return responseXml;

        return ParseVouchers(responseXml, partnerIco, numberContains, voucherType);
    }

    [McpServerTool]
    [Description(
        "Creates a voucher in Pohoda. " +
        "Supply 'voucherItemsJson' as a JSON array of objects with fields: " +
        "text (string), quantity (number), unitPrice (number), rateVAT ('none'|'low'|'high'|'third'), accounting (string, optional), note (string, optional). " +
        "Example: [{\"text\":\"Travel settlement\",\"quantity\":1,\"unitPrice\":150,\"rateVAT\":\"none\"}]")]
    public async Task<string> ImportVoucher(
        [Description("Voucher type. Usually 'expense' or 'receipt'.")]
        string voucherType = "expense",
        [Description("Cash account code (typ:ids), e.g. 'HP'.")]
        string cashAccount = "HP",
        [Description("Voucher number (optional; Pohoda assigns the next number in series if omitted).")]
        string? number = null,
        [Description("Date of issue/payment (yyyy-MM-dd). Defaults to today.")]
        string? date = null,
        [Description("Date of payment (yyyy-MM-dd). Defaults to date.")]
        string? datePayment = null,
        [Description("Date of taxable supply (yyyy-MM-dd). Defaults to date.")]
        string? dateTax = null,
        [Description("Header text/description.")]
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
        [Description("Partner company registration number (IČO).")]
        string? partnerIco = null,
        [Description("Partner VAT registration number (DIČ).")]
        string? partnerDic = null,
        [Description("Accounting preset code (typ:ids).")]
        string? accounting = null,
        [Description("VAT classification type, e.g. 'nonSubsume'.")]
        string? classificationVatType = null,
        [Description("Variable symbol.")]
        string? symVar = null,
        [Description("Pairing symbol.")]
        string? symPar = null,
        [Description("Public note.")]
        string? note = null,
        [Description("Internal note.")]
        string? intNote = null,
        [Description("Optional voucher home-currency summary amount (written to voucherSummary/homeCurrency/priceNone).")]
        double? homeAmount = null,
        [Description(
            "Voucher items as JSON array. Each object: " +
            "{\"text\":\"…\",\"quantity\":1,\"unitPrice\":100.0,\"rateVAT\":\"none\",\"accounting\":\"5Pv\",\"note\":\"…\"}. " +
            "'rateVAT' values: none | low | high | third.")]
        string? voucherItemsJson = null)
    {
        var (serverUrl, username, password, companyIco, appName) = GetPohodaSettings();

        symVar = NormalizeOptionalSymbol(nameof(symVar), symVar);
        symPar = NormalizeOptionalSymbol(nameof(symPar), symPar);

        var supplierAddressbookId = await EnsureSupplierAndGetAddressbookIdAsync(
            new SupplierInfo(
                partnerCompany,
                partnerName,
                partnerStreet,
                partnerCity,
                partnerZip,
                null,
                partnerIco,
                partnerDic));

        VoucherItemDto[] items = [];
        if (!string.IsNullOrWhiteSpace(voucherItemsJson))
        {
            try
            {
                items = JsonSerializer.Deserialize(voucherItemsJson, VoucherJsonContext.Default.VoucherItemDtoArray)
                        ?? [];
            }
            catch (JsonException ex)
            {
                throw new ArgumentException(
                    "Parameter 'voucherItemsJson' must be a JSON array like " +
                    "[{\"text\":\"Travel settlement\",\"quantity\":1,\"unitPrice\":150,\"rateVAT\":\"none\"}].",
                    nameof(voucherItemsJson),
                    ex);
            }
        }

        var xml = BuildImportXml(
            voucherType,
            cashAccount,
            number,
            date,
            datePayment,
            dateTax,
            text,
            partnerCompany,
            partnerName,
            partnerStreet,
            partnerCity,
            partnerZip,
            partnerIco,
            partnerDic,
            supplierAddressbookId,
            accounting,
            classificationVatType,
            symVar,
            symPar,
            note,
            intNote,
            homeAmount,
            items,
            companyIco,
            appName);

        return await SendAsync(xml, serverUrl, username, password);
    }

    [McpServerTool]
    [Description(
        "Cancels (stornos) a voucher in Pohoda by creating a reversal document. " +
        "Supply either 'id' (Pohoda internal record ID) or 'number' (document number) to identify the voucher to cancel.")]
    public async Task<string> CancelVoucher(
        [Description("Pohoda internal record ID of the voucher to cancel.")]
        string? id = null,
        [Description("Document number of the voucher to cancel.")]
        string? number = null)
    {
        if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(number))
            throw new ArgumentException("Either 'id' or 'number' must be provided to identify the voucher to cancel.");

        var (serverUrl, username, password, companyIco, appName) = GetPohodaSettings();
        var xml = BuildCancelXml(id, number, companyIco, appName);
        return await SendAsync(xml, serverUrl, username, password);
    }

    private static string BuildCancelXml(
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

            w.WriteStartElement("vch", "voucher", VchNs);
            w.WriteAttributeString("version", "2.0");

            // cancelDocument identifies the document to be storno-ed
            w.WriteStartElement("vch", "cancelDocument", VchNs);
            w.WriteStartElement("typ", "sourceDocument", TypNs);
            if (!string.IsNullOrWhiteSpace(id))
                w.WriteElementString("typ", "id", TypNs, id);
            if (!string.IsNullOrWhiteSpace(number))
                w.WriteElementString("typ", "number", TypNs, number);
            w.WriteEndElement(); // typ:sourceDocument
            w.WriteEndElement(); // vch:cancelDocument

            w.WriteEndElement(); // vch:voucher
            w.WriteEndElement(); // dat:dataPackItem
            w.WriteEndElement(); // dat:dataPack
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string BuildImportXml(
        string voucherType,
        string cashAccount,
        string? number,
        string? date,
        string? datePayment,
        string? dateTax,
        string? text,
        string? partnerCompany,
        string? partnerName,
        string? partnerStreet,
        string? partnerCity,
        string? partnerZip,
        string? partnerIco,
        string? partnerDic,
        string? partnerAddressbookId,
        string? accounting,
        string? classificationVatType,
        string? symVar,
        string? symPar,
        string? note,
        string? intNote,
        double? homeAmount,
        VoucherItemDto[] items,
        string companyIco,
        string appName)
    {
        var effectiveDate = date ?? DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var effectiveDatePayment = datePayment ?? effectiveDate;
        var effectiveDateTax = dateTax ?? effectiveDate;

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

            w.WriteStartElement("vch", "voucher", VchNs);
            w.WriteAttributeString("version", "2.0");

            w.WriteStartElement("vch", "voucherHeader", VchNs);
            w.WriteElementString("vch", "voucherType", VchNs, voucherType);

            w.WriteStartElement("vch", "cashAccount", VchNs);
            w.WriteElementString("typ", "ids", TypNs, cashAccount);
            w.WriteEndElement();

            if (number is not null)
            {
                w.WriteStartElement("vch", "number", VchNs);
                w.WriteElementString("typ", "numberRequested", TypNs, number);
                w.WriteEndElement();
            }

            w.WriteElementString("vch", "date", VchNs, effectiveDate);
            w.WriteElementString("vch", "datePayment", VchNs, effectiveDatePayment);
            w.WriteElementString("vch", "dateTax", VchNs, effectiveDateTax);

            if (accounting is not null)
            {
                w.WriteStartElement("vch", "accounting", VchNs);
                w.WriteElementString("typ", "ids", TypNs, accounting);
                w.WriteEndElement();
            }

            if (classificationVatType is not null)
            {
                w.WriteStartElement("vch", "classificationVAT", VchNs);
                w.WriteElementString("typ", "classificationVATType", TypNs, classificationVatType);
                w.WriteEndElement();
            }

            WriteOptional(w, "vch", "text", VchNs, text);

            bool hasPartner = partnerAddressbookId is not null || partnerCompany is not null || partnerName is not null || partnerStreet is not null ||
                              partnerCity is not null || partnerZip is not null || partnerIco is not null ||
                              partnerDic is not null;
            if (hasPartner)
            {
                w.WriteStartElement("vch", "partnerIdentity", VchNs);
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
                    WriteOptional(w, "typ", "ico", TypNs, partnerIco);
                    WriteOptional(w, "typ", "dic", TypNs, partnerDic);
                    w.WriteEndElement();
                }
                w.WriteEndElement();
            }

            WriteOptional(w, "vch", "symVar", VchNs, symVar);
            WriteOptional(w, "vch", "symPar", VchNs, symPar);
            WriteOptional(w, "vch", "note", VchNs, note);
            WriteOptional(w, "vch", "intNote", VchNs, intNote);
            w.WriteEndElement(); // voucherHeader

            if (items.Length > 0)
            {
                w.WriteStartElement("vch", "voucherDetail", VchNs);
                foreach (var item in items)
                {
                    w.WriteStartElement("vch", "voucherItem", VchNs);
                    w.WriteElementString("vch", "text", VchNs, item.Text);
                    w.WriteElementString("vch", "quantity", VchNs, item.Quantity.ToString(CultureInfo.InvariantCulture));
                    w.WriteElementString("vch", "rateVAT", VchNs, item.RateVAT);

                    w.WriteStartElement("vch", "homeCurrency", VchNs);
                    w.WriteElementString("typ", "unitPrice", TypNs, item.UnitPrice.ToString("F2", CultureInfo.InvariantCulture));
                    w.WriteElementString("typ", "priceVAT", TypNs, "0");
                    w.WriteEndElement();

                    WriteOptional(w, "vch", "note", VchNs, item.Note);

                    if (item.Accounting is not null)
                    {
                        w.WriteStartElement("vch", "accounting", VchNs);
                        w.WriteElementString("typ", "ids", TypNs, item.Accounting);
                        w.WriteEndElement();
                    }

                    w.WriteEndElement(); // voucherItem
                }
                w.WriteEndElement(); // voucherDetail
            }

            if (homeAmount is not null)
            {
                w.WriteStartElement("vch", "voucherSummary", VchNs);
                w.WriteStartElement("vch", "homeCurrency", VchNs);
                w.WriteElementString("typ", "priceNone", TypNs, homeAmount.Value.ToString(CultureInfo.InvariantCulture));
                w.WriteEndElement();
                w.WriteEndElement();
            }

            w.WriteEndElement(); // voucher
            w.WriteEndElement(); // dataPackItem
            w.WriteEndElement(); // dataPack
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string BuildListVouchersXml(string companyIco, string appName)
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
            w.WriteAttributeString("id", "list-vouchers");
            w.WriteAttributeString("ico", companyIco);
            w.WriteAttributeString("application", appName);
            w.WriteAttributeString("version", "2.0");
            w.WriteAttributeString("note", "list-vouchers");

            w.WriteStartElement("dat", "dataPackItem", DataNs);
            w.WriteAttributeString("id", "1");
            w.WriteAttributeString("version", "2.0");

            w.WriteStartElement("lst", "listVoucherRequest", ListNs);
            w.WriteAttributeString("version", "2.0");
            w.WriteAttributeString("voucherVersion", "2.0");
            w.WriteStartElement("lst", "requestVoucher", ListNs);
            w.WriteEndElement(); // requestVoucher
            w.WriteEndElement(); // listVoucherRequest

            w.WriteEndElement(); // dataPackItem
            w.WriteEndElement(); // dataPack
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string ParseVouchers(string responseXml, string? partnerIcoFilter, string? numberContainsFilter, string? voucherTypeFilter)
    {
        var doc = XDocument.Parse(responseXml);

        XNamespace lst = ListNs;
        XNamespace vch = VchNs;
        XNamespace typ = TypNs;

        var items = doc
            .Descendants(lst + "voucher")
            .Select(voucher =>
            {
                var header = voucher.Element(vch + "voucherHeader");
                var partnerAddress = header
                    ?.Element(vch + "partnerIdentity")
                    ?.Element(typ + "address");

                var numberNode = header?.Element(vch + "number");
                var number = (string?)numberNode?.Element(typ + "numberRequested")
                          ?? (string?)numberNode?.Element(typ + "numberIssued")
                          ?? (string?)numberNode?.Element(typ + "numberReceived")
                          ?? (string?)numberNode?.Element(typ + "number")
                          ?? numberNode?.Value;

                var cashAccount = (string?)header?.Element(vch + "cashAccount")?.Element(typ + "ids");
                var summaryHome = voucher
                    .Element(vch + "voucherSummary")
                    ?.Element(vch + "homeCurrency");

                var total = (string?)summaryHome?.Element(typ + "priceNone")
                         ?? (string?)summaryHome?.Element(typ + "priceLow")
                         ?? (string?)summaryHome?.Element(typ + "priceHigh")
                         ?? (string?)summaryHome?.Element(typ + "price3");

                return new
                {
                    id = (string?)header?.Element(vch + "id"),
                    number,
                    date = (string?)header?.Element(vch + "date"),
                    text = (string?)header?.Element(vch + "text"),
                    voucherType = (string?)header?.Element(vch + "voucherType"),
                    cashAccount,
                    symVar = (string?)header?.Element(vch + "symVar"),
                    symPar = (string?)header?.Element(vch + "symPar"),
                    partnerCompany = (string?)partnerAddress?.Element(typ + "company"),
                    partnerIco = (string?)partnerAddress?.Element(typ + "ico"),
                    totalHome = total,
                };
            })
            .Where(x => string.IsNullOrWhiteSpace(partnerIcoFilter) || string.Equals(x.partnerIco, partnerIcoFilter, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(numberContainsFilter) ||
                        (!string.IsNullOrWhiteSpace(x.number) && x.number.Contains(numberContainsFilter, StringComparison.OrdinalIgnoreCase)))
            .Where(x => string.IsNullOrWhiteSpace(voucherTypeFilter) || string.Equals(x.voucherType, voucherTypeFilter, StringComparison.OrdinalIgnoreCase))
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
            sb.AppendLine($"    \"voucherType\": {ToJsonString(x.voucherType)},");
            sb.AppendLine($"    \"cashAccount\": {ToJsonString(x.cashAccount)},");
            sb.AppendLine($"    \"symVar\": {ToJsonString(x.symVar)},");
            sb.AppendLine($"    \"symPar\": {ToJsonString(x.symPar)},");
            sb.AppendLine($"    \"partnerCompany\": {ToJsonString(x.partnerCompany)},");
            sb.AppendLine($"    \"partnerIco\": {ToJsonString(x.partnerIco)},");
            sb.AppendLine($"    \"totalHome\": {ToJsonString(x.totalHome)}");
            sb.Append("  }");
            if (i < items.Count - 1)
                sb.Append(',');
            sb.AppendLine();
        }

        sb.Append(']');
        return sb.ToString();
    }
}