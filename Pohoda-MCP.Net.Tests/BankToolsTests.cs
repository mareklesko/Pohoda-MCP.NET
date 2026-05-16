namespace Pohoda_MCP.Net.Tests;

public class BankToolsTests
{
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
}