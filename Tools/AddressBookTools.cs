using System.ComponentModel;
using System.Net.Http.Headers;
using System.Text;
using System.Xml;
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
                WriteOptional(w, "adb", "company",  AdbNs, company);
                WriteOptional(w, "adb", "division", AdbNs, division);
                WriteOptional(w, "adb", "name",     AdbNs, name);
                WriteOptional(w, "adb", "city",     AdbNs, city);
                WriteOptional(w, "adb", "street",   AdbNs, street);
                WriteOptional(w, "adb", "zip",      AdbNs, zip);
                if (country is not null)
                {
                    w.WriteStartElement("adb", "country", AdbNs);
                    w.WriteElementString("typ", "ids", TypNs, country);
                    w.WriteEndElement();
                }
                WriteOptional(w, "adb", "phone", AdbNs, phone);
                WriteOptional(w, "adb", "mobil", AdbNs, mobil);
                WriteOptional(w, "adb", "fax",   AdbNs, fax);
                WriteOptional(w, "adb", "email", AdbNs, email);
                WriteOptional(w, "adb", "www",   AdbNs, www);
                WriteOptional(w, "adb", "ICO",   AdbNs, ico);
                WriteOptional(w, "adb", "DIC",   AdbNs, dic);
                WriteOptional(w, "adb", "note",  AdbNs, note);
                w.WriteEndElement(); // addressbookHeader
            }

            w.WriteEndElement(); // addressbook
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
        using var response = await client.PostAsync(serverUrl, content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            return $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}\n{responseBody}";

        return responseBody;
    }
}
