# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Pohoda-MCP.NET is a Model Context Protocol (MCP) server written in C# that bridges Pohoda (a Slovak/Czech accounting software) with AI assistants. It enables AI tools to interact with Pohoda's XML API for managing business documents.

## Architecture

### Project Structure

```
Pohoda-MCP.NET/
├── Pohoda-MCP.Net.Server/          # Main MCP server project
│   ├── Program.cs                  # Entry point, transport configuration
│   ├── PohodaToolBase.cs           # Base class for all tools (shared logic)
│   ├── AddressBookTools.cs         # Address book CRUD operations
│   ├── InvoiceTools.cs             # Invoice management (import, list, cancel)
│   ├── VoucherTools.cs             # Cash/voucher operations
│   ├── InternalDocumentTools.cs    # Internal document operations
│   └── BankTools.cs                # Bank movement operations
├── Pohoda-MCP.Net.Tests/           # Unit tests
├── appsettings.json                # Configuration template
└── README.md                       # Project documentation
```

### Key Components

**Transport Modes:**
- **HTTP** (default): MCP server runs as ASP.NET Core web app, suitable for VS Code / Visual Studio
- **StdIO**: Standard input/output transport, suitable for Claude Desktop / studio clients

**Tool Categories:**
- **Address Book Tools**: `import_address_book`, `list_address_book`
- **Invoice Tools**: `import_invoice`, `cancel_invoice`, `list_issued_invoices`, `list_received_invoices`
- **Commitment Tools**: `import_commitment`, `list_commitments`
- **Receivable Tools**: `import_receivable`, `list_receivables`
- **Voucher Tools**: `import_voucher`, `cancel_voucher`, `list_vouchers`
- **Internal Document Tools**: `import_internal_document`, `list_internal_documents`
- **Bank Tools**: `import_bank`, `list_banks`

**PohodaToolBase.cs** provides shared functionality:
- XML building with Pohoda namespaces
- HTTP requests with Basic authentication
- Address book lookup/creation (ensureSupplierAndGetAddressbookIdAsync)
- Response parsing

### XML Schema Namespaces

The server uses these Pohoda XML namespaces:
- `data.xsd` - Main data schema
- `addressbook.xsd` - Address book operations
- `type.xsd` - Common types
- `list_addBook.xsd` - Address book listing
- `response.xsd` - Response handling
- `documentresponse.xsd` - Document production details

## Build Commands

```bash
# Restore dependencies
dotnet restore

# Build (Debug mode)
dotnet build

# Build and run
dotnet run

# Build for specific platform
dotnet publish -r win-x64
dotnet publish -r win-arm64
dotnet publish -r linux-x64
```

## Configuration

### appsettings.json Structure
```json
{
  "Pohoda": {
    "ServerUrl": "http://localhost:1234/xml",
    "Username": "admin",
    "Password": "secret",
    "Ico": "12345678",
    "Application": "Pohoda MCP"
  },
  "Mcp": {
    "Transport": "stdio"  // or "http"
  }
}
```

### Transport Configuration Options

| Method | Configuration |
|--------|---------------|
| JSON | `"Mcp:Transport": "stdio"` in appsettings.json |
| Environment | `Mcp__Transport=stdio` |
| CLI | `./Pohoda-MCP.Net --Mcp:Transport=stdio` |

### Testing Configuration (VS Code MCP Client)
```json
{
  "servers": {
    "Pohoda-MCP.Net": {
      "type": "http",
      "url": "http://localhost:6164"
    }
  }
}
```

## Testing

### Unit Tests
```bash
dotnet test
```

### Manual Testing Steps
1. Build the project: `dotnet build`
2. Run the application: `dotnet run`
3. Configure MCP client to connect to `http://localhost:6164`
4. Test tools via MCP protocol (using Copilot Chat or MCP client)

## Development Guidelines

### Adding New Tools

1. Create a new tool class inheriting from `PohodaToolBase`
2. Implement MCP tool definitions in the constructor
3. Add tool registration in `Program.cs` using `.WithTools<YourToolClass>()`
4. Follow existing patterns for XML building and HTTP requests

### XML Building Pattern
```csharp
var ms = new MemoryStream();
var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false) };

using (var w = XmlWriter.Create(ms, settings))
{
    w.WriteStartElement("dat", "dataPack", DataNs);
    // Build your request...
    w.WriteEndElement();
}

return Encoding.UTF8.GetString(ms.ToArray());
```

### Authentication
```csharp
var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
client.DefaultRequestHeaders.Add("STW-Authorization", $"Basic {token}");
```

## Known Issues

1. **VS Code localhost connection**: Using `https://localhost:5118` fails due to self-signed certificate. Use `http://localhost:6164` instead.

2. **Storno operations**: Cancellation creates a reversal document (storno) rather than deleting the original - this is Pohoda API behavior, not a bug.

## References

- [MCP Official Documentation](https://modelcontextprotocol.io/)
- [Protocol Specification](https://spec.modelcontextprotocol.io/)
- [GitHub Organization](https://github.com/modelcontextprotocol)
- [MCP C# SDK](https://modelcontextprotocol.github.io/csharp-sdk)
- [Pohoda XML API](https://www.pohoda.cz/xml)

---
*This CLAUDE.md was generated on 2026-05-19*