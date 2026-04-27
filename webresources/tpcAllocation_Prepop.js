"use strict";

var ProcurementForms = ProcurementForms || {};

ProcurementForms.TpcAllocations = (function () {
// ── Constants ─────────────────────────────────────────────────────────────
const ALLOCATION_ENTITY = "biteam_tpcallocations";
const PARENT_LOOKUP_FIELD = "biteam_purchaserequest";


// Property containing the parent Purchase Request GUID (i.e TPC Number)
const PARENT_FILTER_PROPERTY = "_biteam_purchaserequest_value";

// Form attribute names — used to set values on the form
const FIELD_PROJECT_CODE = "biteam_projectcode";
const FIELD_PRISM_OCC = "biteam_prismocc";
const FIELD_REGIS_ORG_CODE = "biteam_regisorgcode";
const FIELD_BLI = "biteam_bli";
const FIELD_FUND_CODE = "biteam_fundcode";
const FIELD_AMOUNT = "biteam_tpcallocationamount";

// OData navigation property names — used in $select and to read response
const ODATA_PROJECT_CODE = "_biteam_projectcode_value";
const ODATA_PRISM_OCC = "_biteam_prismocc_value";
const ODATA_REGIS_ORG_CODE = "_biteam_regisorgcode_value";
const ODATA_BLI = "_biteam_bli_value";

// Lookup entity types — confirmed from diagnostic output    
// CRITICAL: these must match lookuplogicalname from the OData response    
// NOT the field logical name on biteam_tpcallocations
const ENTITY_PROJECT_CODE = "cr6c5_projecttask";
const ENTITY_PRISM_OCC = "cr6c5_objectclassification";
const ENTITY_REGIS_ORG_CODE = "cr6c5_regisorganizationcodes";
const ENTITY_BLI = "cr6c5_businesslineitem";


// ── onLoad ───────────────────────────────────────────────────────────────
// Wire to Quick Create form OnLoad.
// Tick "Pass execution context as first parameter".
    function onLoad(executionContext) {
        var formContext = executionContext.getFormContext();
        console.log("TpcAllocations.onLoad: Quick Create form loaded.");
        
        // ── Step 1: Get the parent Purchase Request ID ────────────────────
        var val = formContext.getAttribute(PARENT_LOOKUP_FIELD)?.getValue();
        console.log(`val: ${val}`);
        if (!val || val.length === 0) {
            console.log(
                "TpcAllocations.onLoad: No parent Purchase Request ID found. " +
                "Form may not have been opened from the subgrid. " +
                "Skipping pre-population.");
            return;
        }

        var parentIdGuid = val[0].id;
        var parentId = parentIdGuid.replace(/[{}]/g, "");

        console.log(
            "TpcAllocations.onLoad: Parent Purchase Request ID = " + parentId);
    
    //Step 2: Call function for the most recent TPC Allocation by passing parentId
    _getMostRecentAllocation(parentId) 
            .then(function(recentRecord) {
                if (!recentRecord) {
                    console.log(
                        "TpcAllocation.onLoad: No previous allocation found. " +
                        "Form will open blank.");
                    return;
                }
                console.log(
                    "TpcAllocation.onLoad: Previous allocation found. " +
                    "Pre-populating fields.");

            //Step 3: Call function to Pre-populate all fields from the recent record
            _prePopulateFields(formContext, recentRecord);    
        })
       .catch(function(error) {
                // Log but do not block form so user can still fill manually
                console.log(
                    "TpcAllocation.onLoad: WebAPI query failed. " +
                    "Form will open blank. Error: " +
                    JSON.stringify(error)
                );
            }
        );
    }
    
    //Private: Query most recent allocation for this Purchase Request
    //Returns a Promise that resolves to the most allocation record
    //or null if none exists.
    function _getMostRecentAllocation(parentId) {
        if (!parentId) {
            console.error("ParentID is undefined or invalid.");
            return Promise.reject("Invalid parentId");
        }
        // OData query to get the most recent allocation for this parentId
        // Select the fields we want to pre-populate, filter by parentId, order by createdon desc, and take top 1
        var query = `?$select=${ODATA_PROJECT_CODE},${ODATA_PRISM_OCC},${ODATA_REGIS_ORG_CODE},${ODATA_BLI} ` +
                    "&$filter=" + PARENT_FILTER_PROPERTY + " eq " + parentId +
                    "&$orderby=createdon desc&$top=1";
                    

        console.log(`_getMostRecentAllocation: query = ${query}`);

        return Xrm.WebApi.retrieveMultipleRecords(ALLOCATION_ENTITY, query)
            .then(function success(result) {
                if (result && result.entities && result.entities.length > 0) {
                    console.log("Most recent allocation found: " + JSON.stringify(result.entities[0]));
                    return result.entities[0];
                }
                console.log("No previous allocation found for this Purchase Request.");
                return null;
            },
            function error(err) {
                console.log("WebAPI responded with: ", + err.message);
            }
        );
    }

    //Private: Pre-populate form fields from the most recent allocation record
    //Lookup fields: pass the ODATA property name so _setLookup
    //can read both the ID and the formatted display name for the lookup.
    function _prePopulateFields(formContext, recentRecord) {
        if (recentRecord) {
            _setLookup(formContext, FIELD_PROJECT_CODE, recentRecord, ODATA_PROJECT_CODE, ENTITY_PROJECT_CODE);
            _setLookup(formContext, FIELD_PRISM_OCC, recentRecord, ODATA_PRISM_OCC, ENTITY_PRISM_OCC);
            _setLookup(formContext, FIELD_REGIS_ORG_CODE, recentRecord, ODATA_REGIS_ORG_CODE, ENTITY_REGIS_ORG_CODE);
            _setLookup(formContext, FIELD_BLI, recentRecord, ODATA_BLI, ENTITY_BLI);

            // Fund code and amount are intentionally not pre-populated
            // as these should be entered by the user for each allocation.
            
            // Call function to show a notification to the user that fields were pre-populated
           _showReviewNotification(formContext);
            console.log("Fields pre-populated from the most recent allocation record.");
        };
    }
    
    //Private: Set a lookup field value on the form
    //Takes the form context, the field name on the form, the recent record,
    //the OData property name to read the lookup value from, and the entity type for the lookup.
    function _setLookup(formContext, fieldName, recentRecord, odataProperty, entityType) {
        try {
            var lookupId = recentRecord[odataProperty];
            var lookupName = recentRecord[odataProperty + "@OData.Community.Display.V1.FormattedValue"];
            

            if (!lookupId) {
                console.warn(`_setLookup: No value found for ${odataProperty} in recent record. Skipping`);
                return;
            }

            var attr = formContext.getAttribute(fieldName);
            if (!attr) {
                console.error(`_setLookup: Form field ${fieldName} not on form.`);
                return;
            }
            attr.setValue([{
                id: lookupId,
                name: lookupName,
                entityType: entityType
            }]);
            console.log(`_setLookup: Set ${fieldName} to ${lookupName} (ID: ${lookupId})`);
        } 
        catch (error) {
            console.error("Error occurred while setting lookup field on " + fieldName + ": " + error.message);
        }
    }
    //Private: Show a form notification to review pre-populated values
    function _showReviewNotification(formContext) {
        formContext.ui.setFormNotification(
            "Fields are pre-populated from the most recent allocation. Please review and update as needed. " +
            "Pay special attention to the Project Code, which has a pre-selected Task Code. " +
            "NOTE: Fund Code and Amount are blank.",
            "INFO",
            "PrePopNotification"
        );  
          // Use a timeout to ensure the notification is set before trying to modify it
        setTimeout(function() {
            // Find the notification element by its unique ID
            var notificationElement = document.querySelector(`[id='message-PrePopNotification']`);

                if (notificationElement) {
                    // Modify the notification's HTML content
                    notificationElement.innerHTML = notificationElement.innerHTML.replace(
                        "most recent allocation",
                        "<strong>most recent allocation</strong>"
                    ).replace(
                        "NOTE",
                        "<span style='color: red; font-weight: bold;'>NOTE</span>"
                    );
                }
        }, 100);
    }
    // ── Public API ────────────────────────────────────────────────────────────
    return {
        onLoad: onLoad
    };
})();
