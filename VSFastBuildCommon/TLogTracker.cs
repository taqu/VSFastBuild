using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VSFastBuildCommon
{
    internal class TLogTracker : IDisposable
    {
        private const int MaxCount = 16;
        private bool disposed_ = false;
        private FileSystemWatcher watcher_ = null;
        private List<TaskItem> taskItemInputs_ = new List<TaskItem>(16);
        private List<TaskItem> taskItemOutputs_ = new List<TaskItem>(16);
        private CanonicalTrackedInputFiles canonicalTrackedInputFiles_;
        private CanonicalTrackedOutputFiles canonicalTrackedOutputFiles_;

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
                    if(null != watcher_)
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
            if (0 < taskItemInputs_.Count)
            {
                AddCanonicalTrackedInputFiles();
            }
            if (0 < taskItemOutputs_.Count)
            {
                AddCanonicalTrackedOutputFiles();
            }
            canonicalTrackedInputFiles_?.SaveTlog();
            canonicalTrackedOutputFiles_?.SaveTlog();
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Name))
            {
                return;
            }
            if (e.Name.Contains(".read."))
            {
                TaskItem item = new TaskItem(e.FullPath, true);
                taskItemInputs_.Add(item);
                if (MaxCount <= taskItemInputs_.Count)
                {
                    AddCanonicalTrackedInputFiles();
                }
            }else if (e.Name.Contains(".write."))
            {
                TaskItem item = new TaskItem(e.FullPath, true);
                taskItemOutputs_.Add(item);
                if (MaxCount <= taskItemOutputs_.Count)
                {
                    AddCanonicalTrackedOutputFiles();
                }
            }
        }

        private void AddCanonicalTrackedInputFiles()
        {
            if(null == canonicalTrackedInputFiles_)
            {
                canonicalTrackedInputFiles_ = new CanonicalTrackedInputFiles(taskItemInputs_.ToArray(), null, null, false, true);
            }
        }

        private void AddCanonicalTrackedOutputFiles()
        {
            if(null == canonicalTrackedOutputFiles_)
            {
                canonicalTrackedOutputFiles_ = new CanonicalTrackedOutputFiles(taskItemOutputs_.ToArray());
            }
            else
            {
                ITaskItem[] outputs = canonicalTrackedOutputFiles_.OutputsForNonCompositeSource(taskItemOutputs_.ToArray());
            }
            taskItemOutputs_.Clear();
        }
    }
}
