var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

bool useStdio = string.Equals(
    configuration["Mcp:Transport"],
    "stdio",
    StringComparison.OrdinalIgnoreCase);

if (useStdio)
{
    var builder = Host.CreateApplicationBuilder(args);

    // Register HttpClient for Pohoda API calls.
    builder.Services.AddHttpClient();

    // Add the MCP services: stdio transport and the tools to register.
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithTools<RandomNumberTools>()
        .WithTools<AddressBookTools>()
        .WithTools<InvoiceTools>();

    await builder.Build().RunAsync();
}
else
{
    var builder = WebApplication.CreateBuilder(args);

    // Register HttpClient for Pohoda API calls.
    builder.Services.AddHttpClient();

    // Add the MCP services: http transport and the tools to register.
    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithTools<RandomNumberTools>()
        .WithTools<AddressBookTools>()
        .WithTools<InvoiceTools>();

    var app = builder.Build();
    app.MapMcp();

    await app.RunAsync();
}
