using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HousingStudio.Core;

namespace HousingStudio.Game;

/// <summary>
/// Market board prices from Universalis (https://universalis.app), for the pieces and dyes no vendor
/// sells: asked in the background, up to 100 items a request, each answer kept 15 minutes. A request
/// that fails or takes over 10 seconds is dropped quietly (asked again after 2 minutes); the shopping
/// list works without prices. Only asked while Settings → "Look up market prices" is on.
/// </summary>
public sealed class MarketPrices : IDisposable
{
    public static readonly TimeSpan Fresh = TimeSpan.FromMinutes(15);

    /// <summary>A price older than this isn't shown at all (one kept between 15 minutes and this is, while a newer one is asked).</summary>
    private static readonly TimeSpan Shown = TimeSpan.FromHours(1);

    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(2);

    private readonly HttpClient http;
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentDictionary<(string Scope, uint Item), MarketQuote> quotes = new();
    private readonly ConcurrentDictionary<(string Scope, uint Item), DateTime> asked = new();
    private int version;
    private int busy;

    public MarketPrices()
    {
        http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var v = typeof(MarketPrices).Assembly.GetName().Version;
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"HousingStudio/{v?.ToString(3) ?? "0"} (+https://github.com/koko-puffs/housing-studio-plugin)");
    }

    /// <summary>Goes up whenever answers come in (or a request fails), so a list built from them is built again.</summary>
    public int Version => Volatile.Read(ref version);

    /// <summary>Whether a request is out.</summary>
    public bool Busy => Volatile.Read(ref busy) > 0;

    /// <summary>Why the last request failed, or null when it didn't.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Asks about the items that have no fresh price (and weren't just asked), in the background. Cheap when
    /// there's nothing to ask, so it can be called whenever the list is built.
    /// </summary>
    public void Request(string scope, IEnumerable<uint> items)
    {
        if (stop.IsCancellationRequested || scope.Length == 0) return;
        var now = DateTime.UtcNow;
        var ids = items.Distinct().Where(id =>
                !(quotes.TryGetValue((scope, id), out var q) && now - q.FetchedAt < Fresh)
                && !(asked.TryGetValue((scope, id), out var at) && now - at < RetryAfter))
            .ToList();
        if (ids.Count == 0) return;
        foreach (var id in ids) asked[(scope, id)] = now;
        Interlocked.Increment(ref busy);
        Task.Run(async () =>
        {
            try
            {
                foreach (var batch in Universalis.Batches(ids))
                {
                    if (stop.IsCancellationRequested) return;
                    await Fetch(scope, batch).ConfigureAwait(false);
                }
            }
            finally
            {
                Interlocked.Decrement(ref busy);
                Interlocked.Increment(ref version);
            }
        });
    }

    private async Task Fetch(string scope, List<uint> batch)
    {
        try
        {
            var url = Universalis.Url(scope, batch);
            using var response = await http.GetAsync(url, stop.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                LastError = $"Universalis answered {(int)response.StatusCode}";
                Services.Log.Debug($"Market prices: {url} → {(int)response.StatusCode}");
                return;
            }
            var json = await response.Content.ReadAsStringAsync(stop.Token).ConfigureAwait(false);
            var got = Universalis.Parse(json, scope, DateTime.UtcNow);
            foreach (var (id, q) in got) quotes[(scope, id)] = q;
            // Items Universalis doesn't know: an empty quote, so they aren't asked again for a while.
            foreach (var id in batch.Where(id => !got.ContainsKey(id)))
                quotes[(scope, id)] = new MarketQuote(id, scope, DateTime.UtcNow, Array.Empty<MarketListing>());
            LastError = null;
            Interlocked.Increment(ref version);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // A timeout, no network, a bad answer: no prices this time.
            LastError = ex is TaskCanceledException ? "Universalis didn't answer in time" : "Couldn't reach Universalis";
            Services.Log.Debug(ex, "Market prices request failed");
        }
    }

    /// <summary>The prices known for these items (an hour old at most).</summary>
    public Dictionary<uint, MarketQuote> Quotes(string scope, IEnumerable<uint> items)
    {
        var now = DateTime.UtcNow;
        var found = new Dictionary<uint, MarketQuote>();
        foreach (var id in items)
            if (quotes.TryGetValue((scope, id), out var q) && now - q.FetchedAt < Shown)
                found[id] = q;
        return found;
    }

    /// <summary>Whether any of these items is still waiting for its first price.</summary>
    public bool Waiting(string scope, IEnumerable<uint> items) =>
        Busy && items.Any(id => !quotes.ContainsKey((scope, id)));

    public void Dispose()
    {
        stop.Cancel();
        http.Dispose();
    }
}
