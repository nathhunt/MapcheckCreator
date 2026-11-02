using VMS.TPS.Common.Model.API;

// Write-enabled standalone executable: needs approval in Eclipse Script Approvals (Tools > Script Approvals)
// before it can modify patients. Reading works without approval.
[assembly: ESAPIScript(IsWriteable = true)]
