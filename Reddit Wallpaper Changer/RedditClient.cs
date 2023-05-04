using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Reddit_Wallpaper_Changer
{
    public class RedditClient
    {
        private const string ClientId = "tpJEWmy3zJAu972XVegMBQ";
        private const string AccessTokenUrl = "https://www.reddit.com/api/v1/access_token";
        private const string GrantType = "https://oauth.reddit.com/grants/installed_client";

        private const int RateLimitNumRequests = 60;

        private static readonly TimeSpan RateLimitTimeSpan = TimeSpan.FromMinutes(1);

        private static readonly string UserAgent = $"(windows:Reddit Wallpaper Changer:v{typeof(Program).Assembly.GetName().Version} (by /u/qwertydog))";

        private static readonly Guid DeviceId = Guid.NewGuid();

        private static readonly SemaphoreSlim _rateLimitLock = new SemaphoreSlim(RateLimitNumRequests, RateLimitNumRequests);
        private static readonly SemaphoreSlim _loginLock = new SemaphoreSlim(1, 1);

        private static AuthenticationHeaderValue _authenticationHeaderValue;

        private static bool _initialised;

        public async Task<string> GetJsonAsync(string url, CancellationToken token)
        {
            await InitialLoginAsync(token).ConfigureAwait(false);

            using (var httpClient = CreateHttpClient())
            {
                var response = await ExecuteWithRateLimit(() => httpClient.GetAsync(url), token).ConfigureAwait(false);

                switch (response.StatusCode)
                {
                    case HttpStatusCode.OK:
                        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    case HttpStatusCode.Unauthorized:
                        await LoginAsync(token).ConfigureAwait(false);
                        return await ExecuteWithRateLimit(() => httpClient.GetStringAsync(url), token).ConfigureAwait(false);
                    default:
                        response.EnsureSuccessStatusCode();
                        return null;
                }
            }
        }

        private async Task<T> ExecuteWithRateLimit<T>(Func<Task<T>> func, CancellationToken token)
        {
            await _rateLimitLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                return await func().ConfigureAwait(false);
            }
            finally
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(RateLimitTimeSpan, token).ConfigureAwait(false);
                    _rateLimitLock.Release();
                });
            }
        }

        private static async Task InitialLoginAsync(CancellationToken token)
        {
            if (!_initialised)
            {
                await _loginLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    if (!_initialised)
                    {
                        await RefreshTokenAsync(token).ConfigureAwait(false);

                        _initialised = true;
                    }
                }
                finally { _loginLock.Release(); }
            }
        }

        private static async Task LoginAsync(CancellationToken token)
        {
            await _loginLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await RefreshTokenAsync(token).ConfigureAwait(false);
            }
            finally { _loginLock.Release(); }
        }

        private static async Task RefreshTokenAsync(CancellationToken token)
        {
            var postData = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", GrantType),
                new KeyValuePair<string, string>("device_id", DeviceId.ToString())
            });

            var authorizationValue = Convert.ToBase64String(Encoding.UTF8.GetBytes(ClientId + ":"));
            _authenticationHeaderValue = new AuthenticationHeaderValue("Basic", authorizationValue);

            string responseContent;

            using (var httpClient = CreateHttpClient())
            {
                var response = await httpClient.PostAsync(AccessTokenUrl, postData, token).ConfigureAwait(false);

                response.EnsureSuccessStatusCode();

                responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }

            var jObject = JObject.Parse(responseContent);

            var accessToken = jObject["access_token"].ToString();
            var tokenType = jObject["token_type"].ToString();

            _authenticationHeaderValue = new AuthenticationHeaderValue(tokenType, accessToken);
        }

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                Proxy = HelperMethods.CreateProxy()
            };

            var httpClient = new HttpClient(handler);

            httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
            httpClient.DefaultRequestHeaders.Authorization = _authenticationHeaderValue;

            return httpClient;
        }
    }
}
