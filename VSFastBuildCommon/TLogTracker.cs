using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Documents;

namespace VSFastBuildCommon
{
#if false
    public class TLogTracker : IDisposable
    {
        private static Regex FBuildTLogRead = new Regex(@"^FBuild\.\d{5}\.\d{5}\.read\.\d+\.tlog");
        private static Regex FBuildTLogWrite = new Regex(@"^FBuild\.\d{5}\.\d{5}\.write\.\d+\.tlog");

        private static Regex TLogRead = new Regex(@"^FBuild\.\d{5}\.\d{5}-([a-zA-Z0-9\-_]+)\.(\d+\.)?read\.\d+\.tlog");
        private static Regex TLogWrite = new Regex(@"^FBuild\.\d{5}\.\d{5}-([a-zA-Z0-9\-_]+)\.(\d+\.)?write\.\d+\.tlog");

        private const int TryCount = 64;
        private const int WaitTime = 134;

        private const int TryDeleteCount = 128;
        private const int WaitDeleteTime = 268;

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
        private readonly AutoResetEvent autoResetEvent_ = new AutoResetEvent(false);
        private Queue<Tuple<string, string>> paths_ = new Queue<Tuple<string, string>>(16);

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

        public void Run(CancellationToken cancellationToken)
        {
            Start();
            for (; ; )
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                autoResetEvent_.WaitOne(WaitTime);

                Tuple<string, string>[] paths = null;
                lock (paths_)
                {
                    if(0 < paths_.Count) {
                        paths = paths_.ToArray();
                        paths_.Clear();
                    }
                }
                if(null == paths)
                {
                    continue;
                }
                foreach (Tuple<string, string> path in paths)
                {
                    Match match;
                    match = TLogRead.Match(path.Item1);
                    if (null != match && match.Success)
                    {
                        string subroot = match.Groups[1].Value;
                        AddRead(subroot, path.Item2);
                        TryDelete(path.Item2);
                        continue;
                    }

                    match = TLogWrite.Match(path.Item1);
                    if (null != match && match.Success)
                    {
                        string subroot = match.Groups[1].Value;
                        AddWrite(subroot, path.Item2);
                        TryDelete(path.Item2);
                        continue;
                    }
                    //match = FBuildTLogRead.Match(path.Item1);
                    //if (null != match && match.Success)
                    //{
                    //    TryDelete(path.Item2);
                    //    continue;
                    //}

                    //match = FBuildTLogWrite.Match(path.Item1);
                    //if (null != match && match.Success)
                    //{
                    //    TryDelete(path.Item2);
                    //    continue;
                    //}
                    TryDelete(path.Item2);
                }
            }
            Stop();
            Save();
        }

        private void Start()
        {
            watcher_.EnableRaisingEvents = true;
        }

        private void Stop()
        {
            watcher_.EnableRaisingEvents = false;
        }

        private void Save()
        {
            foreach(KeyValuePair<string, TLogEntry> logEntry in logEntries_)
            {
                try
                {
                    string path;
                    path = System.IO.Path.Combine(path_, $"{logEntry.Key}.read.1.tlog");
                    System.IO.File.WriteAllText(path, logEntry.Value.inputs_.ToString());
                    path = System.IO.Path.Combine(path_, $"{logEntry.Key}.write.1.tlog");
                    System.IO.File.WriteAllText(path, logEntry.Value.outputs_.ToString());
                }
                catch
                {
                }
            }
        }

