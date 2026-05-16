using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Server;

internal sealed class BankItemDto
{
    /// <summary>Item description text.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Bank account number (typ:string34).</summary>
    public string AccountNo { get; set; } = string.Empty;

    /// <summary>Pair symbol (pairing symbol).</summary>
    public string PairSymbol { get; set; } = string.Empty;

    /// <summary>Date of the bank movement (yyyy-MM-dd).</summary>
    public string Date { get; set; } = string.Empty;

    /// <summary>Due date (yyyy-MM-dd). Optional.</summary>
    public string? DueDate { get; set; }

    /// <summary>Source agenda (zdrojova agenda).</summary>
    public string SourceAgenda { get; set; } = string.Empty;

    /// <summary>Amount in home currency (amountMD - mazanie, amountD - doplnenie).</summary>
    public double AmountMD { get; set; }

    /// <summary>Amount credited (amountD).</summary>
    public double AmountD { get; set; }

    /// <summary>Remaining amount (amountRemain).</summary>
    public double? AmountRemain { get; set; }

    /// <summary>Optional partner address info.</summary>
    public BankPartnerDto? Partner { get; set; }
}

internal sealed class BankPartnerDto
{
    /// <summary>Partner company name.</summary>
    public string? Company { get; set; }

    /// <summary>Partner person name.</summary>
    public string? Name { get; set; }

    /// <summary>Partner street address.</summary>
    public string? Street { get; set; }

    /// <summary>Partner city.</summary>
    public string? City { get; set; }

    /// <summary>Partner ZIP / postal code.</summary>
    public string? Zip { get; set; }

    /// <summary>Partner company registration number (IČO).</summary>
    public string? Ico { get; set; }

    /// <summary>Partner VAT registration number (DIČ).</summary>
    public string? Dic { get; set; }
}

