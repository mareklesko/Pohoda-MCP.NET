using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Xunit;
using Microsoft.Extensions.Configuration;


namespace Pohoda_MCP.Net.Tests;

public class BankToolsTests
{
    private BankTools _bankTools;

    public BankToolsTests()
    {
        _bankTools = new BankTools();
    }

    [Fact]
    public void SumWithoutVat_WithValidAmounts_CalculatesCorrectly()
    {
        // Arrange
        var amount = 1210.00m;  // Including VAT (21%)
        var vatRate = 21;

        // Act
        var result = BankTools.SumWithoutVat(amount, vatRate);

        // Assert
        Assert.Equal(1000.00m, result);
    }

    [Fact]
    public void Vat_WithValidAmounts_CalculatesCorrectly()
    {
        // Arrange
        var amount = 1210.00m;  // Including VAT (21%)
        var vatRate = 21;

        // Act
        var result = BankTools.Vat(amount, vatRate);

        // Assert
        Assert.Equal(210.00m, result);
    }

    [Fact]
    public void SumWithoutVat_WithZeroAmount_ReturnsZero()
    {
        // Arrange
        var amount = 0m;
        var vatRate = 21;

        // Act
        var result = BankTools.SumWithoutVat(amount, vatRate);

        // Assert
        Assert.Equal(0m, result);
    }

    [Fact]
    public void Vat_WithZeroAmount_ReturnsZero()
    {
        // Arrange
        var amount = 0m;
        var vatRate = 21;

        // Act
        var result = BankTools.Vat(amount, vatRate);

        // Assert
        Assert.Equal(0m, result);
    }

    [Fact]
    public void SumWithoutVat_WithDifferentVatRate_CalculatesCorrectly()
    {
        // Arrange
        var amount = 1100.00m;  // Including VAT (10%)
        var vatRate = 10;

        // Act
        var result = BankTools.SumWithoutVat(amount, vatRate);

        // Assert
        Assert.Equal(1000.00m, result);
    }

    [Fact]
    public void Vat_WithDifferentVatRate_CalculatesCorrectly()
    {
        // Arrange
        var amount = 1100.00m;  // Including VAT (10%)
        var vatRate = 10;

        // Act
        var result = BankTools.Vat(amount, vatRate);

        // Assert
        Assert.Equal(100.00m, result);
    }

    // ========== BankTools Schema Compliance Tests ==========

