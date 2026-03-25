using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;

internal abstract class PohodaToolBase(IHttpClientFactory httpClientFactory, IConfiguration configuration)
{
    private const string DataNs = "http://www.stormware.cz/schema/version_2/data.xsd";
    private const string AdbNs = "http://www.stormware.cz/schema/version_2/addressbook.xsd";
    private const string TypNs = "http://www.stormware.cz/schema/version_2/type.xsd";
    private const string ListAdbNs = "http://www.stormware.cz/schema/version_2/list_addBook.xsd";
    private const string RspNs = "http://www.stormware.cz/schema/version_2/response.xsd";
    private const string RdcNs = "http://www.stormware.cz/schema/version_2/documentresponse.xsd";

    protected sealed record SupplierInfo(
        string? Company,
        string? Name,
        string? Street,
        string? City,
        string? Zip,
        string? Country,
        string? Ico,
        string? Dic);

    protected (string ServerUrl, string Username, string Password, string CompanyIco, string AppName) GetPohodaSettings()
    {
        var serverUrl = configuration["Pohoda:ServerUrl"]
            ?? throw new InvalidOperationException("Pohoda:ServerUrl is not configured.");

        return (
            serverUrl,
            configuration["Pohoda:Username"] ?? string.Empty,
            configuration["Pohoda:Password"] ?? string.Empty,
            configuration["Pohoda:Ico"] ?? string.Empty,
            configuration["Pohoda:Application"] ?? "MCP Server");
    }

    protected static void WriteOptional(XmlWriter w, string prefix, string local, string ns, string? value)
    {
        if (value is not null)
            w.WriteElementString(prefix, local, ns, value);
    }

    protected static string ToJsonString(string? value)
    {
        if (value is null)
            return "null";

        return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }

    protected async Task<string?> EnsureSupplierAndGetAddressbookIdAsync(SupplierInfo supplier)
    {
        bool hasSupplierInfo = !string.IsNullOrWhiteSpace(supplier.Company)
                            || !string.IsNullOrWhiteSpace(supplier.Name)
                            || !string.IsNullOrWhiteSpace(supplier.Street)
                            || !string.IsNullOrWhiteSpace(supplier.City)
                            || !string.IsNullOrWhiteSpace(supplier.Zip)
                            || !string.IsNullOrWhiteSpace(supplier.Country)
                            || !string.IsNullOrWhiteSpace(supplier.Ico)
                            || !string.IsNullOrWhiteSpace(supplier.Dic);

        if (!hasSupplierInfo)
            return null;

        var (serverUrl, username, password, companyIco, appName) = GetPohodaSettings();

        var listXml = BuildListAddressBookXml(companyIco, appName);
        var listResponse = await SendAsync(listXml, serverUrl, username, password);
        if (listResponse.StartsWith("HTTP ", StringComparison.Ordinal))
            throw new InvalidOperationException($"Address book lookup failed: {listResponse}");

        var existingId = FindAddressbookId(listResponse, supplier);
        if (!string.IsNullOrWhiteSpace(existingId))
            return existingId;

        var addXml = BuildAddAddressbookXml(supplier, companyIco, appName);
        var addResponse = await SendAsync(addXml, serverUrl, username, password);
        if (addResponse.StartsWith("HTTP ", StringComparison.Ordinal))
            throw new InvalidOperationException($"Address book create failed: {addResponse}");

        var createdId = ParseProducedId(addResponse);
        if (!string.IsNullOrWhiteSpace(createdId))
            return createdId;

        // Fallback: lookup again if the response did not include producedDetails id.
        var listResponseAfterCreate = await SendAsync(listXml, serverUrl, username, password);
        if (listResponseAfterCreate.StartsWith("HTTP ", StringComparison.Ordinal))
            throw new InvalidOperationException($"Address book re-lookup failed: {listResponseAfterCreate}");

        return FindAddressbookId(listResponseAfterCreate, supplier);
    }

    protected async Task<string> SendAsync(string xml, string serverUrl, string username, string password)
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

