using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace RaspberryPi.Logger
{
    // Simple file logger for Raspberry Pi
    public class FileLogger : ILogger
    {
        private enum TraceLevel
        {
            Info,
            Warning, 
            Error
        }

        private string _logFile, _logFileDirectory;

        private static readonly string AssemblyPath = Assembly.GetEntryAssembly().Location;
        private static readonly string WorkingDirectory = Path.GetDirectoryName(AssemblyPath);

        /// <summary>
        /// Self explanatory, consolidates console output and logging into one statement
        /// </summary>
        public bool ConsoleOutput { get; set; }

        /// <summary>
        /// Splits off a log into a new file daily, with the date appended to the file name.
        /// So if LogFile is set as "log.txt", output file will be "log_YYYYMMDD.txt" 
        /// </summary>
        public bool CreateNewLogFileDaily { get; set; }

        /// <summary>
        /// Specifies the log file, either the full path or just the file name.
        /// If no path specified here or in LogDirectory, the current working directory will be used.
        /// If not set, the name of the executing assembly(.txt) will be used.
        /// </summary>
        public string LogFile
        {
            get
            {
                if (string.IsNullOrEmpty(_logFile))
                {
                    var logFileName = Path.GetFileNameWithoutExtension(AssemblyPath) + ".txt";
                    _logFile = Path.Combine(LogDirectory, logFileName);
                }

                if (CreateNewLogFileDaily)
                {
                    return Path.Combine(Path.GetDirectoryName(_logFile), string.Format("{0}_{1}{2}",
                        Path.GetFileNameWithoutExtension(_logFile), DateTime.Now.ToString("yyyyMMdd"),
                        Path.GetExtension(_logFile)));
                }
                return _logFile;
            }
            set
            {
                if (!string.IsNullOrEmpty(value))
                {
                    LogDirectory = Path.GetDirectoryName(value);
                    _logFile = Path.Combine(LogDirectory, Path.GetFileName(value));
                }
            }
        }

        /// <summary>
        /// Specifies the log file directory.
        /// Relative path up to one level deeper than working directory is supported (i.e. "./logs" or just "logs"),
        /// anything else requires the full path to be set explicitly.
        /// If not set, the current working directory will be used.
        /// </summary>
        public string LogDirectory
        {
            get
            {
                return !string.IsNullOrEmpty(_logFileDirectory) ? _logFileDirectory : WorkingDirectory;
            }
            set
            {
                if (!string.IsNullOrEmpty(value))
                {
                    if (value.StartsWith(".\\") || value.StartsWith("./"))
                        _logFileDirectory = Path.Combine(WorkingDirectory, value.Substring(2));
                    else if (!value.StartsWith("\\") && !value.StartsWith("/"))
                        _logFileDirectory = Path.Combine(WorkingDirectory, value);
                    else
                        _logFileDirectory = value;
                }
            }
        }

        public void Info(Type source, string format, params object[] args)
        {
            Info(source, string.Format(format, args));
        }

        public void Info(Type source, string message)
        {
            Info(string.Format("{0}: {1}", source, message));
        }

        public void Info(string format, params object[] args)
        {
            Info(string.Format(format, args));
        }

        public void Info(string message)
        {
            Log(message, TraceLevel.Info);
        }

        public void Warning(Type source, string format, params object[] args)
        {
            Warning(source, string.Format(format, args));
        }

        public void Warning(Type source, string message)
        {
            Warning(string.Format("{0}: {1}", source, message));
        }

        public void Warning(string format, params object[] args)
        {
            Warning(string.Format(format, args));
        }

        public void Warning(string message)
        {
            Log(message, TraceLevel.Warning);
        }

        public void Error(Type source, string format, params object[] args)
        {
            Error(source, string.Format(format, args));
        }

        public void Error(Type source, string message)
        {
            Error(string.Format("{0}: {1}", source, message));
        }

        public void Error(string format, params object[] args)
        {
            Error(string.Format(format, args));
        }

        public void Error(string message)
        {
            Log(message, TraceLevel.Error);
        }

        private void Log(string message, TraceLevel level)
        {
            if (level == TraceLevel.Warning)
                Trace.TraceWarning(message);
            else if (level == TraceLevel.Error )
                Trace.TraceError(message);
            else 
                Trace.TraceInformation(message);
            
            if (ConsoleOutput)
                Console.WriteLine(message);

            if (!Directory.Exists(LogDirectory))
                Directory.CreateDirectory(LogDirectory);

            var logEntry = string.Format("[{0} {1}][{2}]: {3}\r\n",
                DateTime.Now.ToString("yyyy-MM-dd"),
                DateTime.Now.ToString("hh:mm:sstt"), 
                level.ToString().ToUpper(), message);

            File.AppendAllText(LogFile, logEntry);
        }
    }
}
