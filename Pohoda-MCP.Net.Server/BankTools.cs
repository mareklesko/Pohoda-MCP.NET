using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Server;

public sealed class BankItemDto
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

    /// <summary>Due days (days until due date). Optional. Required by schema.</summary>
    public int? DueDays { get; set; }

    /// <summary>Source agenda (zdrojova agenda). Required by schema.</summary>
    public string SourceAgenda { get; set; } = string.Empty;

    /// <summary>Amount in home currency (amountMD - mazanie, amountD - doplnenie).</summary>
    public double AmountMD { get; set; }

    /// <summary>Amount credited (amountD).</summary>
    public double AmountD { get; set; }

    /// <summary>Remaining amount (amountRemain).</summary>
    public double? AmountRemain { get; set; }

    /// <summary>Optional partner address info.</summary>
    public BankPartnerDto? Partner { get; set; }

    /// <summary>Foreign currency code (e.g., "USD", "EUR"). Optional.</summary>
    public string? ForeignCurrencyCode { get; set; }

    /// <summary>Amount in foreign currency MD.</summary>
    public double? ForeignAmountMD { get; set; }

    /// <summary>Amount in foreign currency D.</summary>
    public double? ForeignAmountD { get; set; }

    /// <summary>Remaining amount in foreign currency.</summary>
    public double? ForeignAmountRemain { get; set; }

    /// <summary>Exchange rate for foreign currency conversion.</summary>
    public double? Rate { get; set; }

    /// <summary>Amount in foreign currency (integer). Optional.</summary>
    public int? Amount { get; set; }

    /// <summary>Variable symbol (symVar).</summary>
    public string? SymVar { get; set; }

    /// <summary>Constant symbol (symConst).</summary>
    public string? SymConst { get; set; }
}

public sealed class BankPartnerDto
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
public sealed partial class BankJsonContext : JsonSerializerContext { }

/// <summary>
/// MCP tools for the Pohoda bank movement (bankovy pohyb) XML import/list API.
/// Import schema namespace: https://www.stormware.cz/schema/version_2/balance.xsd
/// List schema namespace: https://www.stormware.cz/schema/version_2/list.xsd
/// </summary>
public sealed class BankTools(IHttpClientFactory httpClientFactory, IConfiguration configuration)
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
        "amountMD (number), amountD (number), dueDays (integer, optional), sourceAgenda (string, required), " +
        "partner (object with company/name/street/city/zip/ico/dic, optional), " +
        "foreignCurrencyCode (string, optional), foreignAmountMD/foreignAmountD (number, optional). " +
        "Example: [{\"text\":\"Payment\",\"accountNo\":\"123456789/0100\",\"pairSymbol\":\"PAY001\",\"date\":\"2026-03-25\",\"amountMD\":0,\"amountD\":1500,\"sourceAgenda\":\"MAIN\",\"dueDays\":30,\"partner\":{\"company\":\"Acme s.r.o.\"}}]")]
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
            "{\"text\":\"…\",\"accountNo\":\"…\",\"pairSymbol\":\"…\",\"date\":\"2026-03-25\",\"amountMD\":0,\"amountD\":100,\"sourceAgenda\":\"MAIN\",\"dueDays\":30,\"partner\":{\"company\":\"…\"},\"foreignCurrencyCode\":\"EUR\",\"foreignAmountMD\":1500}. " +
            "For payments, set amountMD to payment amount and amountD to 0. For receipts, set amountD to received amount and amountMD to 0. " +
            "sourceAgenda is required by Pohoda schema.")]
        string? bankItemsJson = null)
    {
        var (serverUrl, username, password, companyIco, appName) = GetPohodaSettings();

        symVar = NormalizeOptionalSymbol(nameof(symVar), symVar);
        symConst = NormalizeOptionalSymbol(nameof(symConst), symConst);

        // Deserialize bank items from JSON
        BankItemDto[] items;
        try
        {
            if (string.IsNullOrWhiteSpace(bankItemsJson))
                items = Array.Empty<BankItemDto>();
            else
                items = JsonSerializer.Deserialize<BankItemDto[]>(bankItemsJson, BankJsonContext.Default.BankItemDtoArray);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Invalid bank items JSON: {ex.Message}", nameof(bankItemsJson));
        }

        // Validate sourceAgenda in items
        if (items.Length > 0)
        {
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.SourceAgenda))
                    throw new ArgumentException(
                        "Parameter 'sourceAgenda' is required for each bank item (zdrojová agenda).",
                        nameof(item));
            }
        }

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

    public static decimal SumWithoutVat(decimal amountWithVat, int vatRate)
        => vatRate == 0 ? amountWithVat : Math.Round(amountWithVat / (1 + vatRate / 100m), 2);

    public static decimal Vat(decimal amountWithVat, int vatRate)
        => Math.Round(amountWithVat - SumWithoutVat(amountWithVat, vatRate), 2);

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

                    if (item.ForeignCurrencyCode is not null)
                    {
                        w.WriteStartElement("bal", "foreignCurrency", BalNs);
                        w.WriteElementString("typ", "currency", TypNs, item.ForeignCurrencyCode);
                        if (item.ForeignAmountMD is not null)
                            w.WriteElementString("typ", "amountMD", TypNs, item.ForeignAmountMD.Value.ToString(CultureInfo.InvariantCulture));
                        if (item.ForeignAmountD is not null)
                            w.WriteElementString("typ", "amountD", TypNs, item.ForeignAmountD.Value.ToString(CultureInfo.InvariantCulture));
                        if (item.ForeignAmountRemain is not null)
                            w.WriteElementString("typ", "amountRemain", TypNs, item.ForeignAmountRemain.Value.ToString(CultureInfo.InvariantCulture));
                        if (item.Rate is not null)
                            w.WriteElementString("typ", "rate", TypNs, item.Rate.Value.ToString(CultureInfo.InvariantCulture));
                        if (item.Amount is not null)
                            w.WriteElementString("typ", "amount", TypNs, item.Amount.Value.ToString(CultureInfo.InvariantCulture));
                        w.WriteEndElement();
                    }

                    if (item.SymVar is not null)
                        w.WriteElementString("bal", "symVar", BalNs, item.SymVar);

                    if (item.SymConst is not null)
                        w.WriteElementString("bal", "symConst", BalNs, item.SymConst);

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
