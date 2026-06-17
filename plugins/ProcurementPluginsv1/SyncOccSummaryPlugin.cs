using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Linq;

namespace ProcurementPlugins
{
    /// <summary>
    /// SyncOccSummary Plugin 
    /// 
    /// Responsibilities:
    ///     1. Reads the Associated TPC Allocation lookup on the transaction
    ///     2. Retrieves the PRISM OCC from the TPC Allocation record
    ///     3. Writes the OCC back to biteam_associatedocc on the transaction 
    ///     4. Re-aggregates all transaction amounts for the Purchase Request + OCC combination
    ///     5. Upserts a biteam_occsummary row with the new total
    ///     
    /// Registration
    ///  Entity: cr6c5_usbanktransactions
    ///  Messages: Create (Post-Operation, Stage 40, Sync) - PostImage: all columns
    ///            Update (Post-Operation, Stage 40, Sync) - PostImage: all columns
    ///            Filtering attributes (Update step only):
    ///                                 cr6c5_transactionamount,
    ///                                 biteam_associatedocc,
    ///                                 biteam_purchaserequest,
    ///                                 biteam_associatedtpcallocation,
    ///                                 statecode
    ///            Delete (Post-Operation, Stage 40, Sync) - PreImage: all columns
    ///  
    /// </summary>
    public class SyncOccSummaryPlugin : IPlugin
    {
        //Constant variables
        // -- cr6c5_usbanktransactions fields --
        private const string EntityTransaction = "cr6c5_usbanktransactions";
        private const string FieldTransactionAmount = "cr6c5_transactionamount";
        private const string FieldAssociatedTpcAlloc = "biteam_associatedtpcallocation"; // Lookup to biteam_tpcallocations
        private const string FieldAssociatedOcc = "biteam_associatedocc";           // Lookup to cr6c5_objectclassification
        private const string FieldPurchaseRequest = "biteam_purchaserequest";       // Lookup to biteam_purchaserequest

        // -- biteam_tpcallocations --
        private const string EntityTpcAllocation = "biteam_tpcallocations";
        private const string TpcAllocFieldPrismOcc = "biteam_prismocc";   // Lookup to cr6c5_objectclassification

        // --biteam_occsummary fields --
        private const string EntityOccSummary = "biteam_occsummary";
        private const string SummaryFieldName = "biteam_name";
        private const string SummaryFieldPR = "biteam_purchaserequest";   // Lookup to biteam_purchaserequest
        private const string SummaryFieldOcc = "biteam_occ";              // Lookup to cr6c5_objectclassification
        private const string SummaryFieldTotal = "biteam_totaltransactionamount";

        // --cr6c5_objectclassification fields --
        private const string EntityOcc = "cr6c5_objectclassification";
        private const string OccFieldCode = "cr6c5_objectclasscode";

        //---1. Resolve Services
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var orgService = factory.CreateOrganizationService(null);

