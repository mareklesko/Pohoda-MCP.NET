# BankTools Testing Approach

## Overview
The BankTools class provides MCP tools for interacting with Pohoda's bank movement functionality. Here's how to test its functionality:

## Core Components to Test

### 1. Data Transfer Objects
- `BankItemDto` - Represents bank movement items
- `BankPartnerDto` - Represents partner information
- JSON serialization/deserialization of these objects

### 2. XML Building Functions
- `BuildImportXml` - Creates XML for importing bank movements
- `BuildListBanksXml` - Creates XML for listing bank movements

### 3. Parsing Functions
- `ParseBanks` - Parses XML responses to JSON format

## Test Approach

### Unit Testing Strategy

1. **Serialization Tests**
   - Test BankItemDto JSON serialization
   - Test BankPartnerDto JSON serialization
   - Verify proper JSON structure for bank items

2. **XML Generation Tests**
   - Verify BuildImportXml generates valid XML structure
   - Verify BuildListBanksXml generates proper request XML
   - Check namespace usage in XML

3. **Integration Testing**
   - Test the full flow from input parameters to XML output
   - Verify XML schema compliance

## Manual Testing Steps

1. Build the project successfully:
```bash
dotnet build
```

2. Run the application:
```bash
dotnet run
```

3. Verify BankTools are registered in the MCP server:
   - Check that `/tools` endpoint shows bank-related tools
   - Test `ListBanks` and `ImportBank` tools

4. Test with sample data:
   - Create valid JSON for `ImportBank` tool
   - Verify proper XML generation
   - Check that responses are properly formatted

## Sample JSON Structure

The BankItemDto supports this structure:
```json
{
  "text": "Payment to supplier",
  "accountNo": "123456789/0100",
  "pairSymbol": "PAY001",
  "date": "2026-03-25",
  "amountMD": 0,
  "amountD": 1500,
  "partner": {
    "company": "Acme s.r.o.",
    "name": "John Doe",
    "street": "Main Street 1",
    "city": "Prague",
    "zip": "12345",
    "ico": "12345678",
    "dic": "CZ12345678"
  }
}
```

## Expected Behavior
1. BankTools should be accessible via MCP protocol
2. XML generation should follow Pohoda schema specifications
3. JSON serialization should handle all required fields properly
4. Error handling should provide meaningful messages for invalid inputs

## Key Methods to Verify
- `ListBanks()` - Returns JSON bank movements
- `ImportBank()` - Creates bank movements with proper XML structure
- XML namespace handling (data.xsd, balance.xsd, type.xsd, list.xsd)