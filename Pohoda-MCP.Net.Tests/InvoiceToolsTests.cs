using System.Text.Json;
using System.Globalization;

namespace Pohoda_MCP.Net.Tests;

public class InvoiceToolsTests
{
    [Fact]
    public void DeserializeInvoiceItems_WithValidJson_ReturnsItems()
    {
        // Arrange
        var json = """
            [
                {
                    "text": "Service",
                    "quantity": 1,
                    "unitPrice": 1000,
                    "rateVAT": "high",
                    "unit": "ks"
                }
            ]
            """;

        // Act & Assert - Testing the method directly
        var result = InvoiceTools.DeserializeInvoiceItems(json);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("Service", result[0].Text);
        Assert.Equal(1, result[0].Quantity);
        Assert.Equal(1000, result[0].UnitPrice);
        Assert.Equal("high", result[0].RateVAT);
        Assert.Equal("ks", result[0].Unit);
    }

    [Fact]
    public void DeserializeInvoiceItems_WithEmptyJson_ReturnsEmptyArray()
    {
        // Arrange
        var json = "";

        // Act & Assert
        var result = InvoiceTools.DeserializeInvoiceItems(json);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void NormalizeSymVar_WithNumber_ReturnsDigitsOnly()
    {
        // Arrange
        var number = "F2024001";
        var symVar = null as string;

        // Act
        var result = InvoiceTools.NormalizeSymVar(number, symVar);

        // Assert
        Assert.Equal("2024001", result);
    }

    [Fact]
    public void NormalizeSymVar_WithNumberShort_ReturnsDigitsOnly()
    {
        // Arrange
        var number = "F123";
        var symVar = null as string;

        // Act
        var result = InvoiceTools.NormalizeSymVar(number, symVar);

        // Assert
        Assert.Equal("123", result);
    }

    [Fact]
    public void ParseDecimalSafe_WithValidDecimal_ReturnsParsedValue()
    {
        // Arrange
        var value = "1000.00";

        // Act
        var result = InvoiceTools.ParseDecimalSafe(value);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1000.00m, result);
    }

    [Fact]
    public void ParseDecimalSafe_WithNullValue_ReturnsNull()
    {
        // Arrange
        var value = null as string;

        // Act
        var result = InvoiceTools.ParseDecimalSafe(value);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ParseDecimalSafe_WithEmptyValue_ReturnsNull()
    {
        // Arrange
        var value = "";

        // Act
        var result = InvoiceTools.ParseDecimalSafe(value);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ParseDecimalSafe_WithInvalidValue_ReturnsNull()
    {
        // Arrange
        var value = "invalid";

        // Act
        var result = InvoiceTools.ParseDecimalSafe(value);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ParseDecimalSafe_WithCommaDecimal_ReturnsParsedValue()
    {
        // Arrange
        var value = "1000,50";

        // Act
        var result = InvoiceTools.ParseDecimalSafe(value);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1000.50m, result);
    }
}