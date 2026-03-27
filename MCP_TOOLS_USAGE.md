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
- `cancel_invoice`
- `list_issued_invoices`
- `list_received_invoices`
- `import_commitment`
- `list_commitments`
- `import_receivable`
- `list_receivables`
- `import_voucher`
- `cancel_voucher`
- `list_vouchers`
- `import_internal_document`
- `list_internal_documents`

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

### cancel_invoice

Cancels (stornos) an invoice in Pohoda by creating a reversal document. Supply either `id` (Pohoda internal record ID) or `number` (document number) to identify the invoice. `invoiceType` must match the type of the original document.

Important behavior:

- Creates a storno document that offsets the original — no physical deletion occurs.
- At least one of `id` or `number` is required.
- `invoiceType` accepted values: `issuedInvoice`, `receivedInvoice`, `issuedAdvanceInvoice`, `receivedAdvanceInvoice`, `issuedCreditNotice`, `receivedCreditNotice`, `issuedDebitNotice`, `receivedDebitNotice`, `receivable`, `commitment`.

Cancel by internal ID example:

```json
{
  "invoiceType": "issuedInvoice",
  "id": "12345"
}
```

Cancel by document number example:

```json
{
  "invoiceType": "receivedInvoice",
  "number": "F2026001"
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

### list_issued_invoices

Lists issued invoices as JSON.

Arguments:

- `partnerIco` optional exact match filter
- `numberContains` optional case-insensitive substring filter

Examples:

```json
{}
```

```json
{
  "partnerIco": "12345678"
}
```

```json
{
  "numberContains": "2026"
}
```

## Commitment Tools

### import_commitment

Creates a commitment (ostatny zavazok) in Pohoda.

Important behavior:

- Uses the invoice schema with `invoiceType` fixed to `commitment`.
- `invoiceItemsJson` must be a JSON array serialized as a string.
- `rateVAT` values inside items: `none`, `low`, `high`, `third`.
- If partner details are provided, the tool first checks the address book and binds the record to an existing supplier/customer; if no matching entry exists, it creates one and then binds the commitment.

Example:

```json
{
  "number": "ZAV-2026-001",
  "date": "2026-03-26",
  "dateTax": "2026-03-26",
  "dateDue": "2026-04-10",
  "text": "Other liability for external services",
  "partnerCompany": "Supplier a.s.",
  "partnerIco": "87654321",
  "partnerDic": "SK87654321",
  "symVar": "2026001",
  "note": "Imported by MCP",
  "invoiceItemsJson": "[{\"text\":\"External service\",\"quantity\":1,\"unitPrice\":1500,\"rateVAT\":\"high\"}]"
}
```

### list_commitments

Lists commitments (ostatne zavazky) as JSON.

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
  "numberContains": "ZAV-2026"
}
```

## Receivable Tools

### import_receivable

Creates a receivable (ostatna pohladavka) in Pohoda.

Important behavior:

- Uses the invoice schema with `invoiceType` fixed to `receivable`.
- `invoiceItemsJson` must be a JSON array serialized as a string.
- `rateVAT` values inside items: `none`, `low`, `high`, `third`.
- If partner details are provided, the tool first checks the address book and binds the record to an existing supplier/customer; if no matching entry exists, it creates one and then binds the receivable.

Example:

```json
{
  "number": "POH-2026-001",
  "date": "2026-03-26",
  "dateTax": "2026-03-26",
  "dateDue": "2026-04-10",
  "text": "Other receivable for billed services",
  "partnerCompany": "Customer s.r.o.",
  "partnerIco": "12345678",
  "partnerDic": "SK12345678",
  "symVar": "2026002",
  "note": "Imported by MCP",
  "invoiceItemsJson": "[{\"text\":\"Billed service\",\"quantity\":1,\"unitPrice\":1800,\"rateVAT\":\"high\"}]"
}
```

### list_receivables

Lists receivables (ostatne pohladavky) as JSON.

Arguments:

- `partnerIco` optional exact match filter
- `numberContains` optional case-insensitive substring filter

Examples:

```json
{}
```

```json
{
  "partnerIco": "12345678"
}
```

