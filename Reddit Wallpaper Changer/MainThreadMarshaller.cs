using Reddit_Wallpaper_Changer.Forms;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Reddit_Wallpaper_Changer
{
    public class MainThreadMarshaller
    {
        private readonly RWC _mainForm;
        private readonly SynchronizationContext _uiContext;

        public MainThreadMarshaller(RWC mainForm, SynchronizationContext uiContext)
        {
            _mainForm = mainForm;
            _uiContext = uiContext;
        }

        //======================================================================
        // Update status
        //======================================================================
        public void UpdateStatus(string text)
        {
            if (SynchronizationContext.Current == _uiContext)
                _mainForm.UpdateStatus(text);
            else
                _uiContext.Post(x => UpdateStatus(text), null);
        }

        public void SetWallpaperChanged(RedditLink redditLink)
        {
            if (SynchronizationContext.Current == _uiContext)
                _mainForm.SetWallpaperChanged(redditLink);
            else
                _uiContext.Post(x => SetWallpaperChanged(redditLink), null);
        }

        public void RestartChangeWallpaperTimer()
        {
            if (SynchronizationContext.Current == _uiContext)
                _mainForm.RestartChangeWallpaperTimer();
            else
                _uiContext.Post(x => RestartChangeWallpaperTimer(), null);
        }

        internal void DisableChangeTimerNoResults(int maxRetryAttempts)
        {
            if (SynchronizationContext.Current == _uiContext)
                _mainForm.DisableChangeTimerNoResults(maxRetryAttempts);
            else
                _uiContext.Post(x => DisableChangeTimerNoResults(maxRetryAttempts), null);
        }

        public void DisableAndUpdateStatus(string status, string log)
        {
            if (SynchronizationContext.Current == _uiContext)
                _mainForm.DisableAndUpdateStatus(status, log);
            else
                _uiContext.Post(x => DisableAndUpdateStatus(status, log), null);
        }

        public async Task LogFailureAsync(string uiText, string logText, LogLevel logLevel)
        {
            UpdateStatus(uiText);
            Logger.Instance.LogMessageToFile(logText, logLevel);
            await Task.Delay(1500);
            RestartChangeWallpaperTimer();
        }

        public Task LogFailureAsync(string uiText, string logText)
            => LogFailureAsync(uiText, logText, LogLevel.Warning);
    }
}
