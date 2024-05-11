using Reddit_Wallpaper_Changer.Properties;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Reddit_Wallpaper_Changer
{
    public class LoggingHttpHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Logger.Instance.LogMessageToFile($"Intercept the following request {request}", LogLevel.Information);
            HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
            return response;
        }
    }
}