```json
{
  "numberContains": "POH-2026"
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

### cancel_voucher

Cancels (stornos) a voucher in Pohoda by creating a reversal document. Supply either `id` (Pohoda internal record ID) or `number` (document number) to identify the voucher.

Important behavior:

- Creates a storno document that offsets the original — no physical deletion occurs.
- At least one of `id` or `number` is required.

Cancel by internal ID example:

```json
{
  "id": "5678"
}
```

Cancel by document number example:

```json
{
  "number": "P2026042"
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

## Internal Document Tools

### import_internal_document

Creates an internal document in Pohoda.

Important behavior:

- If partner details are provided, the tool first checks the address book and binds the record to an existing supplier/customer; if no matching entry exists, it creates one and then binds the document.
- `internalDocumentItemsJson` must be a JSON array serialized as a string.
- `rateVAT` values inside items: `none`, `low`, `high`, `third`.
- If no detail items are supplied, you can still create a header-only document by providing `homeAmount`.
- For foreign-currency header-only documents, provide `foreignCurrencyCode`, `foreignCurrencyAmount`, and `foreignCurrencyPriceSum`.
- For liquidation-linked tax documents, provide `sourceLiquidationId`.

Header-only example:

```json
{
  "date": "2026-03-25",
  "dateTax": "2026-03-25",
  "dateAccounting": "2026-03-25",
  "text": "Travel settlement CP-026",
  "partnerName": "Jan Novak",
  "partnerStreet": "Luzni 16",
  "partnerCity": "Jihlava",
  "partnerZip": "58601",
  "accounting": "9Int",
  "classificationVatType": "nonSubsume",
  "centre": "Jihlava",
  "note": "Loaded from MCP",
  "intNote": "Internal document without items",
  "homeAmount": 548
}
```

Foreign-currency example:

```json
{
  "date": "2026-03-25",
  "dateTax": "2026-03-25",
  "dateAccounting": "2026-03-25",
  "text": "Travel settlement abroad",
  "partnerName": "Petr Janacek",
  "partnerStreet": "Jiraskova 26",
  "partnerCity": "Jihlava",
  "partnerZip": "58601",
  "classificationVatType": "nonSubsume",
  "centre": "Jihlava",
  "foreignCurrencyCode": "EUR",
  "foreignCurrencyAmount": 1,
  "foreignCurrencyPriceSum": 77
}
```

Liquidation-linked example:

```json
{
  "date": "2026-03-25",
  "dateTax": "2026-03-25",
  "dateAccounting": "2026-03-25",
  "text": "Tax document for received advance payment",
  "symVar": "2600001",
  "symPar": "260800001",
  "sourceLiquidationId": "118",
  "internalDocumentItemsJson": "[{\"text\":\"Paid advance\",\"quantity\":1,\"unitPrice\":100,\"rateVAT\":\"none\"}]"
}
```

Itemized example:

```json
{
  "date": "2026-03-25",
  "dateTax": "2026-03-25",
  "dateAccounting": "2026-03-25",
  "text": "Travel settlement CP-027",
  "partnerCompany": "Acme s.r.o.",
  "partnerIco": "12345678",
  "partnerDic": "CZ12345678",
  "accounting": "9Int",
  "classificationVatType": "nonSubsume",
  "symVar": "2026027",
  "intNote": "Imported by MCP",
  "internalDocumentItemsJson": "[{\"text\":\"Accommodation\",\"quantity\":1,\"unitPrice\":600,\"rateVAT\":\"none\",\"note\":\"CP-027\",\"accounting\":\"1Int\"},{\"text\":\"Meal allowance\",\"quantity\":1,\"unitPrice\":140,\"rateVAT\":\"none\",\"note\":\"CP-027\"}]"
}
```

### list_internal_documents

Lists internal documents as JSON.

Arguments:

- `partnerIco` optional exact match filter
- `numberContains` optional case-insensitive substring filter
- `textContains` optional case-insensitive substring filter

Examples:

```json
{}
```

```json
{
  "partnerIco": "12345678"
}
```

```json
{
  "numberContains": "2026",
  "textContains": "travel"
}
```

## Notes

- Date format should be `yyyy-MM-dd`.
- When sending item arrays, pass JSON as escaped string (`invoiceItemsJson`, `voucherItemsJson`, `internalDocumentItemsJson`).
- On HTTP/API errors, tools return a string starting with `HTTP <status>` and the response body.
- `cancel_invoice` and `cancel_voucher` create a **storno** (reversal) document — physical deletion is not supported by the Pohoda XML API for these document types.
- Internal documents (`import_internal_document`) do not have a cancel/delete operation in the Pohoda XML schema.
