namespace CodeSearch
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;

    /// <summary>
    /// A worker program
    /// </summary>
    public class WorkerProgram
    {
        private const string ShutdownFileEnvironmentKey = "WEBJOBS_SHUTDOWN_FILE";

        private CancellationTokenSource cancellationToken;
        private string name;
        private Worker worker;

        /// <summary>
        /// Creates a new instance of this class.
        /// </summary>
        /// <param name="name">Name of the program.</param>
        /// <param name="worker">The worker being executed inside the program.</param>
        public WorkerProgram(string name, Worker worker)
        {
            this.name = name;
            this.worker = worker;
        }

        /// <summary>
        /// Run the compensation agent
        /// </summary>
        public void Run()
        {
            bool traceToConsole = true;
            if ("true".Equals(Environment.GetEnvironmentVariable("NO_CONSOLE_TRACE"), StringComparison.OrdinalIgnoreCase))
            {
                traceToConsole = false;
            }

            // setup tracing
            TraceListener traceListener = new ConsoleTraceListener();
            if (traceToConsole)
            {
                Trace.Listeners.Add(traceListener);
            }

            // setup shutdown signals
            Console.CancelKeyPress += this.CancelKeyPress;
            this.StartWatchingShutdownFile();

            try
            {
                Trace.TraceInformation(this.name + " started on " + Environment.MachineName);

                this.cancellationToken = new CancellationTokenSource();
                this.worker.Setup(this.cancellationToken.Token).Wait();
                this.worker.Run(TimeSpan.FromSeconds(60), this.cancellationToken.Token).Wait();
            }
            finally
            {
                this.worker.TearDown(this.cancellationToken.Token).Wait();

                if (traceToConsole)
                {
                    Trace.Listeners.Remove(traceListener);
                }

                Console.CancelKeyPress -= this.CancelKeyPress;
            }
        }

        private void CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            this.cancellationToken.Cancel();
            e.Cancel = true;
            Trace.TraceInformation("Stop requested. Waiting for the process to finish...");
        }

        private void StartWatchingShutdownFile()
        {
            string shutdownFile = Environment.GetEnvironmentVariable(ShutdownFileEnvironmentKey);
            if (string.IsNullOrWhiteSpace(shutdownFile))
            {
                Trace.TraceWarning("No shutdown file specified");
                return;
            }

            Trace.TraceInformation("Shut down file: {0}", shutdownFile);

            FileSystemWatcher watcher = new FileSystemWatcher(Path.GetDirectoryName(shutdownFile));
            watcher.Created += this.ShutdownDirectoryChanged;
            watcher.Changed += this.ShutdownDirectoryChanged;
            watcher.NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.FileName | NotifyFilters.LastWrite;
            watcher.IncludeSubdirectories = false;
            watcher.EnableRaisingEvents = true;
        }

        private void ShutdownDirectoryChanged(object sender, FileSystemEventArgs e)
        {
            string shutdownFile = Environment.GetEnvironmentVariable(ShutdownFileEnvironmentKey);
            Trace.TraceInformation("ShutdownDirectoryChanged");
            Trace.TraceInformation(e.FullPath);

            if (e.FullPath.IndexOf(Path.GetFileName(shutdownFile), StringComparison.OrdinalIgnoreCase) >= 0)
            {
                this.cancellationToken.Cancel();
            }
        }
    }
}
