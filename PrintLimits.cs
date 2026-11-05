namespace Skuload.PrintBridge;

/// <summary>
/// Hard limits for one print job. A label is a small PDF or image, so these sit
/// far above real use and only stop a caller from exhausting the machine.
/// </summary>
internal static class PrintLimits
{
    /// <summary>Largest request body we read (the label is base64, about 7.5 MB of file).</summary>
    public const int MaxBodyBytes = 10 * 1024 * 1024;

    /// <summary>Most pages in one job.</summary>
    public const int MaxPages = 20;

    /// <summary>Largest page side in PDF points (72 per inch): 24 inches.</summary>
    public const float MaxPageSidePoints = 24 * 72;

    /// <summary>Largest decoded image, in pixels.</summary>
    public const long MaxImagePixels = 50_000_000;

    /// <summary>Jobs rendering or printing at the same time.</summary>
    public const int MaxConcurrentJobs = 2;

    /// <summary>How long a job waits for a free slot before the caller is told to retry.</summary>
    public static readonly TimeSpan QueueWait = TimeSpan.FromSeconds(10);
}

/// <summary>
/// The label itself can't be printed (wrong format, too big, unknown printer).
/// Its message is written for the caller; any other failure stays in the log.
/// </summary>
internal sealed class LabelRejectedException : Exception
{
    public LabelRejectedException(string message) : base(message)
    {
    }
}
