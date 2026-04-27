using Microsoft.Xrm.Sdk;
using System;
using System.ServiceModel;

namespace ProcurementPlugins
{    ///<summary>
     ///Pre-Operation plugin that adds a "TPC-" prefix to the biteam_name field
     ///on new biteam_purchaserequest records before they are created in Dataverse.
     ///
     /// Registration settings (Plugin Registration Tool):
     /// Message: Create 
     /// Entity: biteam_purchaserequest (Purchase Request)
     /// Stage: Pre-Operation (20)
     /// Mode: Synchronous
     /// Execution Order: 1
     ///</summary>
    public class AddTpcPrefixPlugin : IPlugin
    {
        //Constants for documentation and maintenance
        private const string TargetEntityLogicalName = "biteam_purchaserequest";
        private const string NameAttribute = "biteam_name";
        private const string Prefix = "TPC-";
        private const string CreateMessage = "Create";
        private const int PreOperationStage = 20;

        public void Execute(IServiceProvider serviceProvider) 
        { 
            //---1. Resolve Services

            if(serviceProvider == null)
                throw new ArgumentNullException(nameof(serviceProvider));

            var tracingService = 
                (ITracingService)serviceProvider.GetService(typeof(ITracingService))
                ?? throw new InvalidPluginExecutionException("Failed to obtain ITTracingService");

            var context =
                (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext))
                ?? throw new InvalidPluginExecutionException("Failed to obtain IPluginExecutionContext");

            //var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));

            tracingService.Trace(
                "AddTpcPrefixPlugin : Entered. Message={0}, Stage = {1}, Depth={2}, Entity={3}",
                context.MessageName, context.Stage, context.Depth, context.PrimaryEntityName);

            //---2.Guard: correct message

            //Check if on 'Create'
            if (!context.MessageName.Equals(CreateMessage, StringComparison.OrdinalIgnoreCase))
            {
                tracingService.Trace("Exiting: Message is not Create");
                return;
               
            }

            //---3.Guard: correct pipeline stage(Pre-Operation = 20)
            //Ensures to modify Target BEFORE the record ID is committed to the database
            if (context.Stage != PreOperationStage) 
            {
                tracingService.Trace("Exiting: stage is not Pre-Operation(20).");
                return;

            }

            //---4.Guard: prevent recursive/cascading plugin execution
            if (context.Depth >1) 
            {
                tracingService.Trace("Exiting: plugin depth > 1, skipping to prevent recursion");
                return;
            }

            //---5. Guard: Target exists and is an Entity
            if (!context.InputParameters.Contains("Target") ||
                !(context.InputParameters["Target"]is Entity target))
            {
                tracingService.Trace("Exiting: Target is missing or not an Entity.");
                return;
            }
            //---6. Guard : correct entity logical name
            if (!target.LogicalName.Equals(TargetEntityLogicalName, StringComparison.OrdinalIgnoreCase))
            {
                tracingService.Trace("Exiting: entity logical name is '{0}, expected {1}", 
                    target.LogicalName, TargetEntityLogicalName);
                return;

            }
            try
            {
                //---7.Read the current name value (which may be absent or null)
                //  GetAttributeValue<T> safely returns the default if missing,
                //  avoiding a KeyNotFoundException unlike direct ["key"] access.
                string currentName = target.GetAttributeValue<string>(NameAttribute) ?? string.Empty;

                tracingService.Trace("Current value of '{0}':'{1}'", NameAttribute, currentName);

                //---8.Guard: skip if prefix already present(prevents duplicates)
                if (currentName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                {
                    tracingService.Trace("Prefix already present. No changes made.");
                    return;
                }

                //---9.Apply the prefix and write it back to Target
                // Because this is Pre-Operation, mutating Target here is enough
                // Datavere will persist the modified value.  
                // No separate org service call needed.
                string newName = Prefix + currentName;
                target[NameAttribute] = newName;

                tracingService.Trace("Prefix applied. '{0}' updated from '{1}' to '{2}'.",
                    NameAttribute, currentName, newName);
            }
            catch (InvalidPluginExecutionException) 
            {
                //Re-throw plugin exceptions as is
                throw;
            }

            catch (FaultException<OrganizationServiceFault>ex) 
            {
                tracingService.Trace("OrganizationServiceFault: {0}", ex.ToString());
                throw new InvalidPluginExecutionException("A Dataverse service fault occurred in AddTpcPrefixPlugin", ex);
            
            }

            catch (Exception ex)
            {
                tracingService?.Trace("Unexpected exception: {0}", ex.ToString());
                throw new InvalidPluginExecutionException("An error occurred in AddTpcPrefixPlugin", ex);
            }
            
        }
    }
}
