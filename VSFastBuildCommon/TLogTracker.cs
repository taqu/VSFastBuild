using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace VSFastBuildCommon
{
    public class TLogTracker : IDisposable
    {
        private static Regex FBuildTLogRead = new Regex(@"FBuild\.\d{5}\.\d{5}\.read\.\d+\.tlog");
        private static Regex FBuildTLogWrite = new Regex(@"FBuild\.\d{5}\.\d{5}\.write\.\d+\.tlog");

        private static Regex TLogRead = new Regex(@"FBuild\.\d{5}\.\d{5}-(.+)\.\d+\.read\.\d+\.tlog");
        private static Regex TLogWrite = new Regex(@"FBuild\.\d{5}\.\d{5}-(.+)\.\d+\.write\.\d+\.tlog");

        private struct TLogEntry
        {
            public string name_;
            public StringBuilder inputs_;
            public StringBuilder outputs_;
        }
        private string path_ = string.Empty;
        private Dictionary<string, TLogEntry> logEntries_ = new Dictionary<string, TLogEntry>();
        private const int MaxCount = 16;
        private bool disposed_ = false;
        private FileSystemWatcher watcher_ = null;

        public TLogTracker(string path)
        {
            path_ = path;
            watcher_ = new FileSystemWatcher(path);
            watcher_.NotifyFilter = NotifyFilters.Attributes
                                 | NotifyFilters.CreationTime
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.FileName
                                 | NotifyFilters.LastAccess
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.Security
                                 | NotifyFilters.Size;

            watcher_.Created += OnCreated;

            watcher_.Filter = "*.tlog";
            watcher_.IncludeSubdirectories = false;
            watcher_.EnableRaisingEvents = false;
        }

        ~TLogTracker()
        {
            Dispose(false);
        }


        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed_)
            {
                if (disposing)
                {
                    if (null != watcher_)
                    {
                        watcher_.Dispose();
                        watcher_ = null;
                    }
                }
                disposed_ = true;
            }
        }

        public void Start()
        {
            watcher_.EnableRaisingEvents = true;
        }

        public void Stop()
        {
            watcher_.EnableRaisingEvents = false;
        }

        public void Save()
        {
        }

        private void TryDelete(string path)
        {
            try
            {
                System.IO.File.Delete(path);
            }
            catch
            {
            }
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Name))
            {
                return;
            }
            Match match;

            match = FBuildTLogRead.Match(e.Name);
            if (null != match && match.Success)
            {
                try {
                System.IO.File.Delete(e.FullPath);
                }
                catch
                {
                }
            }

            match = FBuildTLogWrite.Match(e.Name);
            if (null != match && match.Success)
            {
                try {
                System.IO.File.Delete(e.FullPath);
                }
                catch
                {
                }
            }

            match = TLogRead.Match(e.Name);
            if (null != match && match.Success)
            {
                string subroot = match.Groups[1].Value;
                Trace.WriteLine(e.Name + " " + subroot);
            }

            match = TLogWrite.Match(e.Name);
            if (null != match && match.Success)
            {
                string subroot = match.Groups[1].Value;
                Trace.WriteLine(e.Name + " " + subroot);
            }
        }

        private void WriteRead(string name, string path)
        {
            TLogEntry logEntry;
            if (!logEntries_.TryGetValue(name, out logEntry))
            {
                logEntry.name_ = name;
                logEntry.inputs_ = new StringBuilder(256);
                logEntry.outputs_ = new StringBuilder(256);
                logEntries_.Add(name, logEntry);
            }
        }
    }
}
