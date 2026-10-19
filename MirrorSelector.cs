using System.Net;

namespace MajdataSync;

internal sealed class SiteRoute
{
    public required string Host { get; init; }
    public long LatencyMs { get; init; }
    public IReadOnlyList<IPAddress> Addresses { get; init; } = [];
    public bool Available { get; init; }
}

internal static class MirrorSelector
{
    public const string PrimaryHost = "majdata.net";

    public static readonly string[] Mirrors =
    [
        "maj-1.moyingmoe.top",
        "maj-2.moyingmoe.top",
        "maj-3.moyingmoe.top"
    ];

    public static string ApiRoot(string host) => $"https://{host}/api3/api";

    public static string ChartRoot(string host, string chartId) =>
        $"{ApiRoot(host)}/maichart/{Uri.EscapeDataString(chartId)}";
}
