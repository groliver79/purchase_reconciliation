using Microsoft.Xrm.Sdk;
using System;
using System.ServiceModel;

namespace ProcurementPlugins
{
    /// <summary>
    /// Pre-Operation plugin that adds "PCA-" prefix to the cr6c5_pcanumber field
    /// on new cr6c5_PCA records before they are created in Dataverse.
    /// 
    /// Registration settings (Plugin Registration Tool):
    /// Message: Create
    /// Entity: cr6c5_PCA
    /// Stage: Pre-Operation(20)
    /// Mode: Synchronous
    /// Execution Order: 1
    /// </summary>
    public class AddPcaPrefixPlugin : IPlugin
    {
        //Constants for self-documentation and maintenance
        private const string TargetEntityLogicalName = "cr6c5_PCA";
        private const string NameAttribute = "cr6c5_pcanumber";
        private const string Prefix = "PCA-";
        private const string CreateMessage = "Create";
        private const int PreOperationStage = 20;

        public void Execute(IServiceProvider serviceProvider)
        {
            //---1.Resolve services
            if (serviceProvider == null)
                throw new ArgumentNullException(nameof(serviceProvider));
            var tracingService =
                (ITracingService)serviceProvider.GetService(typeof(ITracingService))
                ?? throw new InvalidPluginExecutionException("Failed to obtain ITracingService");

            var context =
                (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext))
                ?? throw new InvalidPluginExecutionException("Failed to obtain IPluginExecutionContext.");

            tracingService.Trace("AddPcaPrefixPlugin: Entered. Message {0}, Stage={1}, Depth= {2}, Entity={3}",
                context.MessageName, context.Stage, context.Depth, context.PrimaryEntityName);

            //---2. Guard: Ensure correct message
            if (!context.MessageName.Equals(CreateMessage, StringComparison.OrdinalIgnoreCase)) 
            {
                tracingService.Trace("Exiting: message is not Create");
                return;
            }
            //---3. Guard: Ensure correct pipepline stage (Pre-Operation =20)
            if (context.Stage != PreOperationStage)
            {
                tracingService.Trace("Exiting: stage is not Pre-Operation(20)");
                return;
            }
            //---4. Guard: Prevent rescursive/cascading plugin execution
            if (context.Depth > 1) 
            {
                tracingService.Trace("Exiting: plugin Depth >1, skipping to prevent recursion");
                return;
            }

            //---5. Guard: Ensure target exists and is an Entity
            if (!context.InputParameters.Contains("Target") ||
                !(context.InputParameters["Target"] is Entity target)) 
            {
                tracingService.Trace("Exiting: Target is missing or is not an Entity");
                return;
            }

            //---6. Guard: Ensure correct entity logical name
            if (!target.LogicalName.Equals(TargetEntityLogicalName, StringComparison.OrdinalIgnoreCase)) 
            {
                tracingService.Trace(
                    "Exiting: entity logical name is '{0}', expected '{1}.",
                    target.LogicalName, TargetEntityLogicalName);
                return;
            }

            // Start of business logic and error handling
            try
            {
                //---7. Read the current name value (which may be absent or null)
                // GetAttributeValue<T> safely returns the default if missing,
                // avoiding a KeyNotFoundException unlike direct ["key"] access.
                string currentName = target.GetAttributeValue<string>(NameAttribute) ??
                    string.Empty;

                tracingService.Trace("Current value of '{0}':'{1}'", NameAttribute, currentName);

                //---8. Guard: Skip if prefix already present (to prevent duplicates)
                if(currentName.StartsWith(Prefix,StringComparison.OrdinalIgnoreCase)) 
                {
                    tracingService.Trace("Prefix already present. No changes made.");
                    return;
                }
                //---9. Apply the pre-fix and write it back to Target
                //Because this is Pre-operation, mutating Target here is enough
                //Dataverse will persist the modified value.
                //No separate org service call is needed.
                string newName = Prefix + currentName;
                target[NameAttribute] = newName;

                tracingService.Trace("Prefix applied. '{0} updated from '{1} to '{2}'.", NameAttribute, currentName, newName);
            }
            //Completion of Error Handling
            catch (InvalidPluginExecutionException)
            {
                //Re-throw plugin exception as is;
                throw;
            }
            catch (FaultException<OrganizationServiceFault> ex)
            {
                tracingService.Trace("OrganizationServiceFault: {0}", ex.ToString());
                throw new InvalidPluginExecutionException("A Dataverse service fault occurred in AddPcaPrefixPlugin", ex);
            }
            catch (Exception ex) 
            {
                tracingService.Trace("Unexpected exception: {0}", ex.ToString());
                throw new InvalidPluginExecutionException(
                    "An unexpected error occurred in AddPcaPrefixPlugin", ex);
            }
            
        }
    }
}
