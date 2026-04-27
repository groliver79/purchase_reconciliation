using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.ServiceModel;

namespace ProcurementPlugins
{
    /// <summary>
    /// Post-Operation plugin that automatically creates a linked cr6c5_PCA
    /// record whenever a new biteam_purchaserequest record is created.
    /// Derives the PCA number by replacing the "TPC-" prefix with "PCA-".
    /// 
    /// Example: TPC-26-151515 -> PCA-26-151515
    /// 
    /// Registration settings (Plugin Registration Tool):
    /// Message: Create
    /// Entity: biteam_purchaserequest
    /// Stage: Post-Operation (40)
    /// Mode: Synchronous
    /// Execution Order: 1
    /// </summary>
    /// 
    public class CreatePcaRecordPlugin : IPlugin
    {
        private const string TpcEntityName = "biteam_purchaserequest";
        private const string TpcNameAttribute = "biteam_name";
        private const string TpcPrefix = "TPC-";
        private const string PcaEntityName = "cr6c5_pca";
        private const string PcaNameAttribute = "cr6c5_pcanumber";
        private const string PcaLookupAttribute = "biteam_purchaserequest";
        private const string PcaPrefix = "PCA-";

        private const string CreateMessage = "Create";
        private const int PostOperationStage = 40;

        public void Execute(IServiceProvider serviceProvider)
        {
            //---1. Resolve Services

            if (serviceProvider == null)
                throw new ArgumentNullException(nameof(serviceProvider));

            var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService))
                ?? throw new InvalidPluginExecutionException("Failed to obtain ITracingService");

            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext))
                ?? throw new InvalidPluginExecutionException("Failed to obtain IpluginExecutionContext");

            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory))
                ?? throw new InvalidPluginExecutionException("Failed to obtain IOrganizationServiceFactory");

            //null= run as system user - reliable access regardless of
            //the calling user's security role
            var orgService = serviceFactory.CreateOrganizationService(null);

            tracingService.Trace(
                "CreatePcaPlugin: Entered." +
                "Message={0}, Stage={1}, Depth={2}, Entity={3}",
                context.MessageName, context.Stage, context.Depth,
                context.PrimaryEntityName);

            //---2. Guards
            if(!context.MessageName.Equals(CreateMessage, StringComparison.OrdinalIgnoreCase)) 
            {
                tracingService.Trace("Exiting: message not Create.");
                return;
            }

            if(context.Stage != PostOperationStage) 
            {
                tracingService.Trace("Exiting: stage is not Post-Operation(40).");
                return;
            }

            if(context.Depth >1) 
            {
                tracingService.Trace(
                    "Exiting: depth > 1, skipping to prevent recursion");
                return;
            }

            if(!context.InputParameters.Contains("Target") ||
                !(context.InputParameters["Target"] is Entity target))
            {
                tracingService.Trace("Exiting: Target missing or not an Entity");
                return;
            }

            if(!target.LogicalName.Equals(TpcEntityName, StringComparison.OrdinalIgnoreCase))
            {
                tracingService.Trace(
                    "Exiting: entity is '{0}', expected '{1}'.",
                    target.LogicalName, TpcEntityName);
                return;
            }

            try
            //---3. Start of Business Logic: Read the committed TPC number
            {
                //Post-Operation fires after Dataverse commits the record.
                //Target contains the values sent in, including the TPC-prefix
                //that AddTpcPrfixPlugin wrote at Stage 20.
                string tpcName =
                    target.GetAttributeValue<string>(TpcNameAttribute)
                    ?? string.Empty;

                tracingService.Trace("TPC name read from Target: '{0}'", tpcName);

                //---4. Guard: confirm TPC prefix is Present
                //Add TpcPrefixPlugin (Stage 20) should always run first
                //but code below guards against future misconfiguration or changes
                if (!tpcName.StartsWith(TpcPrefix, StringComparison.OrdinalIgnoreCase)) 
                { 
                    tracingService.Trace(
                        "TPC name '{0}' does not start with '{1}." +
                        "AddTpcPrefixPlugin may not have fired." +
                        "Cannot derive PCA number. Exiting",
                        tpcName, TpcPrefix);
                    return;
                
                }
                //---5. Derive the PCA Number
                //Strip "TPC-" and prepend "PCA-"
                //e.g. TPC-26-151515  -> PCA-26-151515
                string pcaName = PcaPrefix + tpcName.Substring(TpcPrefix.Length);

                tracingService.Trace("Dervied PCA name: '{0}'", pcaName);

                //---6. Get the parent TPC record ID for the lookup
                //PrimaryEntityId is the ID of the newly committed TPC record
                Guid tpcRecordId = context.PrimaryEntityId;

                tracingService.Trace("Parent TPC record ID: {0}", tpcRecordId);

                //---7.Idempotency check
                //Query Dataverse for any existing PCA linked to this TPC record.
                //This is the primary guard against duplicates regardless of whether the cause is a 
                //double step registration, a solution import collision, or any indirect re-trigger scenario.

                tracingService.Trace(
                    "Query for existing PCA record linked to TPC ID: {0}",
                    tpcRecordId);

                QueryExpression query = new QueryExpression(PcaEntityName)
                {
                    ColumnSet = new ColumnSet(PcaNameAttribute), TopCount = 1,
                    Criteria = new FilterExpression(LogicalOperator.And)
                };

                query.Criteria.AddCondition(
                    PcaLookupAttribute, ConditionOperator.Equal, tpcRecordId);

                EntityCollection existing = orgService.RetrieveMultiple(query);

                if (existing.Entities.Count > 0) 
                {
                    tracingService.Trace(
                        "Existing PCA record found for TPC ID {0}." +
                        "Existing PCA name: '{1}'" +
                        "Skipping creation to prevent duplicate.", tpcRecordId, 
                        existing.Entities[0].GetAttributeValue<string>(PcaNameAttribute));
                    return;
                }

                tracingService.Trace(
                    "No exisitng PCA record found. Proceeding with creation");

                //---8. Build the new PCA record
                Entity pcaRecord = new Entity(PcaEntityName);

                //The derived PCA number e.g. PCA-26-151515
                pcaRecord[PcaNameAttribute] = pcaName;

                //Lookup back to the parent biteam_purchaserequest record
                pcaRecord[PcaLookupAttribute] = new EntityReference(TpcEntityName, tpcRecordId);

                //---8. Commit the PCA recprd to Dataverse
                Guid pcaRecordId = orgService.Create(pcaRecord);

                tracingService.Trace("PCA record successfully create." +
                    "PCA ID={0}, PCA Name = '{1}', Linked TPC ID={2}",
                    pcaRecordId, pcaName, tpcRecordId);
            }

            catch (InvalidPluginExecutionException)
            {
                //Re-throw as is - already formatted for the user
                throw;
            }
            catch (FaultException<OrganizationServiceFault> ex)
            {
                tracingService.Trace(
                    "OrganizationServiceFault: {0}", ex.ToString());
                throw new InvalidPluginExecutionException(
                    "A Dataverse service fault occurred in" +
                    "CreatePcaRecordPlugin", ex);
            }
            catch (Exception ex) 
            {
                tracingService.Trace(
                    "Unexpected exception: {0}", ex.ToString());
                throw new InvalidPluginExecutionException(
                    "An unexpected error occurred in" +
                    "CreatePcaRecordPlugin.", ex);
            }
        }

    }
}
    

