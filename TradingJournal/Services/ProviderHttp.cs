using System.Net;

namespace TradingJournal.Services;

public static class ProviderHttp
{
    // Bounded retries for temporary provider failures; never retry caller cancellation.
    public static async Task<string> GetStringAsync(HttpClient client, string url, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            TimeSpan delay = TimeSpan.FromSeconds(1 << attempt);
            try
            {
                using var response = await client.GetAsync(url, ct);
                var transient = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout
                    || (int)response.StatusCode >= 500;
                if (!transient || attempt >= 2)
                {
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync(ct);
                }
                var retry = response.Headers.RetryAfter;
                delay = retry?.Delta ?? (retry?.Date - DateTimeOffset.UtcNow) ?? delay;
                // Do not retry sooner than a provider's requested cooldown.
                if (delay > TimeSpan.FromSeconds(30)) response.EnsureSuccessStatusCode();
                if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
            }
            catch (HttpRequestException ex) when (attempt < 2 && ex.StatusCode == null && !ct.IsCancellationRequested) { }
            catch (OperationCanceledException) when (attempt < 2 && !ct.IsCancellationRequested) { }
            await Task.Delay(delay, ct);
        }
    }
}
