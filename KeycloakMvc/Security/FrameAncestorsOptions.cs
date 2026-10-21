namespace KeycloakMvc.Security;

/// <summary>Origins allowed to show the application in an iframe, bound from the "FrameAncestors" array.</summary>
public sealed class FrameAncestorsOptions
{
    public const string SectionName = "FrameAncestors";

    public string[] Origins { get; set; } = [];
}
