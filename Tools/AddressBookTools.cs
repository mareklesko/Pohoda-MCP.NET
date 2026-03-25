using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Server;

/// <summary>
/// MCP tools for the Pohoda addressbook (Adresář) XML import API.
/// Wraps the adb:addressbook element in a dat:dataPack envelope and POSTs it to the configured Pohoda XML endpoint.
/// Schema: https://www.stormware.sk/xml/schema/version_2/addressbook.xsd
/// </summary>
internal sealed class AddressBookTools(IHttpClientFactory httpClientFactory, IConfiguration configuration)
{
    private const string DataNs    = "http://www.stormware.cz/schema/version_2/data.xsd";
    private const string AdbNs     = "http://www.stormware.cz/schema/version_2/addressbook.xsd";
    private const string TypNs     = "http://www.stormware.cz/schema/version_2/type.xsd";
    private const string FilterNs  = "http://www.stormware.cz/schema/version_2/filter.xsd";
    private const string ListAdbNs = "http://www.stormware.cz/schema/version_2/list_addBook.xsd";

    [McpServerTool]
    [Description(
        "Imports an address book entry into Pohoda (add, update, or delete). " +
        "For 'update' and 'delete' actions supply at least 'ico' or 'company' to identify the record.")]
    public async Task<string> ImportAddressBook(
        [Description("Action: 'add' (default), 'update', or 'delete'.")] string action = "add",
        [Description("Company or organisation name.")] string? company = null,
        [Description("Division / department within the company.")] string? division = null,
        [Description("Contact person's name.")] string? name = null,
        [Description("Street address.")] string? street = null,
        [Description("City / municipality.")] string? city = null,
        [Description("ZIP / postal code.")] string? zip = null,
        [Description("Two-letter country code, e.g. 'CZ' or 'SK'.")] string? country = null,
        [Description("Phone number.")] string? phone = null,
        [Description("Mobile phone number.")] string? mobil = null,
        [Description("Fax number.")] string? fax = null,
        [Description("E-mail address.")] string? email = null,
        [Description("Website URL.")] string? www = null,
        [Description("Czech/Slovak company registration number (IČO).")] string? ico = null,
        [Description("Czech/Slovak VAT registration number (DIČ).")] string? dic = null,
        [Description("Internal note.")] string? note = null)
    {
        var serverUrl  = configuration["Pohoda:ServerUrl"]  ?? throw new InvalidOperationException("Pohoda:ServerUrl is not configured.");
        var username   = configuration["Pohoda:Username"]   ?? string.Empty;
        var password   = configuration["Pohoda:Password"]   ?? string.Empty;
        var companyIco = configuration["Pohoda:Ico"]        ?? string.Empty;
        var appName    = configuration["Pohoda:Application"] ?? "MCP Server";

        var actionLower = action.Trim().ToLowerInvariant();
        if (actionLower is not ("add" or "update" or "delete"))
            throw new ArgumentException($"Unsupported action '{action}'. Expected 'add', 'update', or 'delete'.", nameof(action));
        if (actionLower is "update" or "delete" && string.IsNullOrWhiteSpace(ico) && string.IsNullOrWhiteSpace(company))
            throw new ArgumentException("Either 'ico' or 'company' must be provided to identify the record for update or delete.");

        var xml = BuildXml(actionLower, company, division, name, street, city, zip, country,
                           phone, mobil, fax, email, www, ico, dic, note,
                           companyIco, appName);

        return await SendAsync(xml, serverUrl, username, password);
    }

