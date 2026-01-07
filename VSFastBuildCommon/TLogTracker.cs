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
using static System.Windows.Forms.LinkLabel;

namespace VSFastBuildCommon
{
    public class TLogTracker : IDisposable
    {
        public static bool IsAlpha(char c)
        {

            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        }

        public static bool IsSeparator(char c)
        {
            return c == '\\' || c == '/';
        }

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
                Lib,
                Unknown,
            }

            public struct Command
            {
                public CommandType type_;
                public string name_;
                public List<string> inputs_;
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
                    inputs_ = new List<string>(),
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
                command.name_ = args[0];
                for (int i = 1; i < args.Length;)
                {
                    System.Diagnostics.Debug.Assert(0 < args[i].Length);
                    if (args[i].StartsWith("/Fo"))
                    {
                        if (args[i] == "/Fo")
                        {
                            if ((i + 1) < args.Length)
                            {
                                command.output_ = args[i + 1];
                                i += 2;
                            }
                            else
                            {
                                ++i;
                            }
                        }
                        else
                        {
                            command.output_ = args[i].Substring(3);
                            ++i;
                        }
                    }
                    else if (args[i].StartsWith("/OUT:"))
                    {
                        if (args[i] == "/OUT:")
                        {
                            if ((i + 1) < args.Length)
                            {
                                command.output_ = args[i + 1];
                                i += 2;
                            }
                            else
                            {
                                ++i;
                            }
                        }
                        else
                        {
                            command.output_ = args[i].Substring(4);
                            ++i;
                        }
                    }
                    else if (args[i].StartsWith("/ifcOutput", StringComparison.OrdinalIgnoreCase))
                    {
                        if (0 < optionBuilder_.Length)
                        {
                            optionBuilder_.Append(' ');
                        }
                        optionBuilder_.Append(args[i]);

                        if (args[i].ToUpperInvariant() == "/IFCOUTPUT")
                        {
                            if ((i + 1) < args.Length)
                            {
                                optionBuilder_.Append(' ');
                                optionBuilder_.Append(args[i + 1]);
                                i += 2;
                            }
                            else
                            {
                                ++i;
                            }
                        }
                        else
                        {
                            ++i;
                        }
                    }
                    else if (args[i].StartsWith("/") || args[i].StartsWith("-"))
                    {
                        if (0 < optionBuilder_.Length)
                        {
                            optionBuilder_.Append(' ');
                        }
                        optionBuilder_.Append(args[i]);
                        ++i;
                    }
                    else
                    {
                        command.inputs_.Add(args[i]);
                        ++i;
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
        private static Regex FBuildTLogCommand = new Regex(@"^FBuild\.\d+\.\d+\.command\.\d+\.tlog");

        private static Regex TLogRead = new Regex(@"^FBuild\.\d+\.\d+-([a-zA-Z0-9\-_]+)\.(\d+\.)?read\.\d+\.tlog");
        private static Regex TLogWrite = new Regex(@"^FBuild\.\d+\.\d+-([a-zA-Z0-9\-_]+)\.(\d+\.)?write\.\d+\.tlog");
        private static Regex TLogCommand = new Regex(@"^FBuild\.\d+\.\d+-([a-zA-Z0-9\-_]+)\.(\d+\.)?command\.\d+\.tlog");

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

        private class CommandEntry
        {
            public CommandLineParser.Command command_;
        }

        private struct TLogEntry
        {
            public string name_;
            public Dictionary<string, ReadEntry> inputs_;
            public Dictionary<string, WriteEntry> outputs_;
            public Dictionary<string, CommandEntry> commands_;
        }

        public string Path { get { return work_; } }
        private string path_ = string.Empty;
        private string work_ = string.Empty;
        private string temp_ = string.Empty;
        private CommandLineParser commandLineParser_ = new CommandLineParser();
        private Dictionary<string, TLogEntry> logEntries_ = new Dictionary<string, TLogEntry>();
        private const int Capacity = 16;
        private bool disposed_ = false;

        public TLogTracker(string path, string temp)
        {
            System.Diagnostics.Debug.Assert(!string.IsNullOrEmpty(path));
            path_ = path;
            if (!System.IO.Directory.Exists(path_))
            {
                System.IO.Directory.CreateDirectory(path_);
            }

            work_ = path_ + ".tmp";
            if (!System.IO.Directory.Exists(work_))
            {
                System.IO.Directory.CreateDirectory(work_);
            }

            temp_ = temp;

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
                    path_ = null;
                    work_ = null;
                }
                disposed_ = true;
            }
        }

