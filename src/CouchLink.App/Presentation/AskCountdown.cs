namespace CouchLink.App.Presentation;

/// <summary>The approval toast's countdown: how much of the bar is left (1 to 0) and its text.</summary>
internal readonly record struct AskCountdown(double Remaining, string Text)
{
    public static AskCountdown For(TimeSpan elapsed, TimeSpan timeout)
    {
        var left = timeout - elapsed;
        if (left < TimeSpan.Zero)
            left = TimeSpan.Zero;
        double remaining = timeout <= TimeSpan.Zero ? 0 : left / timeout;
        int seconds = (int)Math.Ceiling(left.TotalSeconds);
        return new(remaining, $"Denied automatically in {seconds} s");
    }
}
