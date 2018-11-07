using Reddit_Wallpaper_Changer.Forms;
using System.Threading;

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

        public void OpenPopupInfoWindow(RedditLink redditLink)
        {
            if (SynchronizationContext.Current == _uiContext)
                _mainForm.OpenPopupInfoWindow(redditLink);
            else
                _uiContext.Post(x => OpenPopupInfoWindow(redditLink), null);
        }

        public void DisableChangeWallpaperTimer()
        {
            if (SynchronizationContext.Current == _uiContext)
                _mainForm.DisableChangeWallpaperTimer();
            else
                _uiContext.Post(x => DisableChangeWallpaperTimer(), null);
        }

        public void AddImageToHistory(RedditImage image)
        {
            if (SynchronizationContext.Current == _uiContext)
                _mainForm.AddImageToHistory(image);
            else
                _uiContext.Post(x => AddImageToHistory(image), null);
        }

        public void SetCurrentThread(string currentThread)
        {
            if (SynchronizationContext.Current == _uiContext)
                _mainForm.CurrentThread = currentThread;
            else
                _uiContext.Post(x => SetCurrentThread(currentThread), null);
        }

        public void RestartBreakBetweenChangeTimer()
        {
            if (SynchronizationContext.Current == _uiContext)
                _mainForm.RestartBreakBetweenChangeTimer();
            else
                _uiContext.Post(x => RestartBreakBetweenChangeTimer(), null);
        }

        public void RestartChangeWallpaperTimer()
        {
            if (SynchronizationContext.Current == _uiContext)
                _mainForm.RestartChangeWallpaperTimer();
            else
                _uiContext.Post(x => RestartChangeWallpaperTimer(), null);
        }

        public void ShowNoResultsBalloonTip(int retryCount)
        {
            if (SynchronizationContext.Current == _uiContext)
                _mainForm.ShowNoResultsBalloonTip(retryCount);
            else
                _uiContext.Post(x => ShowNoResultsBalloonTip(retryCount), null);
        }

        public void LogFailure(string uiText, string logText, LogLevel logLevel)
        {
            UpdateStatus(uiText);
            Logger.Instance.LogMessageToFile(logText, LogLevel.Warning);
            RestartBreakBetweenChangeTimer();
        }

        public void LogFailure(string uiText, string logText)
            => LogFailure(uiText, logText, LogLevel.Warning);
    }
}
