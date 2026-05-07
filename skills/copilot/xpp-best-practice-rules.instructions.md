---
description: Best-practice (BP) rules every generated X++ file must satisfy â€” today/DateTimeUtil, label-typed messages, EDT migration, nested loops, alternate keys, doc comments, label existence. Invoke whenever you scaffold or edit X++ that will eventually be linted by xppbp.exe.
applyTo: '**/*.xpp,**/AxClass/**,**/AxTable/**,**/AxForm/**'
---
# Best-practice rules â€” generated X++ must be BP-clean

> **Source of truth:** [`d365fo bp check`](../../docs/EXAMPLES.md) â€” the Windows-VM runner that executes `xppbp.exe`. The list below covers the non-negotiable BP rules every scaffold and hand-edit must satisfy out of the box.

## Per-rule rules

### `BPUpgradeCodeToday` â€” `today()` is forbidden

`today()` ignores the user's preferred time-zone. Always use:

```xpp
TransDate today = DateTimeUtil::getToday(DateTimeUtil::getUserPreferredTimeZone());
```

### `BPErrorLabelIsText` â€” no hardcoded UI strings

Every string passed to `info()` / `warning()` / `error()` / `Box::yesNo()` etc. must be a label token of the form `@File:Key`. Search before you create:

```sh
d365fo search label "Vehicle is required" --lang en-us --output json
d365fo resolve label @SYS12345                          # confirm an existing token
```

If no match â†’ create the label via your model's labels file, then reference it. Never inline.

### `BPErrorEDTNotMigrated` â€” modern EDT relations

EDT relations must use the `EDT.Relations` element, **not** the legacy table-level relations on the EDT. The CLI's `d365fo generate edt` and `d365fo generate extension edt` already emit the modern shape â€” preserve it when hand-editing.

### `BPCheckNestedLoopinCode` â€” no nested data-access loops

Nested `while select` blocks are forbidden:

```xpp
// âŒ WRONG
while select custTable
{
    while select custInvoiceJour where custInvoiceJour.OrderAccount == custTable.AccountNum
    { â€¦ }
}

// âœ… CORRECT â€” single join
while select custTable
    join custInvoiceJour
    where custInvoiceJour.OrderAccount == custTable.AccountNum
{ â€¦ }
```

For *filter-only* joins use `exists join` / `notExists join`. For complex correlations pre-load to a `Map` or temp table.

### `BPCheckAlternateKeyAbsent` â€” every table needs an alternate key

A unique index on the natural key, marked `AlternateKey = Yes`. The CLI's `d365fo generate table` template emits a `<Pkey>` index with `AlternateKey = Yes` â€” don't strip it.

### `BPErrorUnknownLabel` â€” labels referenced must exist

`@File:Key` tokens must resolve to a real entry in an indexed label file. Confirm with:

```sh
d365fo resolve label @File:Key --lang en-us,cs --output json
```

If the result is `ok:false` with `LABEL_NOT_FOUND`, **stop** and either pick an existing label (`d365fo search label â€¦`) or add the entry to the model's labels file before referencing it.

### `BPXmlDocNoDocumentationComments` â€” meaningful doc comments

Public/protected classes and methods need a non-trivial `/// <summary>`:

```xpp
/// <summary>Calculates the customer balance in the company currency.</summary>
/// <param name="_includeOpen">Whether open transactions count.</param>
/// <returns>Balance in MST.</returns>
public AmountMST balanceMST(boolean _includeOpen = true) { â€¦ }
```

Auto-generated stubs ("This method does foo.") do **not** count. Restate the contract.

### `BPDuplicateMethod` â€” no dupes on the inheritance chain

Adding a method that already exists on a base class in the same model fails BP. Run `d365fo get class <Base>` to confirm before adding.

## Label-on-field exception

When adding a field whose **EDT** already carries a label, do **NOT** set `--label` on the field â€” it inherits from the EDT. Override only if you deliberately want a different caption in this table:

```sh
d365fo generate table FmVehicle \
    --field VIN:VinEdt:mandatory      \   # â† VinEdt has Label = "VIN" â€” leave it alone
    --field Make:Name                  \   # â† inherits "Name" from EDT
    --label "@Fleet:Vehicle"
```

## Linting workflow

```sh
# In-process heuristics â€” fast, runs anywhere, useful for CI:
d365fo lint --output sarif > lint.sarif

# Run specific method-flag categories (detected at index-extract time):
d365fo lint --category today-usage          # BPUpgradeCodeToday: today() calls
d365fo lint --category do-insert-update     # doInsert/doUpdate/doDelete usage
d365fo lint --category doc-comment-missing  # BPXmlDocNoDocumentationComments

# Full BP â€” only on the Windows VM, only on user request:
d365fo bp check --output json
```

Six categories are now available: `table-no-index`, `ext-named-not-attributed`, `string-without-edt`, `today-usage`, `do-insert-update`, `doc-comment-missing`. The method-flag categories are populated at extract time by scanning `<Source>` text â€” no full body is stored. Re-run `d365fo index refresh` after editing source before linting.

**Never** auto-run `bp check`. It blocks the user (slow, Windows-only). Say *"Changes scaffolded. Run `d365fo bp check` when you're ready."*

## Hard "never" list

- **Never** call `today()`.
- **Never** hardcode a UI string in `info()` / `warning()` / `error()`.
- **Never** nest `while select` blocks.
- **Never** ship a table without an alternate key.
- **Never** reference a label without verifying it exists.
- **Never** auto-run `d365fo bp check`.
