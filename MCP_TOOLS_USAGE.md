# Pohoda MCP Tools Usage

This document describes all MCP tools currently exposed by this server, with practical examples.

## Prerequisites

Set these configuration values before calling any tool:

- `Pohoda:ServerUrl`
- `Pohoda:Username` (optional, but usually needed)
- `Pohoda:Password` (optional, but usually needed)
- `Pohoda:Ico`
- `Pohoda:Application` (optional; defaults to `MCP Server`)

Example in `appsettings.json`:

```json
{
  "Pohoda": {
    "ServerUrl": "http://localhost:1234/xml",
    "Username": "admin",
    "Password": "secret",
    "Ico": "12345678",
    "Application": "Pohoda MCP"
  }
}
```

## Tool List

The server currently provides these tools:

- `import_address_book`
- `list_address_book`
- `import_invoice`
- `list_received_invoices`
- `import_voucher`
- `list_vouchers`

## Address Book Tools

### import_address_book

Creates, updates, or deletes an address book entry.

Important behavior:

- `action` supports: `add`, `update`, `delete`
- For `update` and `delete`, at least one of `ico` or `company` is required

Minimal add example:

```json
{
  "action": "add",
  "company": "Acme s.r.o.",
  "street": "Main 1",
  "city": "Brno",
  "zip": "60200",
  "ico": "12345678",
  "dic": "CZ12345678",
  "email": "info@acme.test"
}
```

Update by ICO example:

```json
{
  "action": "update",
  "ico": "12345678",
  "phone": "+420777111222",
  "note": "Updated by MCP"
}
```

Delete by company example:

```json
{
  "action": "delete",
  "company": "Acme s.r.o."
}
```

### list_address_book

Lists address book entries as JSON.

Arguments:

- `ico` optional exact match filter
- `companyContains` optional case-insensitive substring filter

Examples:

```json
{}
```

```json
{
  "ico": "12345678"
}
```

```json
{
  "companyContains": "acme"
}
```

## Invoice Tools

### import_invoice

Creates an invoice in Pohoda.

Important behavior:

- `invoiceType` defaults to `issuedInvoice`
- `invoiceItemsJson` must be a JSON array serialized as a string
- `rateVAT` values inside items: `none`, `low`, `high`, `third`

Simple invoice example:

```json
{
  "invoiceType": "issuedInvoice",
  "text": "Consulting services",
  "partnerCompany": "Acme s.r.o.",
  "partnerIco": "12345678",
  "partnerDic": "CZ12345678",
  "date": "2026-03-25",
  "dateDue": "2026-04-08",
  "invoiceItemsJson": "[{\"text\":\"Development\",\"quantity\":8,\"unitPrice\":1200,\"rateVAT\":\"high\",\"unit\":\"hour\"}]"
}
```

Received invoice example:

```json
{
  "invoiceType": "receivedInvoice",
  "number": "R-2026-001",
  "partnerCompany": "Supplier a.s.",
  "partnerIco": "87654321",
  "symVar": "2026001",
  "note": "Imported by MCP",
  "invoiceItemsJson": "[{\"text\":\"Office supplies\",\"quantity\":1,\"unitPrice\":4500,\"rateVAT\":\"high\"}]"
}
```

### list_received_invoices

Lists received invoices as JSON.

Arguments:

- `partnerIco` optional exact match filter
- `numberContains` optional case-insensitive substring filter

Examples:

```json
{}
```

```json
{
  "partnerIco": "87654321"
}
```

```json
{
  "numberContains": "2026"
}
```

## Voucher Tools

### import_voucher

Creates a voucher (cash document) in Pohoda.

Important behavior:

- `voucherType` is usually `expense` or `receipt`
- `cashAccount` is a Pohoda cash account code (`typ:ids`), for example `HP`
- `voucherItemsJson` must be a JSON array serialized as a string
- `rateVAT` values inside items: `none`, `low`, `high`, `third`

Minimal voucher example:

```json
{
  "voucherType": "expense",
  "cashAccount": "HP",
  "date": "2026-03-25",
  "text": "Travel advance",
  "partnerName": "Peter Novak",
  "partnerCity": "Brno",
  "homeAmount": 300
}
```

Voucher with detail items example:

```json
{
  "voucherType": "expense",
  "cashAccount": "HP",
  "date": "2026-03-25",
  "datePayment": "2026-03-25",
  "dateTax": "2026-03-25",
  "text": "Travel settlement",
  "accounting": "5Pv",
  "classificationVatType": "nonSubsume",
  "partnerCompany": "Acme s.r.o.",
  "partnerIco": "12345678",
  "symPar": "150100003",
  "note": "Loaded from MCP",
  "voucherItemsJson": "[{\"text\":\"Settlement\",\"quantity\":1,\"unitPrice\":-300,\"rateVAT\":\"none\",\"note\":\"CP-001\"},{\"text\":\"Meal allowance\",\"quantity\":1,\"unitPrice\":58,\"rateVAT\":\"none\",\"accounting\":\"5Pv\"}]"
}
```

### list_vouchers

Lists vouchers as JSON.

Arguments:

- `partnerIco` optional exact match filter
- `numberContains` optional case-insensitive substring filter
- `voucherType` optional exact match filter (for example `expense`, `receipt`)

Examples:

```json
{}
```

```json
{
  "voucherType": "expense"
}
```

```json
{
  "partnerIco": "12345678",
  "numberContains": "2026"
}
```

## Notes

- Date format should be `yyyy-MM-dd`.
- When sending item arrays, pass JSON as escaped string (`invoiceItemsJson`, `voucherItemsJson`).
- On HTTP/API errors, tools return a string starting with `HTTP <status>` and the response body.
