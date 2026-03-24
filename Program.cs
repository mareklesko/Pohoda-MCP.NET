var builder = WebApplication.CreateBuilder(args);

// Register HttpClient for Pohoda API calls.
builder.Services.AddHttpClient();

// Add the MCP services: the transport to use (http) and the tools to register.
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<RandomNumberTools>()
    .WithTools<AddressBookTools>()
    .WithTools<InvoiceTools>();

var app = builder.Build();
app.MapMcp();

app.Run();
