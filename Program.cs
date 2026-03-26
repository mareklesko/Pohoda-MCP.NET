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
        .WithTools<AddressBookTools>()
        .WithTools<InvoiceTools>()
        .WithTools<VoucherTools>()
        .WithTools<InternalDocumentTools>();

    await builder.Build().RunAsync();
}
else
{
    var builder = WebApplication.CreateBuilder(args);

    RegisterServices(builder.Services);
    builder.Services.AddMcpServer()
        .WithHttpTransport()
        .WithTools<AddressBookTools>()
        .WithTools<InvoiceTools>()
        .WithTools<VoucherTools>()
        .WithTools<InternalDocumentTools>();

    var app = builder.Build();
    app.MapMcp();

    await app.RunAsync();
}

static void RegisterServices(IServiceCollection services)
{
    // Register HttpClient for Pohoda API calls.
    services.AddHttpClient();
}