            try
            {
                //---2.Guards
                tracingService.Trace("SyncOccSummaryPlugin Entered.");

                // Guard: Post-Operation only, no recursion
                if (context.Stage != 40 || context.Depth > 1)
                {
                    tracingService.Trace("Exiting: Stage is not 40 or Depth > 1.");
                    return;
                }

                Guid transactionId;
                Guid purchaseRequestId;
                Guid occId;

                if (context.MessageName.Equals("Delete", StringComparison.OrdinalIgnoreCase))
                {
                    // ── DELETE ──────────────────────────────────────────────────────
                    if (!context.PreEntityImages.Contains("PreImage"))
                        throw new InvalidPluginExecutionException("PreImage not registered on Delete step.");

                    var preImage = context.PreEntityImages["PreImage"];
                    transactionId = preImage.Id;
                    purchaseRequestId = GetLookupId(preImage, FieldPurchaseRequest);
                    occId = GetLookupId(preImage, FieldAssociatedOcc);

                    if (purchaseRequestId == Guid.Empty || occId == Guid.Empty)
                    {
                        tracingService.Trace("PreImage missing Purchase Request or OCC — skipping.");
                        return;
                    }
                }
                else
                {
                    // ── CREATE / UPDATE ──────────────────────────────────────────────
                    Entity target = context.InputParameters.Contains("Target")
                        ? (Entity)context.InputParameters["Target"]
                        : null;

                    Entity source = MergeWithPostImage(target, context);
                    transactionId = source.Id != Guid.Empty ? source.Id : target?.Id ?? Guid.Empty;
                    purchaseRequestId = GetLookupId(source, FieldPurchaseRequest);

                    if (purchaseRequestId == Guid.Empty)
                    {
                        tracingService.Trace("Purchase Request is empty — skipping.");
                        return;
                    }

                    // STEP 1: Resolve OCC
                    // Always re-resolve when TPC Allocation is explicitly changed so the new
                    // allocation's OCC overwrites the stale value already on the transaction.
                    bool tpcAllocChanged = context.MessageName.Equals("Update", StringComparison.OrdinalIgnoreCase)
                        && target != null
                        && target.Contains(FieldAssociatedTpcAlloc);

                    Guid oldOccId = GetLookupId(source, FieldAssociatedOcc);
                    occId = oldOccId;

                    if (occId == Guid.Empty || tpcAllocChanged)
                    {
                        tracingService.Trace(tpcAllocChanged
                            ? "TPC Allocation changed — re-resolving OCC."
                            : "biteam_associatedocc empty — resolving via TPC Allocation.");
                        occId = ResolveOccFromTpcAllocation(orgService, source, transactionId, tracingService);
                    }

                    if (occId == Guid.Empty)
                    {
                        tracingService.Trace("OCC could not be resolved — TPC Allocation may not be set. Skipping.");
                        return;
                    }

                    // If the OCC changed, recalculate the old OCC summary so its total no
                    // longer includes this transaction's amount.
                    if (tpcAllocChanged && oldOccId != Guid.Empty && oldOccId != occId)
                    {
                        tracingService.Trace($"OCC changed from {oldOccId} to {occId} — recalculating old OCC summary.");
                        decimal oldOccTotal = AggregateTotalAmount(orgService, purchaseRequestId, oldOccId, tracingService);
                        string oldOccCode = GetOccCode(orgService, oldOccId);
                        UpsertOccSummary(orgService, purchaseRequestId, oldOccId, oldOccCode, oldOccTotal, tracingService);
                    }
                }

                // STEP 2: Re-aggregate
                tracingService.Trace($"Syncing OCC summary — PR: {purchaseRequestId}, OCC: {occId}");
                decimal newTotal = AggregateTotalAmount(orgService, purchaseRequestId, occId, tracingService);

                // STEP 3: Get OCC display code
                string occCode = GetOccCode(orgService, occId);

                // STEP 4: Upsert summary row
                UpsertOccSummary(orgService, purchaseRequestId, occId, occCode, newTotal, tracingService);

            }
            catch (Exception ex)
            {
                tracingService.Trace($"SyncOccSummaryPlugin error: {ex.Message}");
                throw new InvalidPluginExecutionException($"SyncOccSummaryPlugin failed: {ex.Message}", ex);
            }
        }
        //Helper - Resolve OCC via TPC Allocation and write it back
        //
        /// <summary>
        /// Reads the TPC Allocation from the transaction, retrieves the PRISM OCC
        /// from the allocation, writes biteam_associatedocc back onto the transaction,
        /// and returns the OCC Guid.
        /// </summary>
        private Guid ResolveOccFromTpcAllocation(
            IOrganizationService service,
            Entity source,
            Guid transactionId,
            ITracingService tracingService)
        {
            Guid tpcAllocId = GetLookupId(source, FieldAssociatedTpcAlloc);

            if (tpcAllocId == Guid.Empty)
            {
                tracingService.Trace("No TPC Allocation set - cannot resolve OCC.");
                return Guid.Empty;
            }

            //Retrieve the TPC Allocation to get its PRISM Occ
            var tpcAlloc = service.Retrieve(
                EntityTpcAllocation,
                tpcAllocId,
                new ColumnSet(TpcAllocFieldPrismOcc));

            Guid occId = GetLookupId(tpcAlloc, TpcAllocFieldPrismOcc);

            if (occId == Guid.Empty)
            {
                tracingService.Trace("TPC Allocation has no PRISM OCC - cannot resolve OCC.");
                return Guid.Empty;
            }

            //Write the resolved OCC back to the transaction so future
            //plugin executions can read it directly without re-resolving
            if (transactionId != Guid.Empty)
            {
                var updateTransaction = new Entity(EntityTransaction, transactionId);
                updateTransaction[FieldAssociatedOcc] = new EntityReference(EntityOcc, occId);
                service.Update(updateTransaction);
                tracingService.Trace($"Wrote biteam_associatedocc ({occId}) back to transaction {transactionId}.");

            }

            return occId;
        }

