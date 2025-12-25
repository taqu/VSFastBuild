using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using SharpCompress.Common.Rar;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Documents;

namespace VSFastBuildCommon
{
    public class TLogTracker : IDisposable
    {
        public class CommandLineParser
        {
            [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
            private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine, out int pNumArgs);

            [DllImport("kernel32.dll")]
            private static extern IntPtr LocalFree(IntPtr hMem);

            public enum CommandType
            {
                Cmd,
                PowerShell,
                Cl,
                Link,
                Lib_Link,
                Lib,
                Unknown,
            }

            public struct Command
            {
                public CommandType type_;
                public string name_;
                public string input_;
                public string output_;
                public string options_;
            }

            public static string[] Parse(string text)
            {
                IntPtr argsPtr = CommandLineToArgvW(text, out var argsNum);
                if (IntPtr.Zero == argsPtr)
                {
                    return null;
                }
                try
                {
                    return Enumerable
                      .Range(0, argsNum)
                      .Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argsPtr, i * IntPtr.Size)))
                      .ToArray();
                }
                finally
                {
                    LocalFree(argsPtr);
                }
            }

            public Command GetCommand(string text)
            {
                string[] args = Parse(text);
                Command command = new Command()
                {
                    type_ = CommandType.Unknown,
                    name_ = string.Empty,
                    input_ = string.Empty,
                    output_ = string.Empty,
                    options_ = string.Empty,
                };
                if (null == args)
                {
                    return command;
                }
                if (args.Length < 1)
                {
                    return command;
                }
                optionBuilder_.Clear();
                command.name_ = args[0].Trim('"');
                for (int i = 1; i < args.Length; ++i)
                {
                    System.Diagnostics.Debug.Assert(0 < args[i].Length);
                    if (args[i].StartsWith("/Fo"))
                    {
                        if (args[i] == "/Fo")
                        {
                            if ((i + 1) < args.Length)
                            {
                                command.output_ = args[i + 1].Trim('"');
                            }
                        }
                        else
                        {
                            command.output_ = args[i].Substring(3).Trim('"');
                        }
                    }
                    else if (args[i].StartsWith("/OUT:"))
                    {
                        if (args[i] == "/OUT:")
                        {
                            if ((i + 1) < args.Length)
                            {
                                command.output_ = args[i + 1].Trim('"');
                            }
                        }
                        else
                        {
                            command.output_ = args[i].Substring(4).Trim('"');
                        }
                    }
                    else if (args[i].StartsWith("/") || args[i].StartsWith("-"))
                    {
                        if (0 < optionBuilder_.Length)
                        {
                            optionBuilder_.Append(' ');
                        }
                        optionBuilder_.Append(args[i]);
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(command.input_))
                        {
                            command.input_ = args[i];
                        }
                    }
                }
                command.options_ = optionBuilder_.ToString();
                string name = System.IO.Path.GetFileNameWithoutExtension(command.name_).ToLowerInvariant();
                switch (name)
                {
                    case "cmd":
                        command.type_ = CommandType.Cmd;
                        break;
                    case "powershell":
                        command.type_ = CommandType.PowerShell;
                        break;
                    case "cl":
                        command.type_ = CommandType.Cl;
                        break;
                    case "link":
                        command.type_ = CommandType.Link;
                        break;
                    case "lib":
                        command.type_ = CommandType.Lib;
                        break;
                    default:
                        command.type_ = CommandType.Unknown;
                        break;
                }
                return command;
            }
            private StringBuilder optionBuilder_ = new StringBuilder();
        }

        private static Regex FBuildTLogRead = new Regex(@"^FBuild\.\d+\.\d+\.read\.\d+\.tlog");
        private static Regex FBuildTLogWrite = new Regex(@"^FBuild\.\d+\.\d+\.write\.\d+\.tlog");

        private static Regex TLogRead = new Regex(@"^FBuild\.\d+\.\d+-([a-zA-Z0-9\-_]+)\.(\d+\.)?read\.\d+\.tlog");
        private static Regex TLogWrite = new Regex(@"^FBuild\.\d+\.\d+-([a-zA-Z0-9\-_]+)\.(\d+\.)?write\.\d+\.tlog");

        private static Regex LastTLogRead = new Regex(@"^([a-zA-Z0-9\-_]+)\.read\.\d+\.tlog");
        private static Regex LastTLogWrite = new Regex(@"^([a-zA-Z0-9\-_]+)\.write\.\d+\.tlog");

        private class ReadEntry
        {
            public CommandLineParser.Command command_;
            public List<string> files_ = new List<string>(Capacity);
        }
        private class WriteEntry
        {
            public CommandLineParser.Command command_;
            public List<string> inputs_ = new List<string>(Capacity);
            public List<string> outputs_ = new List<string>(Capacity);
        }

        private struct TLogEntry
        {
            public string name_;
            public Dictionary<string, ReadEntry> inputs_;
            public Dictionary<string, WriteEntry> outputs_;
        }
        private string path_ = string.Empty;
        private CommandLineParser commandLineParser_ = new CommandLineParser();
        private Dictionary<string, TLogEntry> logEntries_ = new Dictionary<string, TLogEntry>();
        private const int Capacity = 16;
        private bool disposed_ = false;

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
                    if (null != logEntries_)
                    {
                        logEntries_.Clear();
                        logEntries_ = null;
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
                if (null != match && match.Success)
                {
                    string subroot = match.Groups[1].Value;
                    LoadRead(subroot, path);
                }
                //match = LastTLogWrite.Match(name);
                //if (null != match && match.Success)
                //{
                //    string subroot = match.Groups[1].Value;
                //    LoadWrite(subroot, path);
                //}
            }
        }

        private static bool NeedSaveRead(KeyValuePair<string, TLogEntry> logEntry)
        {
            Dictionary<string, ReadEntry>.ValueCollection dictionary = logEntry.Value.inputs_.Values;
            foreach (ReadEntry entry in dictionary)
            {
                if (0 < entry.files_.Count)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool NeedSaveWrite(KeyValuePair<string, TLogEntry> logEntry)
        {
            Dictionary<string, WriteEntry>.ValueCollection dictionary = logEntry.Value.outputs_.Values;
            foreach (WriteEntry entry in dictionary)
            {
                if (entry.inputs_.Count <= 0)
                {
                    continue;
                }
                if (0 < entry.outputs_.Count)
                {
                    return true;
                }
            }
            return false;
        }


        public void Check()
        {
            logEntries_.Clear();
            if (!System.IO.Directory.Exists(path_))
            {
                return;
            }
            foreach (string path in System.IO.Directory.GetFiles(path_, "*.tlog"))
            {
                string name = System.IO.Path.GetFileName(path);

                Match match;
                match = TLogRead.Match(name);
                if (null != match && match.Success)
                {
                    string subroot = match.Groups[1].Value;
                    AddRead(subroot, path);
                    continue;
                }

                match = TLogWrite.Match(name);
                if (null != match && match.Success)
                {
                    string subroot = match.Groups[1].Value;
                    AddWrite(subroot, path);
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

            // check rsp
            foreach (KeyValuePair<string, TLogEntry> logEntry in logEntries_)
            {
                foreach (WriteEntry writeEntry in logEntry.Value.outputs_.Values)
                {
                    string input = writeEntry.command_.input_.TrimEnd('"').TrimEnd();
                    if (input.EndsWith(".rsp", StringComparison.OrdinalIgnoreCase))
                    {
                        ReadEntry readEntry;
                        if (logEntry.Value.inputs_.TryGetValue(writeEntry.command_.input_.ToUpperInvariant(), out readEntry)){
                            for(int i=0; i<readEntry.files_.Count;)
                            {
                                if (readEntry.files_[i].EndsWith("RSP", StringComparison.OrdinalIgnoreCase))
                                {
                                    readEntry.files_.RemoveAt(i);
                                }
                                else
                                {
                                    writeEntry.inputs_.Add(readEntry.files_[i]);
                                    ++i;
                                }
                            }
                        }
                    }
                }
            }
            List<string> inputList = new List<string>(64);
            StringBuilder stringBuilder = new StringBuilder(1024);
            foreach (KeyValuePair<string, TLogEntry> logEntry in logEntries_)
            {
                try
                {
                    string path;
                    if (NeedSaveRead(logEntry))
                    {
                        path = System.IO.Path.Combine(path_, $"{logEntry.Key}.read.1.tlog");
                        using (FileStream fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                        using (StreamWriter streamWriter = new StreamWriter(fileStream, Encoding.Unicode))
                        {
                            Dictionary<string, ReadEntry>.ValueCollection dictionary = logEntry.Value.inputs_.Values;
                            foreach (KeyValuePair<string, ReadEntry> entry in logEntry.Value.inputs_)
                            {
                                streamWriter.Write('^');
                                string input = entry.Key.TrimEnd('"').TrimEnd();
                                if(input.EndsWith(".rsp", StringComparison.OrdinalIgnoreCase))
                                {
                                    inputList.Clear();
                                    for(int i=0; i< entry.Value.files_.Count; ++i)
                                    {
                                        if(entry.Value.files_[i].EndsWith(".RSP", StringComparison.OrdinalIgnoreCase))
                                        {
                                            continue;
                                        }
                                        inputList.Add(entry.Value.files_[i]);
                                    }
                                    string inputs = string.Join("|", inputList);
                                    streamWriter.WriteLine(inputs);
                                }
                                else
                                {
                                    streamWriter.WriteLine(entry.Key);
                                }
                                foreach (string l in entry.Value.files_)
                                {
                                    if (l.EndsWith(".RSP", StringComparison.OrdinalIgnoreCase))
                                    {
                                        continue;
                                    }
                                    streamWriter.WriteLine(l);
                                }
                            }
                        }
                    }

                    if (NeedSaveWrite(logEntry))
                    {
                        path = System.IO.Path.Combine(path_, $"{logEntry.Key}.write.1.tlog");
                        using (FileStream fileStream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None))
                        using (StreamWriter streamWriter = new StreamWriter(fileStream, Encoding.Unicode))
                        {
                            foreach (KeyValuePair<string, WriteEntry> entry in logEntry.Value.outputs_)
                            {
                                streamWriter.Write('^');
                                for (int i = 0; i < entry.Value.inputs_.Count; ++i)
                                {
                                    streamWriter.Write(entry.Value.inputs_[i]);
                                    if (i != (entry.Value.inputs_.Count - 1))
                                    {
                                        streamWriter.Write("|");
                                    }
                                }
                                streamWriter.WriteLine();
                                for (int i = 0; i < entry.Value.outputs_.Count; ++i)
                                {
                                    streamWriter.WriteLine(entry.Value.outputs_[i]);
                                }
                            }
                        }
                    }

                    #if false
                    {//commands
                        path = System.IO.Path.Combine(path_, $"{logEntry.Key}.command.1.tlog");
                        using (FileStream fileStream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None))
                        using (StreamWriter streamWriter = new StreamWriter(fileStream, Encoding.Unicode))
                        {
                            foreach (KeyValuePair<string, WriteEntry> entry in logEntry.Value.inputs_)
                            {
                                if(string.is)
                                streamWriter.Write('^');
                                streamWriter.WriteLine(entry.Value.command_.input_);
                                streamWriter.WriteLine(entry.Value.command_.options_);
                                for (int i = 0; i < entry.Value.outputs_.Count; ++i)
                                {
                                    streamWriter.WriteLine(entry.Value.outputs_[i]);
                                }
                            }
                        }
                    }
                    #endif
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
                //System.IO.File.Delete(path);
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
                logEntry.inputs_ = new Dictionary<string, ReadEntry>(Capacity);
                logEntry.outputs_ = new Dictionary<string, WriteEntry>(Capacity);
                logEntries_.Add(name, logEntry);
            }
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                using (StreamReader streamReader = new StreamReader(stream))
                {
                    ReadEntry readEntry = null;
                    string line;
                    while (null != (line = streamReader.ReadLine()))
                    {
                        if (string.IsNullOrEmpty(line))
                        {
                            continue;
                        }
                        if (line.StartsWith("#Command: "))
                        {
                            line = line.Substring("#Command: ".Length);
                            CommandLineParser.Command command = commandLineParser_.GetCommand(line);
                            if (string.IsNullOrEmpty(command.input_))
                            {
                                break;
                            }
                            string key = command.input_.ToUpperInvariant();
                            if (!logEntry.inputs_.TryGetValue(key, out readEntry))
                            {
                                readEntry = new ReadEntry();
                                readEntry.command_ = command;
                                logEntry.inputs_.Add(command.input_, readEntry);
                            }
                        }
                        else if(null != readEntry)
                        {
                            if (readEntry.files_.FindIndex((string x0) => x0 == line) < 0)
                            {
                                readEntry.files_.Add(line);
                            }
                        }
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
                logEntry.inputs_ = new Dictionary<string, ReadEntry>(Capacity);
                logEntry.outputs_ = new Dictionary<string, WriteEntry>(Capacity);
                logEntries_.Add(name, logEntry);
            }
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                using (StreamReader streamReader = new StreamReader(stream))
                {
                    string line;
                    ReadEntry readEntry = null;
                    while (null != (line = streamReader.ReadLine()))
                    {
                        if (string.IsNullOrEmpty(line))
                        {
                            continue;
                        }
                        if (line.StartsWith("^"))
                        {
                            line = line.Substring(1);
                            if (!logEntry.inputs_.TryGetValue(line, out readEntry))
                            {
                                readEntry = new ReadEntry();
                                readEntry.files_ = new List<string>(Capacity);
                                logEntry.inputs_.Add(line, readEntry);
                            }
                        }   
                        else if (null != readEntry)
                        {
                            readEntry.files_.Add(line);
                        }
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
                logEntry.inputs_ = new Dictionary<string, ReadEntry>(Capacity);
                logEntry.outputs_ = new Dictionary<string, WriteEntry>(Capacity);
                logEntries_.Add(name, logEntry);
            }
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                using (StreamReader streamReader = new StreamReader(stream))
                {
                    string line;
                    WriteEntry writeEntry = null;
                    while (null != (line = streamReader.ReadLine()))
                    {
                        if (string.IsNullOrEmpty(line))
                        {
                            continue;
                        }
                        if (line.StartsWith("#Command: "))
                        {
                            line = line.Substring("#Command: ".Length);
                            CommandLineParser.Command command = commandLineParser_.GetCommand(line);
                            if (string.IsNullOrEmpty(command.input_))
                            {
                                continue;
                            }
                            string key = string.IsNullOrEmpty(command.options_)? command.input_.ToUpperInvariant() : command.options_.ToUpperInvariant();

                            if (!logEntry.outputs_.TryGetValue(key, out writeEntry))
                            {
                                writeEntry = new WriteEntry();
                                writeEntry.command_ = command;
                                logEntry.outputs_.Add(key, writeEntry);
                            }
                            string input = command.input_.TrimEnd('"').TrimEnd();
                            if (input.EndsWith(".rsp", StringComparison.OrdinalIgnoreCase))
                            {
                            }
                            else
                            {
                                if (!string.IsNullOrEmpty(command.input_))
                                {
                                    writeEntry.inputs_.Add(command.input_.ToUpperInvariant());
                                }
                                if (!string.IsNullOrEmpty(command.output_))
                                {
                                    writeEntry.outputs_.Add(command.output_.ToUpperInvariant());
                                }
                            }
                        }
                        else if(null != writeEntry)
                        {
                            if (writeEntry.outputs_.FindIndex((string x0) => x0 == line) < 0)
                            {
                                writeEntry.outputs_.Add(line);
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
        }

#if false
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
#endif
    }
}
