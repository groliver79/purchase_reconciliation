# Purchase Reconciliation Tool

> A Microsoft Power Apps Model-Driven application built on Dataverse for managing and reconciling government purchase card transactions against PRISM TPC (Commitment) and PCA (Obligation) data.

---

## Table of Contents

- [Overview](#overview)
- [Users](#users)
- [Key Features](#key-features)
- [Architecture](#architecture)
- [Repository Structure](#repository-structure)
- [Entities](#entities)
- [C# Plugins](#c-plugins)
- [JavaScript Web Resources](#javascript-web-resources)
- [Deployment](#deployment)
- [Known Issues](#known-issues)
- [Change Log](#change-log)

---

## Overview

The Purchase Reconciliation Tool gives Purchase Cardholders and Approving Officials a unified view of PRISM TPC (Commitment) and PCA (Obligation) data down to the allocation level, alongside US Bank Purchase Card transaction data. It allows users to document purchase request items against a TPC from various vendors and verify that purchase totals reconcile against both the TPC commitment and the PCA obligation.

**Solution:** `ProcurementSolution`  
**Publisher Prefix:** `gro`  
**Plugin Namespace:** `ProcurementPlugins`  
**Platform:** Microsoft Power Apps (Model-Driven) · Microsoft Dataverse  

---

## Users

| Role                        | Description                                                                               |
|-----------------------------|-------------------------------------------------------------------------------------------|
| **Purchase Cardholder**     | Creates and manages purchase requests, adds line items, associates US Bank transactions to|
|                             | to TPC allocations                                                                        |
| **Approving Official        |                                                                                           |
|      (Manager)**            | Reviews purchase requests and validates reconciliation against TPC and PCA balances       |

---

## Key Features

- Create purchase requests tied to a TPC number with a committed amount of funds
- Add purchase request line items including vendor, item description, recipient, estimated cost, and actual cost
- Support for quantity-based line items (quantity × unit cost calculated automatically)
- Add multiple TPC allocations per purchase request
- Auto-populate the last TPC allocation as a starting point to reduce keystrokes when adding new allocations
- Associate US Bank purchase card transactions to TPC allocations
- View an OCC-level summary and aggregation of transactions per allocation
- Server-side enforcement of TPC and PCA naming conventions via C# plugins

---

## Architecture

```text
Dataverse
  └─ biteam_purchaserequest          (primary entity — Purchase Request)
  │    ├─ Plugin: AddTpcPrefixPlugin         (Pre-Create, Pre-Update)
  │    ├─ Plugin: CreatePcaRecordPlugin      (Post-Create)
  │    └─ Plugin: SyncOccSummaryPlugin       (Create, Update, Delete on allocations)
  │
  └─ cr6c5_pca                       (PCA / Obligation entity)
       └─ Plugin: AddPcaPrefixPlugin         (Pre-Create, Pre-Update)

Web Resources
  ├─ tpcRegexPrompt.js               (TPC field format validation & hint)
  ├─ pcaRegExPrompt.js               (PCA field format validation & hint)
  └─ tpcAllocation_Prepop.js         (Auto-populates last TPC allocation on Quick Create)
```

---

## Repository Structure

```text
purchase_reconciliation/
│
├── plugins/
│   └── ProcurementPluginsv1/           # C# Visual Studio solution
│       ├── AddTpcPrefixPlugin.cs        # Prepends TPC- to purchase request name
│       ├── AddPcaPrefixPlugin.cs        # Prepends PCA- to PCA record name
│       ├── SyncOccSummaryPlugin.cs      # Maintains OCC summary child entity
│       ├── OccSummaryOnAllocationDeletePlugin.cs  # Handles OCC cleanup on delete
│       ├── ProcurementPlugins.csproj
│       └── ProcurementPlugins.sln
│
├── webresources/
│   ├── tpcRegexPrompt.js               # TPC field input validation (JavaScript)
│   ├── pcaRegExPrompt.js               # PCA field input validation (JavaScript)
│   ├── tpcAllocation_Prepop.js         # Quick Create pre-population (JavaScript)
│   └── purchase_reconciliation_tool_docs.html  # In-app technical documentation
│
├── archive/
│   └── CreatePcaRecordPlugin.cs        # Archived — superseded plugin version
│
├── git-cheatsheet.md                   # Git command reference for the team
└── README.md
```

---

## Entities

### `biteam_purchaserequest` — Purchase Request

| Display Name | Schema Name   | Type          | Notes                                                         |
|--------------|---------------|---------------|---------------------------------------------------------------|
| Name         | `biteam_name` | Text (max 13) | User enters `YY-XXXXXX`; plugin prepends `TPC-` automatically |

> ⚠️ **Important:** `biteam_name` max length is capped at **13 characters** to accommodate the `TPC-` prefix added server-side. Do not revert to the default 100 without reviewing plugin logic.

### `cr6c5_pca` — PCA (Obligation)

| Display Name | Schema Name       | Type          | Notes                                                             |
|--------------|-------------------|---------------|-------------------------------------------------------------------|
| PCA Number   | `cr6c5_pcanumber` | Text (max 13) | Auto-generated by plugin; derived by replacing `TPC-` with `PCA-` |

---

## C# Plugins

All plugins live in the `ProcurementPlugins` namespace and are compiled into a single assembly: `ProcurementPlugins.dll`.

### `AddTpcPrefixPlugin`

Automatically prepends `TPC-` to the `biteam_name` field on Create and Update. Includes a duplicate-prefix guard so re-saving a record does not stack prefixes.

| Attribute      | Value                    |
|----------------|--------------------------|
| Stage          | Pre-Operation (20)       |
| Message        | Create, Update           |
| Entity         | `biteam_purchaserequest` |
| Execution Mode | Synchronous              |

---

### `AddPcaPrefixPlugin`

Automatically prepends `PCA-` to the PCA number field on Create and Update, with the same duplicate-prefix guard pattern as `AddTpcPrefixPlugin`.

| Attribute      | Value                    |
|----------------|--------------------------|
| Stage          | Pre-Operation (20)       |
| Message        | Create, Update           |
| Entity         | `cr6c5_pca`              |
| Execution Mode | Synchronous              |

---

### `CreatePcaRecordPlugin` *(archived)*

> ⚠️ This plugin has been archived. See `archive/CreatePcaRecordPlugin.cs`. Functionality was superseded — PCA record creation logic has been refactored into the active plugin set.

---

### `SyncOccSummaryPlugin`

Maintains the OCC summary child entity (`biteam_occsummary`) which surfaces a read-only aggregation of allocation transactions by OCC code. Fires on Create, Update, and Delete of allocation records. Uses Pre/Post images to accurately calculate summary deltas.

| Attribute      | Value                    |
|----------------|--------------------------|
| Stage          | Post-Operation (40)      |
| Message        | Create, Update, Delete   |
| Entity         | TPC Allocation entity    |
| Execution Mode | Synchronous              |

---

### `OccSummaryOnAllocationDeletePlugin`

Handles cleanup of the `biteam_occsummary` records when a TPC allocation is deleted, ensuring the summary subgrid remains accurate.

| Attribute      | Value                    |
|----------------|--------------------------|
| Stage          | Post-Operation (40)      |
| Message        | Delete                   |
| Entity         | TPC Allocation entity    |
| Execution Mode | Synchronous              |

---

> 💡 **Note:** Additional plugin classes can be added to the same DLL without re-registering the assembly — only a new Step registration is needed in the Plugin Registration Tool.

---

## JavaScript Web Resources

### `tpcRegexPrompt.js`

Provides format validation and user-facing hints for the TPC number field. Displays a format hint on load for new records and clears notifications on save. Includes a `setTimeout` guard to prevent the hint from reappearing on existing records.

### `pcaRegExPrompt.js`

Same pattern as `tpcRegexPrompt.js` but applied to PCA number field validation.

### `tpcAllocation_Prepop.js`

Fires on the Quick Create form for TPC Allocations. Auto-populates the form with the most recently saved allocation record so the user can make incremental changes (e.g., updating the OCC number) without re-entering all fields from scratch.

---

## Deployment

### Plugin Assembly

1. Build the solution in **Visual Studio in Release mode**
2. Open the **Plugin Registration Tool** and connect to the target environment
3. If the assembly is already registered — click **Update** to upload the new DLL
4. If registering for the first time — click **Register New Assembly**
5. Register or verify Steps for each plugin (see tables above)
6. Test by creating a new Purchase Request and confirming the `TPC-` prefix appears and a linked PCA record is created

### Solution (Full Deployment)

1. Export the solution as **Managed** from the source environment via make.powerapps.com
2. Import into the target environment via the Power Platform Admin Center or PAC CLI
3. Verify plugin assembly steps are active post-import
4. Run an end-to-end test: create a Purchase Request, add allocations, associate a US Bank transaction, and validate the OCC summary subgrid

> ⚠️ **Import failures** are typically caused by including a **Debug build** DLL in the solution. Always use a Release build before exporting.

---

## Known Issues

| Issue                               | Symptom                     | Resolution                                           |
|-------------------------------------|-----------------------------|------------------------------------------------------|
| Lingering INFO notification on save | Error code `2200000007`     | Call `clearNotification()` at the start of `onSave()`|
| Hint reappears after save           | Format hint shown           | `setTimeout` guard in `onLoad` checks for existing   |
|                                     | on already-saved records.   | value before setting hint.                           |
| `OnPostSave` event not available    | Event missing from form     | Cleanup logic moved to `onLoad` instead              |
|                                     | editor.                     |                                                      |

---

## Change Log

| Date    | Version | Description                                                                                  |
|---------|---------|----------------------------------------------------------------------------------------------|
| 2025 Q1 | v1.0    | Initial release — `AddTpcPrefixPlugin`, `tpcRegexPrompt.js`, OCC summary subgrid feature     |
| 2025 Q2 | v1.1    | Added `SyncOccSummaryPlugin`, `OccSummaryOnAllocationDeletePlugin`,`tpcAllocation_Prepop.js`,|
|         |         | `pcaRegExPrompt.js`                                                                          |

---

*Purchase Reconciliation Tool · Internal Use Only · AJV-C Procurement · `gro` publisher*

purchase_reconciliation