        private void TryDelete(string path)
        {
            for (int i = 0; i < TryDeleteCount; ++i)
            {
                try
                {
                    System.IO.File.Delete(path);
                    break;
                }
                catch (Exception e)
                {
                    Thread.Sleep(WaitDeleteTime);
                }
            }
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Name) || string.IsNullOrEmpty(e.FullPath))
            {
                return;
            }

            lock(paths_)
            {
                paths_.Enqueue(new Tuple<string, string>(e.Name, e.FullPath));
                autoResetEvent_.Set();
            }
        }

        private void AddRead(string name, string path)
        {
            TLogEntry logEntry;
            if (!logEntries_.TryGetValue(name, out logEntry))
            {
                logEntry.name_ = name;
                logEntry.inputs_ = new StringBuilder(256);
                logEntry.outputs_ = new StringBuilder(256);
                logEntries_.Add(name, logEntry);
            }
            for (int i = 0; i < TryCount; ++i)
            {
                try
                {
                    using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    using (StreamReader streamReader = new StreamReader(stream))
                    {
                        string line;
                        while (null != (line = streamReader.ReadLine()))
                        {
                            if (string.IsNullOrEmpty(line))
                            {
                                continue;
                            }
                            if (line.StartsWith("#"))
                            {
                                continue;
                            }
                            logEntry.inputs_.AppendLine(line);
                        }
                        break;
                    }
                }
                catch (Exception)
                {
                    Thread.Sleep(WaitTime);
                }
            }
        }

        private void AddWrite(string name, string path)
        {
            TLogEntry logEntry;
            if (!logEntries_.TryGetValue(name, out logEntry))
            {
                logEntry.name_ = name;
                logEntry.inputs_ = new StringBuilder(256);
                logEntry.outputs_ = new StringBuilder(256);
                logEntries_.Add(name, logEntry);
            }
            for (int i = 0; i < TryCount; ++i)
            {
                try
                {
                    using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                    using (StreamReader streamReader = new StreamReader(stream))
                    {
                        string line;
                        while (null != (line = streamReader.ReadLine()))
                        {
                            if (string.IsNullOrEmpty(line))
                            {
                                continue;
                            }
                            if (line.StartsWith("#"))
                            {
                                continue;
                            }
                            logEntry.outputs_.AppendLine(line);
                        }
                        break;
                    }
                }
                catch (Exception)
                {
                    Thread.Sleep(WaitTime);
                }
            }
        }
    }