        public void Start()
        {
            logEntries_.Clear();
            //clean and current
            foreach (string path in System.IO.Directory.GetFiles(work_, "*.tlog"))
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

                match = TLogCommand.Match(name);
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

                match = FBuildTLogCommand.Match(name);
                if (null != match && match.Success)
                {
                    TryDelete(path);
                    continue;
                }

                //match = LastTLogRead.Match(name);
                //if (null != match && match.Success)
                //{
                //    string subroot = match.Groups[1].Value;
                //    LoadRead(subroot, path);
                //}
                //match = LastTLogWrite.Match(name);
                //if (null != match && match.Success)
                //{
                //    string subroot = match.Groups[1].Value;
                //    LoadWrite(subroot, path);
                //}
                TryDelete(path);
            }
        }

        private static bool NeedSaveRead(KeyValuePair<string, TLogEntry> logEntry)
        {
            if (0 <= logEntry.Key.IndexOf("Link-cvtres", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (0 <= logEntry.Key.IndexOf("CMD", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
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
            if (0 <= logEntry.Key.IndexOf("Link-cvtres", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
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

        private static bool NeedSaveCommand(KeyValuePair<string, TLogEntry> logEntry)
        {
            return true;
        }

        public void Save()
        {
            foreach (string path in System.IO.Directory.GetFiles(work_, "*.tlog"))
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

                match = TLogCommand.Match(name);
                if (null != match && match.Success)
                {
                    string subroot = match.Groups[1].Value;
                    AddCommand(subroot, path);
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

                match = FBuildTLogCommand.Match(name);
                if (null != match && match.Success)
                {
                    TryDelete(path);
                    continue;
                }
                TryDelete(path);
            }

            // check rsp
            foreach (KeyValuePair<string, TLogEntry> logEntry in logEntries_)
            {
                foreach (WriteEntry writeEntry in logEntry.Value.outputs_.Values)
                {
                    string input = writeEntry.command_.inputs_[0].TrimEnd('"').TrimEnd().ToUpperInvariant();
                    if (input.EndsWith(".RSP", StringComparison.OrdinalIgnoreCase))
                    {
                        ReadEntry readEntry;
                        if (logEntry.Value.inputs_.TryGetValue(input, out readEntry))
                        {
                            for (int i = 0; i < readEntry.files_.Count;)
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
                        using (FileStream fileStream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None))
                        using (StreamWriter streamWriter = new StreamWriter(fileStream, Encoding.Unicode))
                        {
                            Dictionary<string, ReadEntry>.ValueCollection dictionary = logEntry.Value.inputs_.Values;
                            foreach (KeyValuePair<string, ReadEntry> entry in logEntry.Value.inputs_)
                            {
                                streamWriter.Write('^');
                                string input = entry.Key.TrimEnd('"').TrimStart('@').TrimEnd();
                                if (input.EndsWith(".RSP", StringComparison.OrdinalIgnoreCase))
                                {
                                    inputList.Clear();
                                    for (int i = 0; i < entry.Value.files_.Count; ++i)
                                    {
                                        if (entry.Value.files_[i].EndsWith(".RSP", StringComparison.OrdinalIgnoreCase))
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

                    if (NeedSaveCommand(logEntry))
                    {
                        path = System.IO.Path.Combine(path_, $"{logEntry.Key}.command.1.tlog");
                        using (FileStream fileStream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None))
                        using (StreamWriter streamWriter = new StreamWriter(fileStream, Encoding.Unicode))
                        {
                            foreach (KeyValuePair<string, CommandEntry> entry in logEntry.Value.commands_)
                            {
                                streamWriter.Write('^');
                                streamWriter.WriteLine(entry.Key);
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
                logEntry.commands_ = new Dictionary<string, CommandEntry>(Capacity);
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
                            if (command.inputs_.Count <= 0)
                            {
                                break;
                            }
                            string key = command.inputs_[0].TrimEnd('"').TrimStart('@').TrimEnd().ToUpperInvariant();
                            if (!logEntry.inputs_.TryGetValue(key, out readEntry))
                            {
                                readEntry = new ReadEntry();
                                readEntry.command_ = command;
                                logEntry.inputs_.Add(key, readEntry);
                            }
                        }
                        else if (null != readEntry)
                        {
                            if (!line.StartsWith(temp_, StringComparison.OrdinalIgnoreCase) && readEntry.files_.FindIndex((string x0) => x0 == line) < 0)
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

#if false
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
#endif

        private void AddWrite(string name, string path)
        {
            TLogEntry logEntry;
            if (!logEntries_.TryGetValue(name, out logEntry))
            {
                logEntry.name_ = name;
                logEntry.inputs_ = new Dictionary<string, ReadEntry>(Capacity);
                logEntry.outputs_ = new Dictionary<string, WriteEntry>(Capacity);
                logEntry.commands_ = new Dictionary<string, CommandEntry>(Capacity);
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
                            if (command.inputs_.Count <= 0)
                            {
                                continue;
                            }
                            string key = string.IsNullOrEmpty(command.options_) ? command.inputs_[0].ToUpperInvariant() : command.options_.ToUpperInvariant();

                            if (!logEntry.outputs_.TryGetValue(key, out writeEntry))
                            {
                                writeEntry = new WriteEntry();
                                writeEntry.command_ = command;
                                logEntry.outputs_.Add(key, writeEntry);
                            }
                            string input = command.inputs_[0].TrimEnd('"').TrimStart('@').TrimEnd();
                            if (input.EndsWith(".RSP", StringComparison.OrdinalIgnoreCase))
                            {
                            }
                            else
                            {
                                if (!string.IsNullOrEmpty(input))
                                {
                                    writeEntry.inputs_.Add(input);
                                }
                                if (!string.IsNullOrEmpty(command.output_))
                                {
                                    writeEntry.outputs_.Add(command.output_.ToUpperInvariant());
                                }
                            }
                        }
                        else if (null != writeEntry)
                        {
                            if (!line.StartsWith(temp_, StringComparison.OrdinalIgnoreCase) && writeEntry.outputs_.FindIndex((string x0) => x0 == line) < 0)
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

        private static bool IsDrive(int index, string line)
        {
            System.Diagnostics.Debug.Assert(index < line.Length);
            if (IsAlpha(line[index]))
            {
                if ((index + 3) <= line.Length && line[index + 1] == ':' && (line[index + 2] == '\\' || line[index + 2] == '/'))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsUNC(int index, string line)
        {
            System.Diagnostics.Debug.Assert(index < line.Length);
            if ('\\' == line[index])
            {
                if ((index + 1) < line.Length && '\\' == line[index + 1])
                {
                    return true;
                }
            }
            else if ('/' == line[index])
            {
                if ((index + 1) < line.Length && '/' == line[index + 1])
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsOption(int index, string line)
        {
            System.Diagnostics.Debug.Assert(index < line.Length);
            if ('/' == line[index])
            {
                if ((index + 1) < line.Length && IsAlpha(line[index+1]))
                {
                    return true;
                }
            }
            return false;
        }

        private static int FindEnd(int index, string line)
        {
            for (int i = index; i < line.Length;)
            {
                for (int i = 0; i < line.Length;)
            {
                if (IsDrive(i, line))
                {
                    int end = FindEnd(i + 3, line);
                    if (end < 0)
                    {
                        break;
                    }
                    if (first)
                    {
                        first = false;
                        libCommand.command_.name_ = line.Substring(i, end - i+1);
                    }
                    else
                    {
                        libCommand.command_.inputs_.Add(line.Substring(i, end - i+1));
                    }
                    i = end;
                }
                else if (IsUNC(i, line))
                {
                    int end = FindEnd(i + 2, line);
                    if (end < 0)
                    {
                        break;
                    }
                    if (first)
                    {
                        first = false;
                        libCommand.command_.name_ = line.Substring(i, end - i + 1);
                    }
                    else
                    {
                        libCommand.command_.inputs_.Add(line.Substring(i, end - i + 1));
                    }
                    i = end;
                }else if(IsOption(i, line))
                {
                    int end = FindEnd(i + 1, line);
                    if (end < 0)
                    {
                        break;
                    }
                    libCommand.command_.options_ += $" {line.Substring(i, end - i+1)}";
                    i = end;
                }
                else
                {
                    ++i;
                }
            }
        }

        private static void ParseLibCommand(CommandEntry libCommand, string line)
        {
            line = line.Trim();
            bool first = true;
            for (int i = 0; i < line.Length;)
            {
                if (IsDrive(i, line))
                {
                    int end = FindEnd(i + 3, line);
                    if (end < 0)
                    {
                        break;
                    }
                    if (first)
                    {
                        first = false;
                        libCommand.command_.name_ = line.Substring(i, end - i+1);
                    }
                    else
                    {
                        libCommand.command_.inputs_.Add(line.Substring(i, end - i+1));
                    }
                    i = end;
                }
                else if (IsUNC(i, line))
                {
                    int end = FindEnd(i + 2, line);
                    if (end < 0)
                    {
                        break;
                    }
                    if (first)
                    {
                        first = false;
                        libCommand.command_.name_ = line.Substring(i, end - i + 1);
                    }
                    else
                    {
                        libCommand.command_.inputs_.Add(line.Substring(i, end - i + 1));
                    }
                    i = end;
                }else if(IsOption(i, line))
                {
                    int end = FindEnd(i + 1, line);
                    if (end < 0)
                    {
                        break;
                    }
                    libCommand.command_.options_ += $" {line.Substring(i, end - i+1)}";
                    i = end;
                }
                else
                {
                    ++i;
                }
            }
        }

        private void AddCommand(string name, string path)
        {
            TLogEntry logEntry;
            if (!logEntries_.TryGetValue(name, out logEntry))
            {
                logEntry.name_ = name;
                logEntry.inputs_ = new Dictionary<string, ReadEntry>(Capacity);
                logEntry.outputs_ = new Dictionary<string, WriteEntry>(Capacity);
                logEntry.commands_ = new Dictionary<string, CommandEntry>(Capacity);
                logEntries_.Add(name, logEntry);
            }
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                using (StreamReader streamReader = new StreamReader(stream))
                {
                    string line;
                    CommandEntry lastLibCommand = null;
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
                            for (int i = 0; i < command.inputs_.Count;)
                            {
                                string input = command.inputs_[i].Trim(' ', '\"');
                                if (command.inputs_[i].EndsWith(".rsp", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (command.type_ == CommandLineParser.CommandType.Link)
                                    {
                                        return;
                                    }
                                    break;
                                }
                            }
                            if (command.inputs_.Count <= 0)
                            {
                                continue;
                            }
                            string key = line.ToUpperInvariant();
                            if (!logEntry.commands_.ContainsKey(key))
                            {
                                CommandEntry commandEntry = new CommandEntry() { command_ = command };
                                logEntry.commands_.Add(key, commandEntry);
                                if (command.type_ == CommandLineParser.CommandType.Lib)
                                {
                                    lastLibCommand = commandEntry;
                                }
                            }
                        }
                        else if (null != lastLibCommand)
                        {
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
