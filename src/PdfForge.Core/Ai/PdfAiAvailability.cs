namespace PdfForge.Core.Ai;

public sealed record PdfAiAvailability(
    bool EnabledByUser,
    bool EndpointReachable,
    string Message)
{
    public bool CanUseAi => EnabledByUser && EndpointReachable;

    public static PdfAiAvailability Disabled(string message = "Local AI features are disabled in settings.") =>
        new(false, false, message);
}