    [Fact]
    public void ImportBank_BasicImport_GeneratesValidXmlStructure()
    {
        // Arrange
        var json = @"[{
            ""text"": ""Test Payment"",
            ""accountNo"": ""123456789/0100"",
            ""pairSymbol"": ""PAY001"",
            ""date"": ""2026-03-25"",
            ""amountMD"": 0,
            ""amountD"": 1500,
            ""sourceAgenda"": ""MAIN"",
            ""dueDays"": 30,
            ""partner"": {""company"": ""Acme s.r.o.""}
        }]";

        var (serverUrl, _, _, _, _) = GetTestSettings();

        // Act
        var result = Task.Run(() =>
            _bankTools.ImportBank(
                "CZ000000000000000123456789",
                null,
                null,
                null,
                "Acme s.r.o.",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                json));

        // Assert
        var xmlResult = result.Result;
        Assert.DoesNotContain("HTTP ", xmlResult, StringComparison.Ordinal);

        // Parse and validate structure
        var doc = XDocument.Parse(xmlResult);
        var balanceItem = doc.Root?.Element("dat")?.Element("dataPackItem")?.Element("bal")?.Element("balanceItem");

        Assert.NotNull(balanceItem);

        // Validate schema-compliant structure
        var ns = balanceItem?.GetDefaultNamespace();
        Assert.NotNull(balanceItem.Element("bal", "text", ns));
        Assert.Equal("Test Payment", balanceItem.Element("bal", "text", ns)?.Value);
        Assert.Equal("123456789/0100", balanceItem.Element("bal", "accountNo", ns)?.Value);
        Assert.Equal("PAY001", balanceItem.Element("bal", "pairSymbol", ns)?.Value);
        Assert.Equal("2026-03-25", balanceItem.Element("bal", "date", ns)?.Value);
        Assert.Equal("MAIN", balanceItem.Element("bal", "sourceAgenda", ns)?.Value);

        // Validate homeCurrency is at balanceItem level (schema compliant)
        var homeCurrency = balanceItem.Element("bal", "homeCurrency", ns);
        Assert.NotNull(homeCurrency);
        Assert.Equal("1500", homeCurrency.Element("typ", "amountD", ns)?.Value);
    }

    [Fact]
    public void ImportBank_WithForeignCurrency_GeneratesProperStructure()
    {
        // Arrange
        var json = @"[{
            ""text"": ""Foreign Payment"",
            ""accountNo"": ""123456789/0100"",
            ""pairSymbol"": ""PAY001"",
            ""date"": ""2026-03-25"",
            ""amountMD"": 0,
            ""amountD"": 1500,
            ""sourceAgenda"": ""MAIN"",
            ""foreignCurrencyCode"": ""EUR"",
            ""foreignAmountMD"": 1400,
            ""foreignAmountD"": 0,
            ""rate"": 25.0,
            ""amount"": 1400
        }]";

        var (serverUrl, _, _, _, _) = GetTestSettings();

        // Act
        var result = Task.Run(() =>
            _bankTools.ImportBank(
                "CZ000000000000000123456789",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                json));

        // Assert
        var xmlResult = result.Result;
        Assert.DoesNotContain("HTTP ", xmlResult, StringComparison.Ordinal);

        var doc = XDocument.Parse(xmlResult);
        var balanceItem = doc.Root?.Element("dat")?.Element("dataPackItem")?.Element("bal")?.Element("balanceItem");

        // Validate foreignCurrency is nested properly (schema compliant)
        var foreignCurrency = balanceItem?.Element("bal", "foreignCurrency");
        Assert.NotNull(foreignCurrency);

        // foreignCurrency must contain these elements per bank.xsd schema
        Assert.NotNull(foreignCurrency.Element("typ", "currency"));
        Assert.Equal("EUR", foreignCurrency.Element("typ", "currency")?.Value);

        Assert.NotNull(foreignCurrency.Element("typ", "amountMD"));
        Assert.Equal("1400", foreignCurrency.Element("typ", "amountMD")?.Value);

        Assert.NotNull(foreignCurrency.Element("typ", "rate"));
        Assert.Equal("25", foreignCurrency.Element("typ", "rate")?.Value);

        Assert.NotNull(foreignCurrency.Element("typ", "amount"));
        Assert.Equal("1400", foreignCurrency.Element("typ", "amount")?.Value);
    }

    [Fact]
    public void ImportBank_WithSymVarSymConst_IncludesSymbolsInXml()
    {
        // Arrange
        var json = @"[{
            ""text"": ""Payment with symbols"",
            ""accountNo"": ""123456789/0100"",
            ""pairSymbol"": ""PAY001"",
            ""date"": ""2026-03-25"",
            ""amountMD"": 0,
            ""amountD"": 1000,
            ""sourceAgenda"": ""MAIN"",
            ""symVar"": ""VAR123"",
            ""symConst"": ""CONST456""
        }]";

        var (serverUrl, _, _, _, _) = GetTestSettings();

        // Act
        var result = Task.Run(() =>
            _bankTools.ImportBank(
                "CZ000000000000000123456789",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                json));

        // Assert
        var xmlResult = result.Result;
        Assert.DoesNotContain("HTTP ", xmlResult, StringComparison.Ordinal);

        var doc = XDocument.Parse(xmlResult);
        var balanceItem = doc.Root?.Element("dat")?.Element("dataPackItem")?.Element("bal")?.Element("balanceItem");

        // Validate symVar and symConst are at balanceItem level (schema compliant)
        Assert.NotNull(balanceItem.Element("bal", "symVar"));
        Assert.Equal("VAR123", balanceItem.Element("bal", "symVar")?.Value);

        Assert.NotNull(balanceItem.Element("bal", "symConst"));
        Assert.Equal("CONST456", balanceItem.Element("bal", "symConst")?.Value);
    }

    [Fact]
    public void ImportBank_WithPartnerAddress_UsesCorrectStructure()
    {
        // Arrange
        var json = @"[{
            ""text"": ""Partner Payment"",
            ""accountNo"": ""123456789/0100"",
            ""pairSymbol"": ""PAY001"",
            ""date"": ""2026-03-25"",
            ""amountMD"": 0,
            ""amountD"": 2000,
            ""sourceAgenda"": ""MAIN"",
            ""partner"": {
                ""company"": ""Test Company s.r.o."",
                ""name"": ""Jan Novak"",
                ""street"": ""Hlavn� 123"",
                ""city"": ""Brno"",
                ""zip"": ""600 00"",
                ""ico"": ""12345678"",
                ""dic"": ""12345678/0000""
            }
        }]";

        var (serverUrl, _, _, _, _) = GetTestSettings();

        // Act
        var result = Task.Run(() =>
            _bankTools.ImportBank(
                "CZ000000000000000123456789",
                null,
                null,
                null,
                "Test Company s.r.o.",
                "Jan Novak",
                "Hlavn� 123",
                "Brno",
                "600 00",
                "12345678",
                "12345678/0000",
                null,
                null,
                null,
                null,
                null,
                json));

        // Assert
        var xmlResult = result.Result;
        Assert.DoesNotContain("HTTP ", xmlResult, StringComparison.Ordinal);

        var doc = XDocument.Parse(xmlResult);
        var balanceItem = doc.Root?.Element("dat")?.Element("dataPackItem")?.Element("bal")?.Element("balanceItem");

        // Validate partnerIdentity is at balanceItem level
        var partnerIdentity = balanceItem?.Element("bal", "partnerIdentity");
        Assert.NotNull(partnerIdentity);

        // Validate address structure - ico and dic should be siblings of zip, not children
        var address = partnerIdentity?.Element("typ", "address");
        Assert.NotNull(address);

        // Check that ico and dic are at the same level as zip (siblings, not children)
        var zipElement = address.Element("typ", "zip");
        var icoElement = address.Element("typ", "ico");
        var dicElement = address.Element("typ", "dic");

        Assert.NotNull(zipElement);
        Assert.NotNull(icoElement);
        Assert.NotNull(dicElement);

        // ico and dic should NOT be children of zip (schema validation)
        Assert.NotEqual(zipElement, icoElement.Parent);
        Assert.NotEqual(zipElement, dicElement.Parent);
    }

    [Fact]
    public void ImportBank_WithDueDays_IncludesDueDaysField()
    {
        // Arrange
        var json = @"[{
            ""text"": ""Invoice Payment"",
            ""accountNo"": ""123456789/0100"",
            ""pairSymbol"": "",
            ""date"": ""2026-03-25"",
            ""amountMD"": 0,
            ""amountD"": 3500,
            ""sourceAgenda"": ""MAIN"",
            ""dueDays"": 45
        }]";

        var (serverUrl, _, _, _, _) = GetTestSettings();

        // Act
        var result = Task.Run(() =>
            _bankTools.ImportBank(
                "CZ000000000000000123456789",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                json));

        // Assert
        var xmlResult = result.Result;
        Assert.DoesNotContain("HTTP ", xmlResult, StringComparison.Ordinal);

        var doc = XDocument.Parse(xmlResult);
        var balanceItem = doc.Root?.Element("dat")?.Element("dataPackItem")?.Element("bal")?.Element("balanceItem");

        // Validate dueDays is at balanceItem level (schema compliant)
        Assert.NotNull(balanceItem.Element("bal", "dueDays"));
        Assert.Equal("45", balanceItem.Element("bal", "dueDays")?.Value);
    }

    [Fact]
    public void ImportBank_Validation_SourceAgendaRequired_ThrowsException()
    {
        // Arrange
        var json = @"[{
            ""text"": ""Missing sourceAgenda"",
            ""accountNo"": ""123456789/0100"",
            ""pairSymbol"": "",
            ""date"": ""2026-03-25"",
            ""amountMD"": 0,
            ""amountD"": 1000,
            ""sourceAgenda"": """"
        }]";

        var (serverUrl, _, _, _, _) = GetTestSettings();

        // Act
        var result = Task.Run(() =>
            _bankTools.ImportBank(
                "CZ000000000000000123456789",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                json));

        // Assert
        var exception = Assert.Throws<ArgumentException>(result.Result);
        Assert.Contains("sourceAgenda", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private (string, string, string, string, string) GetTestSettings()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        return (
            configuration["Pohoda:ServerUrl"] ?? "http://localhost:1234/xml",
            configuration["Pohoda:Username"] ?? "admin",
            configuration["Pohoda:Password"] ?? "password",
            configuration["Pohoda:Ico"] ?? "12345678",
            configuration["Pohoda:Application"] ?? "Pohoda MCP"
        );
    }
}