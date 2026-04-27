using Microsoft.Xrm.Sdk;
using System;
using Microsoft.Xrm.Sdk.Query;
using System.ServiceModel;
using System.Web.UI.WebControls;

namespace ProcurementPlugins
{   ///<summary>
    ///Pre-Operation Delete plugin that recalculates or removes the
    ///biteam_occsummary record when a biteam_tpcallocations record is 
    ///hard deleted.
    ///
    /// Registration Settings (Plugin Registration Tool): 
    /// Message:    Delete
    /// Entity:     biteam_tpcallocations
    /// Stage: Pre-Operation (20)
    /// Mode: Synchronous
    /// Execution Order 1
    /// 
    /// Pre-Image required:
    /// Image Type: Pre-Image
    /// Name: PreImage
    /// Alias: PreImage
    /// Parameters: All fields
    ///</summary>
    public class OccSummaryOnAllocationDeletePlugin : IPlugin
    {
        //Plugin Global Variables
        //--biteam_tpcallocation fields------------------
        private const string AllocationEntity = "biteam_tpcallocations";
        private const string AllocationOccField = "biteam_prismocc";
        private const string AllocationAmountField = "biteam_tpcallocationamount";
        private const string AllocationParentField = "biteam_purchaserequest";

        //--biteam_occsummary fields
        private const string OccSummaryEntity = "biteam_occsummary";
        private const string OccSummaryParentField = "biteam_purchaserequest";  
        private const string OccSummaryOccField = "biteam_name";         
        private const string OccSummaryAmountField = "biteam_totaltransactionamount";

        private const string PreImageAlias = "PreImage";
        private const string DeleteMessage = "Delete";
        private const int PreOperationStage = 20;

