using System;

namespace RadioDataApp.Services
{
    public static class LogService
    {
        public static event EventHandler<(string Message, string Category)>? OnLog;

        public static void Log(string message, string category = "INFO")
        {
            OnLog?.Invoke(null, (message, category));
        }

        public static void Debug(string message)
        {
            Log(message, "DEBUG");
        }

        public static void Error(string message)
        {
            Log(message, "ERROR");
        }
    }
}
