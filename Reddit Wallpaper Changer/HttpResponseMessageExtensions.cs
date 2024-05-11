using Reddit_Wallpaper_Changer.Properties;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Reddit_Wallpaper_Changer
{

    static class HttpResponseMessageExtensions
    {
        internal static void WriteRequestToLogger(this HttpResponseMessage response)
        {
            if (response is null)
            {
                return;
            }

            var request = response.RequestMessage;
            Logger.Instance.LogMessageToFile($"Intercept from extension the following request {request}", LogLevel.Information);

        }
    }
}