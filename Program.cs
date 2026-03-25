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

    RegisterServices(builder.Services);
    builder.Services.AddMcpServer()
        .WithStdioServerTransport()
        .WithTools<RandomNumberTools>()
        .WithTools<AddressBookTools>()
        .WithTools<InvoiceTools>();

    await builder.Build().RunAsync();
}
else
{
    var builder = WebApplication.CreateBuilder(args);

    RegisterServices(builder.Services);
    builder.Services.AddMcpServer()
        .WithHttpTransport()
        .WithTools<RandomNumberTools>()
        .WithTools<AddressBookTools>()
        .WithTools<InvoiceTools>();

    var app = builder.Build();
    app.MapMcp();

    await app.RunAsync();
}

static void RegisterServices(IServiceCollection services)
{
    // Register HttpClient for Pohoda API calls.
    services.AddHttpClient();
}
