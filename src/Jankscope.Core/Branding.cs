namespace Jankscope.Core;

/// <summary>
/// Single source of truth for product naming. A rename is a change here plus the resource strings (spec §2.2 #10).
/// The support and store URLs are static values that the app never fetches (HC-13); empty hides the control.
/// </summary>
public static class Branding
{
    public const string ProductName = "Jankscope";
    public const string EtwSessionPrefix = ProductName + "-";
    public const string PipePrefix = ProductName + ".";
    public const string DataFolderName = ProductName;

    public const string SponsorUrl = "";
    public const string StoreUrl = "";
}
