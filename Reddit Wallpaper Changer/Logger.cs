using Reddit_Wallpaper_Changer.Properties;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Reddit_Wallpaper_Changer
{
    public class Logger : IDisposable
    {
        private const int MaxLogFileSize = 512000;

        private readonly static Lazy<Logger> _instance = new Lazy<Logger>(() => new Logger());
        public static Logger Instance => _instance.Value;

        private readonly BlockingCollection<Log> _logs = new BlockingCollection<Log>();
        private readonly Thread _logThread;

        private bool _disposed;

        private Logger()
        {
            _logThread = new Thread(BeginLogging) { IsBackground = true };
            _logThread.Start();
        }

        #region IDisposable Support
        
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // dispose managed state (managed objects).

                _logs.Dispose();
            }

            // free unmanaged resources (unmanaged objects) and override a finalizer below.
            // set large fields to null.

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        //======================================================================
        // Write to the logfile
        //======================================================================
        public void LogMessageToFile(string msg, LogLevel logLevel, [CallerMemberName]string callerName = "") 
            => _logs.Add(new Log(DateTime.Now, logLevel, msg, callerName));

        public void Close()
        {
            _logs.CompleteAdding();

            _logThread.Join();

            Dispose();
        }

        private void BeginLogging()
        {
            foreach (var log in _logs.GetConsumingEnumerable())
            {
                WriteLog(log);
            }
        }

        private static void WriteLog(Log log)
        {
            // Legacy: Remove old logs
            if (File.Exists(AppDomain.CurrentDomain.BaseDirectory + @"Log\RWC.log"))
                Directory.Delete(AppDomain.CurrentDomain.BaseDirectory + "Log", true);

            var logfiledir = Settings.Default.AppDataPath + @"\Logs";

            Directory.CreateDirectory(logfiledir);

            var fullLogPath = $@"{logfiledir}\RWC.log";

            if (File.Exists(fullLogPath) && new FileInfo(fullLogPath).Length >= MaxLogFileSize)
                CleanupOldLogFiles(logfiledir);

            try
            {
                var machineName = Environment.MachineName;
                var logLevel = GetLogLevel(log.LogLevel);

                using (var sw = new StreamWriter(fullLogPath, true))
                {
                    sw.WriteLine($"{log.DateTime} - {machineName} - {log.CallerName} - {logLevel} {log.Message}");
                    sw.Flush();
                }
            }
            catch { }
        }

        private static void CleanupOldLogFiles(string logfiledir)
        {
            try
            {
                for (var i = 3; i >= 1; i--)
                {
                    var destinationFilePath = $@"{logfiledir}\RWC{i}.log";

                    if (File.Exists(destinationFilePath))
                        File.Delete(destinationFilePath);

                    var sourceFileSuffix = ((i - 1) > 0) ? (i - 1).ToString() : "";
                    var sourceFilePath = $@"{logfiledir}\RWC{sourceFileSuffix}.log";

                    if (File.Exists(sourceFilePath))
                        File.Move(sourceFilePath, destinationFilePath);                        
                }
            }
            catch { }
        }

        private static string GetLogLevel(LogLevel logLevel)
        {
            switch (logLevel)
            {
                case LogLevel.Information:
                    return "INFORMATION:";
                case LogLevel.Warning:
                    return "WARNING:";
                case LogLevel.Error:
                    return "ERROR:";
                default:
                    return "";
            }
        }

        private class Log
        {
            public DateTime DateTime { get; }
            public LogLevel LogLevel { get; }
            public string Message { get; }
            public string CallerName { get; }

            public Log(DateTime dateTime, LogLevel logLevel, string message, string callerName)
            {
                DateTime = dateTime;
                LogLevel = logLevel;
                Message = message;
                CallerName = callerName;
            }
        }
    }
}
