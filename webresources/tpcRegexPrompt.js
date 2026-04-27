//the following code tests the input of a TPC number field against a specific format 
// (YY-XXXXXX) and provides user feedback through notifications on the form. 
// It validates the format on both change and save events, ensuring that users 
// enter the TPC number correctly before saving the record. 
// The code also includes helpful hints and error messages to guide users in entering the correct format.

var ProcurementForms = ProcurementForms || {};

ProcurementForms.Tpc = (function () {
    // ── Constants ─────────────────────────────────────────────────────────────
    const FIELD_NAME = "biteam_name";
    const NOTIFICATION_ID = "tpc_name_hint";

    // Exact valid format: 2 digits + dash + 6 digits
    const VALID_PATTERN = /^\d{2}-\d{6}$/;

    // Explicit partial stage patterns — one per typing stage
    const STAGE_YEAR = /^\d{1,2}$/; // "2" or "26"
    const STAGE_DASH = /^\d{2}-$/; // "26-"
    const STAGE_NUMBER = /^\d{2}-\d{1,5}$/; // "26-1" through "26-12345"

    const CURRENT_FY = new Date().getFullYear().toString().slice(-2);
    const PRIOR_FY = (parseInt(CURRENT_FY, 10) - 1).toString().padStart(2, "0");

    //Helper to get form context and attribute
    function _getFormData(executionContext) {
        // Validate executionContext and formContext before accessing
        //Also to account when user saves form before onLoad has a chance to run and set up the expected context
        if (!executionContext || typeof executionContext.getFormContext !== "function") {
            console.warn("_getFormData: Invalid executionContext is not ready." 
                + "Returning null.");
                return null;
        }

        
        var formContext = executionContext.getFormContext();
        //Validate formContext before accessing its properties
        if (!formContext) {
            console.warn("_getFormData: Invalid formContext is not ready." 
                + "Returning null.");
            return null;
        }

        var formType = formContext.ui.getFormType();
        var attribute = formContext.getAttribute(FIELD_NAME);
        return { formContext, formType, attribute };
    }

    // --onLoad ─────────────────────────────────────────────────────────────
    // Wire to OnLoad. Tick "Pass execution context as first parameter."
    function onLoad(executionContext) {
        try {
           var data = _getFormData(executionContext);
           if (!data) {
            console.warn("onLoad: Form data is not ready. Exiting onLoad.");
            return;
           }

           var formContext = data.formContext;
           var formType = data.formType;
           var attribute = data.attribute;

            // Always clear all notifications on every load
            _clearAllControlNotifications(formContext);

            if (formType !== 1) { 
                console.log("onLoad: Existing form loaded"); // Not "Create" form
                return;
            }

            console.log("onLoad: Create form loaded");
            _showHint(formContext);

            if (attribute) {
                attribute.addOnChange(_onTpcNumberChange);
            } else {
                console.warn("Attribute not found: " + FIELD_NAME);
            }

        } catch (e) {
            console.log("onLoad error:", e);
        }
    }
// --onSave ─────────────────────────────────────────────────────────────
// Wire to OnSave. Tick "Pass execution context as first parameter."
  function onSave(executionContext) {
        try {
            var data = _getFormData(executionContext);
            if (!data) {
                console.warn("onSave: Form data is not ready. Exiting onSave.");
                return;
            }
            var formContext = data.formContext;
            var formType = data.formType;
            var attribute = data.attribute;

            if (formType !== 1) return;

            _clearAllControlNotifications(formContext);

       
            if (!attribute) return;

            var value = (attribute.getValue() || "").trim();

            console.log("onSave: value='" + value + "' | valid=" + _isValidFormat(value));

            if (!_isValidFormat(value)) {
                if (value.startsWith("TPC-") && value.length ===13) { 
                    console.log("onSave: TPC number is valid (with prefix).");
                    _clearAllControlNotifications(formContext);
                    return;
                } else {
                 executionContext.getEventArgs().preventDefault();
                _showError(formContext, "TPC number must be in the format YY-XXXXXX " +
                    "(e.g. " + CURRENT_FY + "-101010 or prior year " + PRIOR_FY + "-101010). " +
                    "TPC- prefix is added automatically on save.");
                }
            } 

        } catch (e) {
            console.error("onSave error:", e);
        }
    }

//---_onTpcNumberChange-─────────────────────────────────────────────────────────────
//Fire on blur when field loses focus / standard in Dataverse
//onChange is synchronous.  There is no "onInput" or "onKeyUp" equivalent
function _onTpcNumberChange(executionContext) {
    console.log("_onTpcNumberChange triggered");
    try { 
        var data = _getFormData(executionContext);
        if (!data) {
            console.warn("onTpcNumberChange: Form data is not ready. Exiting.");
            return;
        }
        var formContext = data.formContext;
        var attribute = data.attribute;

        if (!attribute) {
            console.warn("Attribute not found: " + FIELD_NAME);
            return;
        }
        var value = (attribute.getValue() || "").trim();
            console.log("value='" + value + "' | length=" + value.length + " | valid=" + _isValidFormat(value));

            _clearAllControlNotifications(formContext);

            // ── Case 1: Empty — restore hint ──────────────────────────────────
            if (value === "") {
                _showHint(formContext);
                return;
            }

            // ── Case 2: Exact valid format ────────────────────────────────────
            if (_isValidFormat(value)) {
                console.log("Valid format — notifications cleared.");
                return;
            }
            // ── Case 3: Partial valid — guide user toward correct format ──────
            if (STAGE_YEAR.test(value)) {
                _showInfoNotification(formContext, "Enter a two-digit fiscal year then add a dash " +
                    "(e.g. " + CURRENT_FY + "-).");
                return;
            }
            if (STAGE_DASH.test(value)) {
                _showInfoNotification(formContext, "Good — now enter exactly 6 digits " +
                    "(e.g. " + CURRENT_FY + "-101010).");
                return;
            }
            if (STAGE_NUMBER.test(value)) {
                var digitsEntered = value.split("-")[1].length;
                var digitsNeeded = 6 - digitsEntered;
                _showInfoNotification(formContext, "Enter " + digitsNeeded + " more digit" +
                    (digitsNeeded === 1 ? "" : "s") + " after the dash.");
                return;
            }

            // ── Case 4: Wrong format entirely ─────────────────────────────────
            _showError(formContext, "Format must be YY-XXXXXX — two digits, a dash, then " +
                "exactly six digits (e.g. " + CURRENT_FY + "-101010). " +
                "TPC- prefix is added automatically on save.");
        } 
        catch (e) {
            console.log("_onTpcNumberChange error:", e);
        }
    }

//--Helpers────────────────────────────────────────────────────────────
function _isValidFormat(value) {
    return VALID_PATTERN.test(value);
}

function _clearAllControlNotifications(formContext) {
    formContext.ui.controls.forEach(function (control) {
        try {
            control.clearNotification(NOTIFICATION_ID);
        } catch (e) {
            // ignore controls that do not support notifications
        }
    });

}

//---Notifications Helpers────────────────────────────────────────────────────────────
function _showHint(formContext) {
    var control = formContext.getControl(FIELD_NAME);
    if (control) {
        control.setNotification(
            "Enter in format: YY-XXXXXX (e.g. " + CURRENT_FY + "-123456 or prior year " + PRIOR_FY + "-123456). TPC- prefix is added automatically.",
            NOTIFICATION_ID
        );
    }
}

function _showInfoNotification(formContext, message) {
    var control = formContext.getControl(FIELD_NAME);
    if (control) {
        control.setNotification(message, NOTIFICATION_ID);
    }
}

function _showError(formContext, message) {
    var control = formContext.getControl(FIELD_NAME);
    if (control) {
        control.setNotification(message, NOTIFICATION_ID);
    }
}

//--Public API ─────────────────────────────────────────────────────────────
return { 
    onLoad: onLoad,
    onSave: onSave,
    onTpcNumberChange: _onTpcNumberChange
    };
}());
