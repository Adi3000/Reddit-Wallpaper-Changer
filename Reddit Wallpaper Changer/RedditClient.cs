using Reddit_Wallpaper_Changer.Properties;
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
        private const string DefaultClientId = "tpJEWmy3zJAu972XVegMBQ";
        private const string AccessTokenUrl = "https://www.reddit.com/api/v1/access_token";
        private const string GrantType = "https://oauth.reddit.com/grants/installed_client";

        private const int RateLimitNumRequests = 60;

        private static readonly TimeSpan RateLimitTimeSpan = TimeSpan.FromMinutes(1);

        private static readonly string UserAgent = $"(windows/Reddit Wallpaper Changer/v{typeof(Program).Assembly.GetName().Version} (by /u/qwertydog))";

        private static readonly Guid DeviceId = Guid.NewGuid();

        private static readonly SemaphoreSlim _rateLimitLock = new SemaphoreSlim(RateLimitNumRequests, RateLimitNumRequests);
        private static readonly SemaphoreSlim _loginLock = new SemaphoreSlim(1, 1);

        private static string _authenticationHeaderValue = "";

        private static bool _initialised;

        private static long authenticationExpires = 0;

        private static HttpClient _httpClient = null;

        public async Task<string> GetJsonAsync(string url, CancellationToken token)
        {
            await InitialLoginAsync(token).ConfigureAwait(false);

            var httpClient = GetHttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", $"Bearer {_authenticationHeaderValue}");
            var response = await ExecuteWithRateLimit(() => httpClient.SendAsync(request), token).ConfigureAwait(false);
            response.WriteRequestToLogger();
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                case HttpStatusCode.Unauthorized:
                case HttpStatusCode.Forbidden:
                    var error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Logger.Instance.LogMessageToFile($"Received error response {error}", LogLevel.Error);
                    await LoginAsync(token).ConfigureAwait(false);
                    var failbackHttpClient = CreateHttpClient();
                    failbackHttpClient.DefaultRequestHeaders.Authorization =  new AuthenticationHeaderValue("Bearer", _authenticationHeaderValue);
                    return await ExecuteWithRateLimit(() => failbackHttpClient.GetStringAsync(url), token).ConfigureAwait(false);
                default:
                    response.EnsureSuccessStatusCode();
                    return null;
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
            if(DateTimeOffset.Now.ToUnixTimeSeconds() < authenticationExpires){
                return;
            }
            Logger.Instance.LogMessageToFile("Refreshing Reddit token", LogLevel.Information);
            var clientId = Settings.Default.redditClientId;
            var clientSecret = Settings.Default.redditClientSecret;
            var password = Settings.Default.redditPassword;
            var user = Settings.Default.redditUser;
            var grantTypeUsed = "client_credentials";
            var queryParams = new[] {
                new KeyValuePair<string, string>("grant_type", grantTypeUsed),
                new KeyValuePair<string, string>("username", user),
                new KeyValuePair<string, string>("password", password)
            };

            if(string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password)){
                Logger.Instance.LogMessageToFile("Using default query param for grant", LogLevel.Information);
                queryParams = new[] {
                    new KeyValuePair<string, string>("grant_type", GrantType),
                    new KeyValuePair<string, string>("device_id", DeviceId.ToString())
                };
            }

            var postData = new FormUrlEncodedContent(queryParams);

            if(string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret)){
                clientId = DefaultClientId;
                clientSecret = "";
                Logger.Instance.LogMessageToFile("No client id or client secret, using default clientId", LogLevel.Information);
            }

            var authorizationValue = Convert.ToBase64String(Encoding.UTF8.GetBytes(clientId + ":" + clientSecret));
            var basicAuthHeaderValue = new AuthenticationHeaderValue("Basic", authorizationValue);

            string responseContent;

            var httpClient = GetHttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, AccessTokenUrl);
            request.Headers.Authorization = basicAuthHeaderValue;
            request.Content = postData;
            var response = await httpClient.SendAsync(request, token).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            var jObject = JObject.Parse(responseContent);

            var accessToken = jObject["access_token"].ToString();
            var tokenType = jObject["token_type"].ToString();
            authenticationExpires = DateTimeOffset.Now.ToUnixTimeSeconds() + jObject.Value<long>("expires_in");
            Logger.Instance.LogMessageToFile($"Following token will expire at {authenticationExpires}", LogLevel.Information);

            _authenticationHeaderValue = accessToken;
        }

        private static HttpClient GetHttpClient()
        {
            if(_httpClient == null){
                _httpClient = CreateHttpClient();
            }
            return _httpClient;
        }
        private static HttpClient CreateHttpClient()
        {
            var proxy = HelperMethods.CreateProxy();
            HttpClient client = null;
            if(proxy != null){
                var proxyHandler = new HttpClientHandler()
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                    Proxy = HelperMethods.CreateProxy()
                };
                client = new HttpClient(proxyHandler);
            }else{
                client = HttpClientFactory.Create(new LoggingHttpHandler());
            }
            client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            return client;
        }

    }
}
