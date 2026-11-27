namespace AUBDigitalAcademy.Models.ViewModels;

public class ProfileSecurityViewModel
{
    public string FullName { get; set; } = string.Empty;
    public string? UniversityId { get; set; }
    public string? Email { get; set; }
    public string RoleSummary { get; set; } = string.Empty;

    public bool IsTelegramVerified { get; set; }
    public bool HasLinkedGoogle { get; set; }
    public bool HasUsableLocalPassword { get; set; }
    public bool IsGoogleConfigured { get; set; }

    /// <summary>
    /// Current step-up state extracted from the protected pending cookie: "link-otp", "unlink-otp", or null.
    /// </summary>
    public string? PendingActionStage { get; set; }

    public string? StatusMessage { get; set; }
    public string? ErrorMessage { get; set; }
}