    [McpServerTool]
    [Description("Returns address book entries from Pohoda as JSON. Optional filters can be applied by ICO and/or company name substring.")]
    public async Task<string> ListAddressBook(
        [Description("Optional ICO filter (exact match).")]
        string? ico = null,
        [Description("Optional company name filter (case-insensitive contains).")]
        string? companyContains = null)
    {
        var serverUrl  = configuration["Pohoda:ServerUrl"]   ?? throw new InvalidOperationException("Pohoda:ServerUrl is not configured.");
        var username   = configuration["Pohoda:Username"]    ?? string.Empty;
        var password   = configuration["Pohoda:Password"]    ?? string.Empty;
        var companyIco = configuration["Pohoda:Ico"]         ?? string.Empty;
        var appName    = configuration["Pohoda:Application"] ?? "MCP Server";

        var xml = BuildListXml(companyIco, appName);
        var responseXml = await SendAsync(xml, serverUrl, username, password);

        if (responseXml.StartsWith("HTTP ", StringComparison.Ordinal))
            return responseXml;

        return ParseAddressList(responseXml, ico, companyContains);
    }

    // -------------------------------------------------------------------------

    private static string BuildXml(
        string actionLower,
        string? company, string? division, string? name,
        string? street, string? city, string? zip, string? country,
        string? phone, string? mobil, string? fax, string? email, string? www,
        string? ico, string? dic, string? note,
        string companyIco, string appName)
    {
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

            // <adb:addressbook version="2.0">
            w.WriteStartElement("adb", "addressbook", AdbNs);
            w.WriteAttributeString("version", "2.0");

            // <adb:actionType>
            w.WriteStartElement("adb", "actionType", AdbNs);
            if (actionLower is "update" or "delete")
            {
                // update / delete requires a filter to identify the record
                w.WriteStartElement("adb", actionLower, AdbNs);
                w.WriteStartElement("ftr", "filter", FilterNs);
                if (!string.IsNullOrWhiteSpace(ico))
                    w.WriteElementString("ftr", "ICO", FilterNs, ico);
                else if (!string.IsNullOrWhiteSpace(company))
                    w.WriteElementString("ftr", "company", FilterNs, company);
                w.WriteEndElement(); // filter
                w.WriteEndElement(); // update|delete
            }
            else
            {
                // add — empty element
                w.WriteStartElement("adb", "add", AdbNs);
                w.WriteEndElement();
            }
            w.WriteEndElement(); // actionType

            // <adb:addressbookHeader> — only for add / update
            if (actionLower is "add" or "update")
            {
                w.WriteStartElement("adb", "addressbookHeader", AdbNs);

                // Identity/address fields must be nested under adb:identity/typ:address.
                if (company is not null || division is not null || name is not null ||
                    city is not null || street is not null || zip is not null ||
                    country is not null || ico is not null || dic is not null)
                {
                    w.WriteStartElement("adb", "identity", AdbNs);
                    w.WriteStartElement("typ", "address", TypNs);

                    WriteOptional(w, "typ", "company",  TypNs, company);
                    WriteOptional(w, "typ", "division", TypNs, division);
                    WriteOptional(w, "typ", "name",     TypNs, name);
                    WriteOptional(w, "typ", "city",     TypNs, city);
                    WriteOptional(w, "typ", "street",   TypNs, street);
                    WriteOptional(w, "typ", "zip",      TypNs, zip);
                    if (country is not null)
                    {
                        w.WriteStartElement("typ", "country", TypNs);
                        w.WriteElementString("typ", "ids", TypNs, country);
                        w.WriteEndElement();
                    }
                    WriteOptional(w, "typ", "ico", TypNs, ico);
                    WriteOptional(w, "typ", "dic", TypNs, dic);

                    w.WriteEndElement(); // typ:address
                    w.WriteEndElement(); // adb:identity
                }

                WriteOptional(w, "adb", "phone", AdbNs, phone);
                WriteOptional(w, "adb", "mobil", AdbNs, mobil);
                WriteOptional(w, "adb", "fax",   AdbNs, fax);
                WriteOptional(w, "adb", "email", AdbNs, email);
                WriteOptional(w, "adb", "web",   AdbNs, www);
                WriteOptional(w, "adb", "note",  AdbNs, note);
                w.WriteEndElement(); // addressbookHeader
            }

            w.WriteEndElement(); // addressbook
            w.WriteEndElement(); // dataPackItem
            w.WriteEndElement(); // dataPack
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string BuildListXml(string companyIco, string appName)
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
            w.WriteAttributeString("id", "list-addressbook");
            w.WriteAttributeString("ico", companyIco);
            w.WriteAttributeString("application", appName);
            w.WriteAttributeString("version", "2.0");
            w.WriteAttributeString("note", "list");

            w.WriteStartElement("dat", "dataPackItem", DataNs);
            w.WriteAttributeString("id", "1");
            w.WriteAttributeString("version", "2.0");

            w.WriteStartElement("lAdb", "listAddressBookRequest", ListAdbNs);
            w.WriteAttributeString("version", "2.0");
            w.WriteAttributeString("addressBookVersion", "2.0");
            w.WriteStartElement("lAdb", "requestAddressBook", ListAdbNs);
            w.WriteEndElement(); // requestAddressBook
            w.WriteEndElement(); // listAddressBookRequest

            w.WriteEndElement(); // dataPackItem
            w.WriteEndElement(); // dataPack
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string ParseAddressList(string responseXml, string? icoFilter, string? companyContainsFilter)
    {
        var doc = XDocument.Parse(responseXml);

        XNamespace lAdb = ListAdbNs;
        XNamespace adb = AdbNs;
        XNamespace typ = TypNs;

        var items = doc
            .Descendants(lAdb + "addressbook")
            .Select(node =>
            {
                var header = node.Element(adb + "addressbookHeader");
                var address = header
                    ?.Element(adb + "identity")
                    ?.Element(typ + "address");

                return new
                {
                    id = (string?)header?.Element(adb + "id"),
                    company = (string?)address?.Element(typ + "company"),
                    ico = (string?)address?.Element(typ + "ico"),
                    dic = (string?)address?.Element(typ + "dic"),
                    street = (string?)address?.Element(typ + "street"),
                    city = (string?)address?.Element(typ + "city"),
                    zip = (string?)address?.Element(typ + "zip"),
                    country = (string?)address?.Element(typ + "country")?.Element(typ + "ids"),
                    email = (string?)header?.Element(adb + "email"),
                    phone = (string?)header?.Element(adb + "phone"),
                    note = (string?)header?.Element(adb + "note"),
                };
            })
            .Where(x => string.IsNullOrWhiteSpace(icoFilter) || string.Equals(x.ico, icoFilter, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(companyContainsFilter) ||
                        (!string.IsNullOrWhiteSpace(x.company) &&
                         x.company.Contains(companyContainsFilter, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("[");

        for (var i = 0; i < items.Count; i++)
        {
            var x = items[i];
            sb.AppendLine("  {");
            sb.AppendLine($"    \"id\": {ToJsonString(x.id)},");
            sb.AppendLine($"    \"company\": {ToJsonString(x.company)},");
            sb.AppendLine($"    \"ico\": {ToJsonString(x.ico)},");
            sb.AppendLine($"    \"dic\": {ToJsonString(x.dic)},");
            sb.AppendLine($"    \"street\": {ToJsonString(x.street)},");
            sb.AppendLine($"    \"city\": {ToJsonString(x.city)},");
            sb.AppendLine($"    \"zip\": {ToJsonString(x.zip)},");
            sb.AppendLine($"    \"country\": {ToJsonString(x.country)},");
            sb.AppendLine($"    \"email\": {ToJsonString(x.email)},");
            sb.AppendLine($"    \"phone\": {ToJsonString(x.phone)},");
            sb.AppendLine($"    \"note\": {ToJsonString(x.note)}");
            sb.Append("  }");
            if (i < items.Count - 1)
                sb.Append(',');
            sb.AppendLine();
        }

        sb.Append(']');
        return sb.ToString();
    }

    private static string ToJsonString(string? value)
    {
        if (value is null)
            return "null";

        return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
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
            client.DefaultRequestHeaders.Add("STW-Authorization", $"Basic {token}");
        }

        using var content = new StringContent(xml, Encoding.UTF8, "text/xml");
        using var response = await client.PostAsync(serverUrl, content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            return $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}\n{responseBody}";

        return responseBody;
    }
}