[JsonSerializable(typeof(BankItemDto[]))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class BankJsonContext : JsonSerializerContext { }

/// <summary>
/// MCP tools for the Pohoda bank movement (bankovy pohyb) XML import/list API.
/// Import schema namespace: https://www.stormware.cz/schema/version_2/balance.xsd
/// List schema namespace: https://www.stormware.cz/schema/version_2/list.xsd
/// </summary>
internal sealed class BankTools(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    : PohodaToolBase(httpClientFactory, configuration)
{
    private const string DataNs = "http://www.stormware.cz/schema/version_2/data.xsd";
    private const string BalNs = "http://www.stormware.cz/schema/version_2/balance.xsd";
    private const string TypNs = "http://www.stormware.cz/schema/version_2/type.xsd";
    private const string ListNs = "http://www.stormware.cz/schema/version_2/list.xsd";

    [McpServerTool]
    [Description("Returns bank movements from Pohoda as JSON. Optional filters can be applied by partner ICO and bank movement number substring.")]
    public async Task<string> ListBanks(
        [Description("Optional partner ICO filter (exact match).")]
        string? partnerIco = null,
        [Description("Optional bank movement number filter (case-insensitive contains).")]
        string? numberContains = null)
    {
        var (serverUrl, username, password, companyIco, appName) = GetPohodaSettings();

        var xml = BuildListBanksXml(companyIco, appName);
        var responseXml = await SendAsync(xml, serverUrl, username, password);

        if (responseXml.StartsWith("HTTP ", StringComparison.Ordinal))
            return responseXml;

        return ParseBanks(responseXml, partnerIco, numberContains);
    }

    [McpServerTool]
    [Description(
        "Creates a bank movement in Pohoda. " +
        "Supply 'bankItemsJson' as a JSON array of objects with fields: " +
        "text (string), accountNo (string), pairSymbol (string), date (string, yyyy-MM-dd), " +
        "amountMD (number), amountD (number), partner (object with company/name/street/city/zip/ico/dic, optional). " +
        "Example: [{\"text\":\"Payment to supplier\",\"accountNo\":\"123456789/0100\",\"pairSymbol\":\"PAY001\",\"date\":\"2026-03-25\",\"amountMD\":0,\"amountD\":1500,\"partner\":{\"company\":\"Acme s.r.o.\"}}]")]
    public async Task<string> ImportBank(
        [Description("Bank account code (typ:ids), e.g. 'CZ000000000000000123456789'.")]
        string bankAccount = "CZ000000000000000123456789",
        [Description("Bank movement number (optional; Pohoda assigns the next number in series if omitted).")]
        string? number = null,
        [Description("Date of issue (yyyy-MM-dd). Defaults to today.")]
        string? date = null,
        [Description("Header text/description.")]
        string? text = null,
        [Description("Partner company name (used for address book lookup/creation).")]
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
        [Description("Variable symbol (symVar).")]
        string? symVar = null,
        [Description("Constant symbol (symConst).")]
        string? symConst = null,
        [Description("Internal note.")]
        string? note = null,
        [Description(
            "Bank items as JSON array. Each object: " +
            "{\"text\":\"…\",\"accountNo\":\"…\",\"pairSymbol\":\"…\",\"date\":\"2026-03-25\",\"amountMD\":0,\"amountD\":100,\"partner\":{\"company\":\"…\"}}. " +
            "For payments, set amountMD to payment amount and amountD to 0. For receipts, set amountD to received amount and amountMD to 0.")]
        string? bankItemsJson = null)
    {
        var (serverUrl, username, password, companyIco, appName) = GetPohodaSettings();

        symVar = NormalizeOptionalSymbol(nameof(symVar), symVar);
        symConst = NormalizeOptionalSymbol(nameof(symConst), symConst);

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

        BankItemDto[] items = [];
        if (!string.IsNullOrWhiteSpace(bankItemsJson))
        {
            try
            {
                items = JsonSerializer.Deserialize(bankItemsJson, BankJsonContext.Default.BankItemDtoArray)
                        ?? [];
            }
            catch (JsonException ex)
            {
                throw new ArgumentException(
                    "Parameter 'bankItemsJson' must be a JSON array like " +
                    "[{\"text\":\"Payment\",\"accountNo\":\"123456789/0100\",\"pairSymbol\":\"PAY001\",\"date\":\"2026-03-25\",\"amountMD\":0,\"amountD\":1500\"}].",
                    nameof(bankItemsJson),
                    ex);
            }
        }

        var xml = BuildImportXml(
            bankAccount,
            number,
            date,
            text,
            supplierAddressbookId,
            partnerCompany,
            partnerName,
            partnerStreet,
            partnerCity,
            partnerZip,
            partnerIco,
            partnerDic,
            symVar,
            symConst,
            note,
            items,
            companyIco,
            appName);

        return await SendAsync(xml, serverUrl, username, password);
    }

    private static string BuildImportXml(
        string bankAccount,
        string? number,
        string? date,
        string? text,
        string? partnerAddressbookId,
        string? partnerCompany,
        string? partnerName,
        string? partnerStreet,
        string? partnerCity,
        string? partnerZip,
        string? partnerIco,
        string? partnerDic,
        string? symVar,
        string? symConst,
        string? note,
        BankItemDto[] items,
        string companyIco,
        string appName)
    {
        var effectiveDate = date ?? DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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

            w.WriteStartElement("bal", "balance", BalNs);
            w.WriteAttributeString("version", "2.0");

            w.WriteStartElement("bal", "balanceHeader", BalNs);
            w.WriteElementString("bal", "dateTo", BalNs, effectiveDate);

            w.WriteStartElement("bal", "bankAccount", BalNs);
            w.WriteElementString("typ", "ids", TypNs, bankAccount);
            w.WriteEndElement();

            if (number is not null)
            {
                w.WriteStartElement("bal", "number", BalNs);
                w.WriteElementString("typ", "numberRequested", TypNs, number);
                w.WriteEndElement();
            }

            WriteOptional(w, "bal", "text", BalNs, text);

            if (symVar is not null)
                w.WriteElementString("bal", "symVar", BalNs, symVar);

            if (symConst is not null)
                w.WriteElementString("bal", "symConst", BalNs, symConst);

            if (note is not null)
                w.WriteElementString("bal", "note", BalNs, note);

            bool hasPartner = partnerAddressbookId is not null || partnerCompany is not null || partnerName is not null || partnerStreet is not null ||
                              partnerCity is not null || partnerZip is not null || partnerIco is not null ||
                              partnerDic is not null;
            if (hasPartner)
            {
                w.WriteStartElement("bal", "partnerIdentity", BalNs);
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

            w.WriteEndElement(); // balanceHeader

            if (items.Length > 0)
            {
                foreach (var item in items)
                {
                    w.WriteStartElement("bal", "balanceItem", BalNs);

                    WriteOptional(w, "bal", "text", BalNs, item.Text);
                    w.WriteElementString("bal", "accountNo", BalNs, item.AccountNo);
                    w.WriteElementString("bal", "pairSymbol", BalNs, item.PairSymbol);

                    w.WriteElementString("bal", "date", BalNs, item.Date);

                    if (item.DueDate is not null)
                        w.WriteElementString("bal", "dueDate", BalNs, item.DueDate);

                    w.WriteElementString("bal", "sourceAgenda", BalNs, item.SourceAgenda);

                    w.WriteStartElement("bal", "homeCurrency", BalNs);
                    w.WriteElementString("typ", "amountMD", TypNs, item.AmountMD.ToString(CultureInfo.InvariantCulture));
                    w.WriteElementString("typ", "amountD", TypNs, item.AmountD.ToString(CultureInfo.InvariantCulture));
                    if (item.AmountRemain is not null)
                        w.WriteElementString("typ", "amountRemain", TypNs, item.AmountRemain.Value.ToString(CultureInfo.InvariantCulture));
                    w.WriteEndElement();

                    if (item.Partner is not null)
                    {
                        w.WriteStartElement("bal", "partnerIdentity", BalNs);
                        w.WriteStartElement("typ", "address", TypNs);
                        WriteOptional(w, "typ", "company", TypNs, item.Partner.Company);
                        WriteOptional(w, "typ", "name", TypNs, item.Partner.Name);
                        WriteOptional(w, "typ", "street", TypNs, item.Partner.Street);
                        WriteOptional(w, "typ", "city", TypNs, item.Partner.City);
                        WriteOptional(w, "typ", "zip", TypNs, item.Partner.Zip);
                        WriteOptional(w, "typ", "ico", TypNs, item.Partner.Ico);
                        WriteOptional(w, "typ", "dic", TypNs, item.Partner.Dic);
                        w.WriteEndElement();
                        w.WriteEndElement();
                    }

                    w.WriteEndElement(); // balanceItem
                }
            }

            w.WriteEndElement(); // balance
            w.WriteEndElement(); // dataPackItem
            w.WriteEndElement(); // dataPack
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string BuildListBanksXml(string companyIco, string appName)
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
            w.WriteAttributeString("id", "list-banks");
            w.WriteAttributeString("ico", companyIco);
            w.WriteAttributeString("application", appName);
            w.WriteAttributeString("version", "2.0");
            w.WriteAttributeString("note", "list-banks");

            w.WriteStartElement("dat", "dataPackItem", DataNs);
            w.WriteAttributeString("id", "1");
            w.WriteAttributeString("version", "2.0");

            w.WriteStartElement("lst", "listBalanceRequest", ListNs);
            w.WriteAttributeString("version", "2.0");
            w.WriteAttributeString("balanceVersion", "2.0");
            w.WriteStartElement("lst", "requestBalance", ListNs);
            w.WriteEndElement(); // requestBalance
            w.WriteEndElement(); // listBalanceRequest

            w.WriteEndElement(); // dataPackItem
            w.WriteEndElement(); // dataPack
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string ParseBanks(string responseXml, string? partnerIcoFilter, string? numberContainsFilter)
    {
        var doc = XDocument.Parse(responseXml);

        XNamespace lst = ListNs;
        XNamespace bal = BalNs;
        XNamespace typ = TypNs;

        var items = doc
            .Descendants(lst + "balance")
            .Select(balance =>
            {
                var header = balance.Element(bal + "balanceHeader");
                var partnerAddress = header
                    ?.Element(bal + "partnerIdentity")
                    ?.Element(typ + "address");

                var numberNode = header?.Element(bal + "number");
                var number = (string?)numberNode?.Element(typ + "numberRequested")
                          ?? (string?)numberNode?.Element(typ + "numberIssued")
                          ?? (string?)numberNode?.Element(typ + "numberReceived")
                          ?? (string?)numberNode?.Element(typ + "number")
                          ?? numberNode?.Value;

                var bankAccount = (string?)header?.Element(bal + "bankAccount")?.Element(typ + "ids");

                var homeCurrency = balance
                    .Element(bal + "homeCurrency");

                var amountMD = (string?)homeCurrency?.Element(typ + "amountMD");
                var amountD = (string?)homeCurrency?.Element(typ + "amountD");

                return new
                {
                    id = (string?)header?.Element(bal + "id"),
                    number,
                    date = (string?)header?.Element(bal + "dateTo"),
                    text = (string?)header?.Element(bal + "text"),
                    bankAccount,
                    symVar = (string?)header?.Element(bal + "symVar"),
                    symConst = (string?)header?.Element(bal + "symConst"),
                    partnerCompany = (string?)partnerAddress?.Element(typ + "company"),
                    partnerIco = (string?)partnerAddress?.Element(typ + "ico"),
                    amountMD,
                    amountD,
                };
            })
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
            sb.AppendLine($"    \"text\": {ToJsonString(x.text)},");
            sb.AppendLine($"    \"bankAccount\": {ToJsonString(x.bankAccount)},");
            sb.AppendLine($"    \"symVar\": {ToJsonString(x.symVar)},");
            sb.AppendLine($"    \"symConst\": {ToJsonString(x.symConst)},");
            sb.AppendLine($"    \"partnerCompany\": {ToJsonString(x.partnerCompany)},");
            sb.AppendLine($"    \"partnerIco\": {ToJsonString(x.partnerIco)},");
            sb.AppendLine($"    \"amountMD\": {ToJsonString(x.amountMD)},");
            sb.AppendLine($"    \"amountD\": {ToJsonString(x.amountD)}");
            sb.Append("  }");
            if (i < items.Count - 1)
                sb.Append(',');
            sb.AppendLine();
        }

        sb.Append(']');
        return sb.ToString();
    }
}