        // -- Helpers to aggregate total amount for PR + OCC
        private decimal AggregateTotalAmount(
            IOrganizationService service,
            Guid purchaseRequestId,
            Guid occId,
            ITracingService tracingService
            )
        {
            var query = new QueryExpression(EntityTransaction)
            {
                ColumnSet = new ColumnSet(FieldTransactionAmount),
                Criteria = new FilterExpression(LogicalOperator.And)
            };

            query.Criteria.AddCondition(FieldPurchaseRequest, ConditionOperator.Equal, purchaseRequestId);
            query.Criteria.AddCondition(FieldAssociatedOcc, ConditionOperator.Equal, occId);

            //Only Active Records
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);

            var results = service.RetrieveMultiple(query);

            decimal total = results.Entities
                .Where(e => e.Contains(FieldTransactionAmount) && e[FieldTransactionAmount] != null)
                .Sum(e => ((Money)e[FieldTransactionAmount]).Value);

            tracingService.Trace($"Aggregated total {total} across {results.Entities.Count} transaction(s).");
            return total;
        }

        // -- Get OCC display code 

        private string GetOccCode(IOrganizationService service, Guid occId)
        {
            var occ = service.Retrieve(EntityOcc, occId, new ColumnSet(OccFieldCode));
            return occ.Contains(OccFieldCode) ? occ[OccFieldCode].ToString() : occId.ToString();
        }

        // --Upsert summary row

        private void UpsertOccSummary(
            IOrganizationService service,
            Guid purchaseRequestId,
            Guid occId,
            string occCode,
            decimal newTotal,
            ITracingService tracingService)
        {
            //Check for existing summary row
            var query = new QueryExpression(EntityOccSummary)
            {
                ColumnSet = new ColumnSet(SummaryFieldName, SummaryFieldTotal),
                Criteria = new FilterExpression(LogicalOperator.And)
            };

            query.Criteria.AddCondition(SummaryFieldPR, ConditionOperator.Equal, purchaseRequestId);
            query.Criteria.AddCondition(SummaryFieldOcc, ConditionOperator.Equal, occId);

            var existing = service.RetrieveMultiple(query);

            if (existing.Entities.Count > 0)
            {
                //Update existing
                var summaryRecord = new Entity(EntityOccSummary, existing.Entities[0].Id);
                summaryRecord[SummaryFieldTotal] = new Money(newTotal);
                service.Update(summaryRecord);
                tracingService.Trace($"Updated existing OCC summary row: {existing.Entities[0].Id}");
            }
            else
            {
                var summaryRecord = new Entity(EntityOccSummary);
                summaryRecord[SummaryFieldName] = occCode;
                summaryRecord[SummaryFieldPR] = new EntityReference("biteam_purchaserequest", purchaseRequestId);
                summaryRecord[SummaryFieldOcc] = new EntityReference(EntityOcc, occId);
                summaryRecord[SummaryFieldTotal] = new Money(newTotal);

                var newId = service.Create(summaryRecord);
                tracingService.Trace($"Created new OCC summary row: {newId}");
            }
        }

        // -- HELPERS 

        private Guid GetLookupId(Entity entity, string fieldName)
        {
            if (entity == null || !entity.Contains(fieldName) || entity[fieldName] == null)
                return Guid.Empty;
            return ((EntityReference)entity[fieldName]).Id;

        }
        /// <summary>
        ///Merges the Update target with the PostImage so partial Update
        ///payloads still have access to unchanged lookup fields
        /// </summary>

        private Entity MergeWithPostImage(Entity target, IPluginExecutionContext context)
        {
            if (!context.PostEntityImages.Contains("PostImage"))
                return target ?? new Entity(EntityTransaction);

            var postImage = context.PostEntityImages["PostImage"];

            if (target == null)
                return postImage;

            //Start from postImage, overlay target values (target wins on conflict)
            var merged = new Entity(target.LogicalName, target.Id);
            foreach (var attr in postImage.Attributes)
                merged[attr.Key] = attr.Value;
            foreach (var attr in target.Attributes)
                merged[attr.Key] = attr.Value;

            return merged;
        }

    }


}