    private static string BuildListAddressBookXml(string companyIco, string appName)
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
            w.WriteEndElement();
            w.WriteEndElement();

            w.WriteEndElement();
            w.WriteEndElement();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string BuildAddAddressbookXml(SupplierInfo supplier, string companyIco, string appName)
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
            w.WriteAttributeString("id", "001");
            w.WriteAttributeString("ico", companyIco);
            w.WriteAttributeString("application", appName);
            w.WriteAttributeString("version", "2.0");
            w.WriteAttributeString("note", string.Empty);

            w.WriteStartElement("dat", "dataPackItem", DataNs);
            w.WriteAttributeString("id", "001");
            w.WriteAttributeString("version", "2.0");

            w.WriteStartElement("adb", "addressbook", AdbNs);
            w.WriteAttributeString("version", "2.0");

            w.WriteStartElement("adb", "actionType", AdbNs);
            w.WriteStartElement("adb", "add", AdbNs);
            w.WriteEndElement();
            w.WriteEndElement();

            w.WriteStartElement("adb", "addressbookHeader", AdbNs);
            w.WriteStartElement("adb", "identity", AdbNs);
            w.WriteStartElement("typ", "address", TypNs);
            WriteOptional(w, "typ", "company", TypNs, supplier.Company);
            WriteOptional(w, "typ", "name", TypNs, supplier.Name);
            WriteOptional(w, "typ", "street", TypNs, supplier.Street);
            WriteOptional(w, "typ", "city", TypNs, supplier.City);
            WriteOptional(w, "typ", "zip", TypNs, supplier.Zip);
            if (!string.IsNullOrWhiteSpace(supplier.Country))
            {
                w.WriteStartElement("typ", "country", TypNs);
                w.WriteElementString("typ", "ids", TypNs, supplier.Country);
                w.WriteEndElement();
            }
            WriteOptional(w, "typ", "ico", TypNs, supplier.Ico);
            WriteOptional(w, "typ", "dic", TypNs, supplier.Dic);
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();

            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string? FindAddressbookId(string listResponseXml, SupplierInfo supplier)
    {
        var doc = XDocument.Parse(listResponseXml);

        XNamespace lAdb = ListAdbNs;
        XNamespace adb = AdbNs;
        XNamespace typ = TypNs;

        var candidates = doc
            .Descendants(lAdb + "addressbook")
            .Select(node =>
            {
                var header = node.Element(adb + "addressbookHeader");
                var address = header?.Element(adb + "identity")?.Element(typ + "address");

                return new
                {
                    id = (string?)header?.Element(adb + "id"),
                    company = (string?)address?.Element(typ + "company"),
                    ico = (string?)address?.Element(typ + "ico"),
                    dic = (string?)address?.Element(typ + "dic"),
                };
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.id))
            .ToList();

        if (!string.IsNullOrWhiteSpace(supplier.Ico))
        {
            var byIco = candidates.FirstOrDefault(x => string.Equals(x.ico, supplier.Ico, StringComparison.OrdinalIgnoreCase));
            if (byIco is not null)
                return byIco.id;
        }

        if (!string.IsNullOrWhiteSpace(supplier.Dic))
        {
            var byDic = candidates.FirstOrDefault(x => string.Equals(x.dic, supplier.Dic, StringComparison.OrdinalIgnoreCase));
            if (byDic is not null)
                return byDic.id;
        }

        if (!string.IsNullOrWhiteSpace(supplier.Company))
        {
            var byCompany = candidates.FirstOrDefault(x => string.Equals(x.company, supplier.Company, StringComparison.OrdinalIgnoreCase));
            if (byCompany is not null)
                return byCompany.id;
        }

        return null;
    }

    private static string? ParseProducedId(string responseXml)
    {
        var doc = XDocument.Parse(responseXml);
        XNamespace rsp = RspNs;
        XNamespace rdc = RdcNs;

        return (string?)doc
            .Descendants(rsp + "responsePackItem")
            .Elements()
            .Elements(rdc + "producedDetails")
            .Elements(rdc + "id")
            .FirstOrDefault();
    }
}