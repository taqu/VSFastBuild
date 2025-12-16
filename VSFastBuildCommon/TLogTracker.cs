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
    internal class TLogTracker : IDisposable
    {
        private Regex TLogRead = new Regex(@"FBuild\.\d{5}\.\d{5}-(.+)\.\d+\.read\.(.*)\.1\.tlog");
        private Regex TLogWrite = new Regex(@"FBuild\.\d{5}\.\d{5}-(.+)\.\d+\.write\.(.*)\.1\.tlog");
        private const int MaxCount = 16;
        private bool disposed_ = false;
        private FileSystemWatcher watcher_ = null;

        public TLogTracker(string path)
        {
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

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Name))
            {
                return;
            }
            Match match;

            match = TLogRead.Match(e.Name);
            if (null != match)
            {
                string subroot = match.Captures[0].Value;
                Trace.WriteLine(e.Name + " " + subroot);
            }

            match = TLogWrite.Match(e.Name);
            if (null != match)
            {
                string subroot = match.Captures[0].Value;
                Trace.WriteLine(e.Name + " " + subroot);
            }
        }
    }
}
