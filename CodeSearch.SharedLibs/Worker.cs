namespace CodeSearch
{
    using System;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A worker processor
    /// </summary>
    public abstract class Worker
    {
        /// <summary>
        /// Runs an iteration.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The amount of time to wait until the next iteration.</returns>
        public abstract Task<TimeSpan> RunIteration(CancellationToken cancellationToken);

        public virtual Task Setup(CancellationToken cancellationToken) { return Task.FromResult(false); }

        public virtual Task TearDown(CancellationToken cancellationToken) { return Task.FromResult(false); }

        /// <summary>
        /// Runs the worker.
        /// </summary>
        /// <param name="defaultDelay">The default delay if an iteration errors out.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>An asynchronous task.</returns>
        public async Task Run(TimeSpan defaultDelay, CancellationToken cancellationToken)
        {
            Trace.TraceInformation("Worker started");

            while (cancellationToken.IsCancellationRequested == false)
            {
                bool exceptionCaught = false;

                try
                {
                    TimeSpan delay = await this.RunIteration(cancellationToken);
                    await Task.Delay(delay, cancellationToken);
                }
                catch (Exception ex)
                {
                    if (ex is OperationCanceledException == false || cancellationToken.IsCancellationRequested == false)
                    {
                        Trace.TraceError(ex.Message);
                        Trace.TraceError(ex.StackTrace);
                        exceptionCaught = true;
                    }
                }

                if (exceptionCaught)
                {
                    await Task.Delay(defaultDelay, cancellationToken);
                }
            }

            Trace.TraceInformation("Worker stopped");
        }
    }
}