#else
    public class TLogTracker : IDisposable
    {
        private static Regex FBuildTLogRead = new Regex(@"^FBuild\.\d{5}\.\d{5}\.read\.\d+\.tlog");
        private static Regex FBuildTLogWrite = new Regex(@"^FBuild\.\d{5}\.\d{5}\.write\.\d+\.tlog");

        private static Regex TLogRead = new Regex(@"^FBuild\.\d{5}\.\d{5}-([a-zA-Z0-9\-_]+)\.(\d+\.)?read\.\d+\.tlog");
        private static Regex TLogWrite = new Regex(@"^FBuild\.\d{5}\.\d{5}-([a-zA-Z0-9\-_]+)\.(\d+\.)?write\.\d+\.tlog");

        private static Regex LastTLogRead = new Regex(@"^([a-zA-Z0-9\-_]+)\.read\.\d+\.tlog");
        private static Regex LastTLogWrite = new Regex(@"^([a-zA-Z0-9\-_]+)\.write\.\d+\.tlog");

        private const int TryCount = 64;
        private const int WaitTime = 134;

        private const int TryDeleteCount = 128;
        private const int WaitDeleteTime = 268;

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
        private readonly AutoResetEvent autoResetEvent_ = new AutoResetEvent(false);
        private Queue<Tuple<string, string>> paths_ = new Queue<Tuple<string, string>>(16);

        public TLogTracker(string path)
        {
            path_ = path;
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
            logEntries_.Clear();

            //clean and current
            foreach (string path in System.IO.Directory.GetFiles(path_, "*.tlog"))
            {
                string name = System.IO.Path.GetFileName(path);

                Match match;
                match = TLogRead.Match(name);
                if (null != match && match.Success)
                {
                    TryDelete(path);
                    continue;
                }

                match = TLogWrite.Match(name);
                if (null != match && match.Success)
                {
                    TryDelete(path);
                    continue;
                }
                match = FBuildTLogRead.Match(name);
                if (null != match && match.Success)
                {
                    TryDelete(path);
                    continue;
                }

                match = FBuildTLogWrite.Match(name);
                if (null != match && match.Success)
                {
                    TryDelete(path);
                    continue;
                }

                match = LastTLogRead.Match(name);
                if(null != match && match.Success)
                {
                    string subroot = match.Groups[1].Value;
                    LoadRead(subroot, path);
                }
                match = LastTLogWrite.Match(name);
                if (null != match && match.Success)
                {
                    string subroot = match.Groups[1].Value;
                    LoadWrite(subroot, path);
                }
            }
        }

        public void Save()
        {
            foreach (string path in System.IO.Directory.GetFiles(path_, "*.tlog"))
            {
                string name = System.IO.Path.GetFileName(path);

                Match match;
                match = TLogRead.Match(name);
                if (null != match && match.Success)
                {
                    string subroot = match.Groups[1].Value;
                    AddRead(subroot, path);
                    TryDelete(path);
                    continue;
                }

                match = TLogWrite.Match(name);
                if (null != match && match.Success)
                {
                    string subroot = match.Groups[1].Value;
                    AddWrite(subroot, path);
                    TryDelete(path);
                    continue;
                }
                match = FBuildTLogRead.Match(name);
                if (null != match && match.Success)
                {
                    TryDelete(path);
                    continue;
                }

                match = FBuildTLogWrite.Match(name);
                if (null != match && match.Success)
                {
                    TryDelete(path);
                    continue;
                }
                //TryDelete(path);
            }
            foreach (KeyValuePair<string, TLogEntry> logEntry in logEntries_)
            {
                try
                {
                    string path;
                    path = System.IO.Path.Combine(path_, $"{logEntry.Key}.read.1.tlog");
                    System.IO.File.WriteAllText(path, logEntry.Value.inputs_.ToString());
                    path = System.IO.Path.Combine(path_, $"{logEntry.Key}.write.1.tlog");
                    System.IO.File.WriteAllText(path, logEntry.Value.outputs_.ToString());
                }
                catch
                {
                }
            }
        }
        private void TryDelete(string path)
        {
            try
            {
                System.IO.File.Delete(path);
            }
            catch (Exception e)
            {
            }
        }

        private void AddRead(string name, string path)
        {
            TLogEntry logEntry;
            if (!logEntries_.TryGetValue(name, out logEntry))
            {
                logEntry.name_ = name;
                logEntry.inputs_ = new StringBuilder(256);
                logEntry.outputs_ = new StringBuilder(256);
                logEntries_.Add(name, logEntry);
            }
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                using (StreamReader streamReader = new StreamReader(stream))
                {
                    string line;
                    bool first = true;
                    while (null != (line = streamReader.ReadLine()))
                    {
                        if (string.IsNullOrEmpty(line))
                        {
                            continue;
                        }
                        if (line.StartsWith("#"))
                        {
                            continue;
                        }
                        if (first)
                        {
                            logEntry.inputs_.Append("^");
                        }
                        logEntry.inputs_.AppendLine(line);
                        first = false;
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        private void LoadRead(string name, string path)
        {
            TLogEntry logEntry;
            if (!logEntries_.TryGetValue(name, out logEntry))
            {
                logEntry.name_ = name;
                logEntry.inputs_ = new StringBuilder(256);
                logEntry.outputs_ = new StringBuilder(256);
                logEntries_.Add(name, logEntry);
            }
                try
                {
                    using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    using (StreamReader streamReader = new StreamReader(stream))
                    {
                        string line;
                        while (null != (line = streamReader.ReadLine()))
                        {
                        if (string.IsNullOrEmpty(line))
                        {
                            continue;
                        }
                        logEntry.inputs_.AppendLine(line);
                        }
                    }
                }
                catch (Exception)
                {
                }
        }

        private void AddWrite(string name, string path)
        {
            TLogEntry logEntry;
            if (!logEntries_.TryGetValue(name, out logEntry))
            {
                logEntry.name_ = name;
                logEntry.inputs_ = new StringBuilder(256);
                logEntry.outputs_ = new StringBuilder(256);
                logEntries_.Add(name, logEntry);
            }
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                using (StreamReader streamReader = new StreamReader(stream))
                {
                    string line;
                    bool first = true;
                    while (null != (line = streamReader.ReadLine()))
                    {
                        if (string.IsNullOrEmpty(line))
                        {
                            continue;
                        }
                        if (line.StartsWith("#"))
                        {
                            continue;
                        }
                        if (first)
                        {
                            logEntry.outputs_.Append("^");
                        }
                        logEntry.outputs_.AppendLine(line);
                        first = false;
                    }
                }
            }
            catch (Exception)
            {
            }
        }
        private void LoadWrite(string name, string path)
        {
            TLogEntry logEntry;
            if (!logEntries_.TryGetValue(name, out logEntry))
            {
                logEntry.name_ = name;
                logEntry.inputs_ = new StringBuilder(256);
                logEntry.outputs_ = new StringBuilder(256);
                logEntries_.Add(name, logEntry);
            }
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                using (StreamReader streamReader = new StreamReader(stream))
                {
                    string line;
                    while (null != (line = streamReader.ReadLine()))
                    {
                        if (string.IsNullOrEmpty(line))
                        {
                            continue;
                        }
                        logEntry.outputs_.AppendLine(line);
                    }
                }
            }
            catch (Exception)
            {
            }
        }
    }
#endif
}