        public void Execute(IServiceProvider serviceProvider) 
        { 
            //--1. Resolve Services
            if (serviceProvider == null) 
                throw new ArgumentNullException(nameof(serviceProvider));

            var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService))
                ?? throw new InvalidPluginExecutionException("Failed to obtain ITracingService.");

            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext))
                ?? throw new InvalidPluginExecutionException("Failed to obtain IpluginExecutionContext.");

            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory))
                ?? throw new InvalidPluginExecutionException("Failed to obtain IOrganizationServiceFactory.");

            var orgService = serviceFactory.CreateOrganizationService(null);

            tracingService.Trace(
                "OccSummaryOnAllocationDeletePlugin: Entered" +
                "Message = {0}, Stage = {1}, Depth={2}",
                context.MessageName, context.Stage, context.Depth);

            //--2. Guards
            if (!context.MessageName.Equals(DeleteMessage, StringComparison.OrdinalIgnoreCase))
            {
                tracingService.Trace("Exiting: message is not Delete");
                return;
            } 

            if (context.Stage != PreOperationStage) 
            {
                tracingService.Trace(
                    "Exiting: stage is not Pre-Operation(20).");
                return;
            }

            if (context.Depth > 1) 
            {
                tracingService.Trace("Exiting: Depth > 1");
                return;
            }


            //--3. Start of Business Logic : Read deleted record values from the Pre-Image
            //Note: Pre-Image is the ONLY reliable source for a record being deleted
            if (!context.PreEntityImages.Contains(PreImageAlias)) 
            {
                tracingService.Trace("Pre-image '" + PreImageAlias + "'not found" +
                    "Ensure Pre-Image is registered on this Delete step.");
                    return;
            }

            Entity preImage = context.PreEntityImages[PreImageAlias];

            //Read the OCC lookup from the allocation being deleted
            var occRef = preImage.GetAttributeValue<EntityReference>(AllocationOccField);

            if (occRef == null) 
            {
                tracingService.Trace(
                    "No OCC value on deleted allocation." +
                    "Nothing to recalculate.  Exiting.");
                return;
            }

            //Read the parent Purchase Request lookup
            var parentRef = preImage.GetAttributeValue<EntityReference>(
                AllocationParentField);
            if (parentRef == null) 
            {
                tracingService.Trace("No parent Purchase request on deleted allocation." +
                    "Exiting.");
                return;
            }

            Guid occId = occRef.Id;
            Guid parentId = parentRef.Id;
            Guid deletingId = context.PrimaryEntityId;

            tracingService.Trace(
                "Deleted allocation OCC ID = {0}, Parent ID = {1} " +
                "Deleting Allocation ID = {2}",
                occId, parentId, deletingId);

            try
            {
                //--4. Query remaining allocations for the same OCC and parent
                //EXCLUDE the record being - deleted - because it still exists at
                //Pre-Operatioon stage so we must filter it out manually

                QueryExpression remainingQuery = new QueryExpression(AllocationEntity)
                {
                    ColumnSet = new ColumnSet(AllocationAmountField),
                    Criteria = new FilterExpression(LogicalOperator.And)
                };

                remainingQuery.Criteria.AddCondition(
                    AllocationOccField,
                    ConditionOperator.Equal,
                    occId);

                remainingQuery.Criteria.AddCondition(
                    "biteam_tpcallocationsid",
                    ConditionOperator.NotEqual, deletingId);

                EntityCollection remaining = 
                    orgService.RetrieveMultiple(remainingQuery);

                tracingService.Trace(
                    "Remaining allocations for this OCC after deletion: {0}",
                    remaining.Entities.Count);

                //--5. Find the existing OCC Summary record
                QueryExpression summaryQuery =
                    new QueryExpression(OccSummaryEntity)
                    {
                        ColumnSet = new ColumnSet(
                            OccSummaryAmountField,
                            OccSummaryOccField),
                        TopCount = 1,
                        Criteria = new FilterExpression(LogicalOperator.And)
                    };

                summaryQuery.Criteria.AddCondition(
                    OccSummaryParentField,
                    ConditionOperator.Equal,
                    parentId);

                summaryQuery.Criteria.AddCondition(
                    OccSummaryOccField, ConditionOperator.Equal,
                    occId);

                EntityCollection summaryResults = 
                    orgService.RetrieveMultiple(summaryQuery);

                if (summaryResults.Entities.Count == 0) 
                {
                    tracingService.Trace(
                        "No OCC Summary record found for this OCC." +
                        "Nothing to update. Exiting.");
                    return;
                }

                Entity summaryRecord = summaryResults.Entities[0];

                Guid summaryId = summaryRecord.Id;

                tracingService.Trace(
                    "OCC Summary record found. ID ={0}", summaryId);

                //--6. Delete or update the OCC Summary
                if (remaining.Entities.Count == 0)
                {
                    //No allocations remain for this OCC - delete the summary
                    tracingService.Trace(
                        "No remaining allocations for OCC {0}." +
                        "Deleting OCC Summary Record {1}.",
                        occId, summaryId);

                    orgService.Delete(OccSummaryEntity, summaryId);

                    tracingService.Trace(
                        "OCC Summary record {0} deleted successfully.", summaryId);
                }
                else
                {
                    //Allocations remain - recalculate and update the total
                    decimal newTotal = 0m;

                    foreach (Entity allocation in remaining.Entities)
                    {
                        var amount = allocation.GetAttributeValue<Money>(AllocationAmountField);

                        if (amount != null)
                        {
                            newTotal += amount.Value;
                        }

                        tracingService.Trace("" +
                            "Remaining allocations found" +
                            "Recalculated total for OCC {0} = {1}",
                            occId, newTotal);
                    }

                    tracingService.Trace(
                        "Remaining allocations found" +
                        "Recalculated total for OCC {0} = {1}",
                        occId, newTotal);

                    Entity summaryUpdate = new Entity(OccSummaryEntity, summaryId);

                    summaryUpdate[OccSummaryAmountField] =
                        new Money(newTotal);

                    orgService.Update(summaryUpdate);

                    tracingService.Trace(
                        "OCC Summary record {0} updated with new total {1}.",
                        summaryId, newTotal);
                }
            }
            catch (InvalidPluginExecutionException) 
            {
                throw;
            }
            catch (FaultException<OrganizationServiceFault> ex) 
            {
                tracingService.Trace(
                    "OrganizationServiceFault: {0}", ex.ToString());
                throw new InvalidPluginExecutionException(
                    "A Dataverse service fault occurred in " +
                    "OccSummaryOnAllocationDeletePlugin.", ex);
            }
            catch (Exception ex) 
            {
                tracingService.Trace(
                    "Unexpected exeception: {0}", ex.ToString());
                throw new InvalidPluginExecutionException(
                    "An unexpected error occurred in" +
                    "OccSummaryOnAllocationDeletePlugin.", ex);
            }
        }
    }

}
