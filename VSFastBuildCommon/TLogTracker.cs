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

        private static Regex FBuildTLogRead = new Regex(@"^FBuild\.\d{5}\.\d{5}\.read\.\d+\.tlog");
        private static Regex FBuildTLogWrite = new Regex(@"^FBuild\.\d{5}\.\d{5}\.write\.\d+\.tlog");

        private static Regex TLogRead = new Regex(@"^FBuild\.\d{5}\.\d{5}-([a-zA-Z0-9\-_]+)\.(\d+\.)?read\.\d+\.tlog");
        private static Regex TLogWrite = new Regex(@"^FBuild\.\d{5}\.\d{5}-([a-zA-Z0-9\-_]+)\.(\d+\.)?write\.\d+\.tlog");

        private static Regex LastTLogRead = new Regex(@"^([a-zA-Z0-9\-_]+)\.read\.\d+\.tlog");
        private static Regex LastTLogWrite = new Regex(@"^([a-zA-Z0-9\-_]+)\.write\.\d+\.tlog");

        private class ReadEntry
        {
            public List<string> files_;
        }

        private struct WriteEntry
        {
            public string input_;
            public string output_;
        }

        private struct TLogEntry
        {
            public string name_;
            public Dictionary<string, ReadEntry> inputs_;
            public Dictionary<string, List<WriteEntry>> outputs_;
        }
        private string path_ = string.Empty;
        private CommandLineParser commandLineParser_ = new CommandLineParser();
        private Dictionary<string, TLogEntry> logEntries_ = new Dictionary<string, TLogEntry>();
        private const int MaxCount = 16;
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
            foreach (KeyValuePair<string, ReadEntry> entry in logEntry.Value.inputs_)
            {
                if (0 < entry.Value.files_.Count)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool NeedSaveWrite(KeyValuePair<string, TLogEntry> logEntry)
        {
            Dictionary<string, List<WriteEntry>>.ValueCollection dictionary = logEntry.Value.outputs_.Values;
            foreach (List<WriteEntry> writeEntryList in dictionary)
            {
                if (0 < writeEntryList.Count)
                {
                    return true;
                }
            }
            return false;
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
                foreach (List<WriteEntry> writeEntryList in logEntry.Value.outputs_.Values)
                {
                    for (int i = 0; i < writeEntryList.Count; ++i)
                    {
                        string input = writeEntryList[i].input_.TrimEnd('"').TrimEnd();
                        if (input.EndsWith(".rsp", StringComparison.OrdinalIgnoreCase))
                        {

                        }
                    }
                }
            }
            List<string> inputList = new List<string>();
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
                                        if(!entry.Value.files_[i].EndsWith(".OBJ", StringComparison.OrdinalIgnoreCase))
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
                                streamWriter.WriteLine(entry.Key);
                                foreach (string l in entry.Value.files_)
                                {
                                    if(l.EndsWith(".rsp", StringComparison.OrdinalIgnoreCase)){
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
                            Dictionary<string, List<WriteEntry>>.ValueCollection dictionary = logEntry.Value.outputs_.Values;
                            foreach (List<WriteEntry> writeEntryList in dictionary)
                            {
                                streamWriter.Write('^');
                                for (int i = 0; i < writeEntryList.Count; ++i)
                                {
                                    streamWriter.Write(writeEntryList[i].input_);
                                    if (i != (writeEntryList.Count - 1))
                                    {
                                        streamWriter.Write("|");
                                    }
                                }
                                streamWriter.WriteLine();
                                for (int i = 0; i < writeEntryList.Count; ++i)
                                {
                                    streamWriter.WriteLine(writeEntryList[i].output_);
                                }
                            }
                        }
                    }
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
                logEntry.inputs_ = new Dictionary<string, ReadEntry>(MaxCount);
                logEntry.outputs_ = new Dictionary<string, List<WriteEntry>>(MaxCount);
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
                            if (!logEntry.inputs_.TryGetValue(command.input_, out readEntry))
                            {
                                readEntry = new ReadEntry();
                                readEntry.files_ = new List<string>(MaxCount);
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
                logEntry.inputs_ = new Dictionary<string, ReadEntry>(MaxCount);
                logEntry.outputs_ = new Dictionary<string, List<WriteEntry>>(MaxCount);
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
                                readEntry.files_ = new List<string>(MaxCount);
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
                logEntry.inputs_ = new Dictionary<string, ReadEntry>(MaxCount);
                logEntry.outputs_ = new Dictionary<string, List<WriteEntry>>(MaxCount);
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
                        if (line.StartsWith("#Command: "))
                        {
                            line = line.Substring("#Command: ".Length);
                            CommandLineParser.Command command = commandLineParser_.GetCommand(line);
                            if (string.IsNullOrEmpty(command.input_))
                            {
                                continue;
                            }
                            string input = command.input_.TrimEnd('"').TrimEnd();
                            if(input.EndsWith(".rsp", StringComparison.OrdinalIgnoreCase))
                            {
                            }else if(string.IsNullOrEmpty(command.output_))
                            {
                                continue;
                            }

                            List<WriteEntry> writeEntry;
                            if (logEntry.outputs_.TryGetValue(command.options_, out writeEntry))
                            {
                                writeEntry.Add(new WriteEntry() { input_ = command.input_, output_ = command.output_ });
                            }
                            else
                            {
                                writeEntry = new List<WriteEntry>(16);
                                writeEntry.Add(new WriteEntry() { input_ = command.input_, output_ = command.output_ });
                                logEntry.outputs_.Add(command.options_, writeEntry);
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
