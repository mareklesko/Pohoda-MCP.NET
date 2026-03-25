using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Configuration;

internal abstract class PohodaToolBase(IHttpClientFactory httpClientFactory, IConfiguration configuration)
{
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
}