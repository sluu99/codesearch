using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using Octokit;
using RestSharp;
using RestSharp.Authenticators;

namespace CodeSearch.Notifier
{
    public class NotifierWorker : Worker
    {
        private const string GitHubApiKeyEnvVarName = "CODESEARCH_GITHUB_TOKEN";
        private const string MailGunApiKeyEnvVarName = "CODESEARCH_MAILGUN_APIKEY";
        private const string MailGunDomainEnvVarName = "CODESEARCH_MAILGUN_DOMAIN";
        private const string StorageEnvVarName = "CODESEARCH_STORAGE";
        private const string QueueName = "tested-connection-strings";
        private const string TableName = "notifiedaccounts";
        private readonly TimeSpan ShortDelay = TimeSpan.FromSeconds(3);
        private readonly TimeSpan LongDelay = TimeSpan.FromSeconds(10);

        private CloudTable notifiedAccountsTable;
        private CloudQueue testedConnectionStringsQueue;

        public NotifierWorker()
        {
            var cloudAccount = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable(StorageEnvVarName));
            this.testedConnectionStringsQueue = cloudAccount.CreateCloudQueueClient().GetQueueReference(QueueName);
            this.notifiedAccountsTable = cloudAccount.CreateCloudTableClient().GetTableReference(TableName);
        }

        public override async Task Setup(CancellationToken cancellationToken)
        {
            await this.notifiedAccountsTable.CreateIfNotExistsAsync(cancellationToken);
            await this.testedConnectionStringsQueue.CreateIfNotExistsAsync(cancellationToken);

            await base.Setup(cancellationToken);
        }

        public override async Task<TimeSpan> RunIteration(CancellationToken cancellationToken)
        {
            var message = await this.testedConnectionStringsQueue.GetMessageAsync(cancellationToken);
            if (message == null)
            {
                return ShortDelay;
            }

            var connStr = JsonConvert.DeserializeObject<IdentifiedConnectionString>(message.AsString);

            if (await this.GetNotifiedAccountEntity(connStr, cancellationToken) == null)
            {
                var email = await FindGitHubEmail(connStr, cancellationToken);

                if (string.IsNullOrWhiteSpace(email) == false)
                {
                    await this.InsertNotifiedAccountEntity(connStr, cancellationToken);
                    await this.SendEmail(email, connStr, cancellationToken);
                }
            }

            await this.testedConnectionStringsQueue.DeleteMessageAsync(message);
            return TimeSpan.Zero;
        }

        private async Task<string> FindGitHubEmail(IdentifiedConnectionString connStr, CancellationToken cancellationToken)
        {
            var githubClient = new GitHubClient(new ProductHeaderValue("CodeSearch.Notifier"));
            githubClient.Credentials = new Credentials(Environment.GetEnvironmentVariable(GitHubApiKeyEnvVarName));

            var repoParts = connStr.Repository.Split('/');
            if (repoParts.Length != 2)
            {
                return null;
            }

            var githubLogin = repoParts[0];
            var user = await githubClient.User.Get(githubLogin);
            if (user == null)
            {
                return null;
            }

            int page = 1;
            while (true)
            {
                string url = string.Format("https://api.github.com/user/{0}/events?page={1}", user.Id, page);
                var events = await githubClient.Connection.Get<IEnumerable<Activity>>(new Uri(url), new Dictionary<string, string>(0), "application/json", cancellationToken);
                
                if (events.Body.Any() == false)
                {
                    break;
                }

                var pushEvent = events.Body.FirstOrDefault(e => "PushEvent".Equals(e.Type, StringComparison.OrdinalIgnoreCase));
                if (pushEvent != null)
                {
                    var pushPayload = pushEvent.Payload as PushEventPayload;
                    if (pushPayload != null && pushPayload.Commits != null && pushPayload.Commits.Count > 0)
                    {
                        var commit = pushPayload.Commits.FirstOrDefault(c => c.Author != null && string.IsNullOrWhiteSpace(c.Author.Email) == false);
                        return commit.Author.Email;
                    }
                }

                page++;
            }

            return string.Empty;
        }

        private async Task SendEmail(string toEmail, IdentifiedConnectionString connStr, CancellationToken cancellationToken)
        {
            var mailgunDomain = Environment.GetEnvironmentVariable(MailGunDomainEnvVarName);
            var mailgunApiKey = Environment.GetEnvironmentVariable(MailGunApiKeyEnvVarName);

            var cloudAccount = CloudStorageAccount.Parse(connStr.ConnectionString);
            var subject = string.Format("Warning: Your Azure storage account ({0}) might be exposed", cloudAccount.Credentials.AccountName);
            var body = new StringBuilder();
            body.AppendLine("Hi there!");
            body.AppendLine();
            body.AppendLine("Your Azure storage credentials are publicly visible from GitHub: ");
            body.AppendLine(string.Format(" - GitHub repository: {0}", connStr.Repository));
            body.AppendLine(string.Format(" - Storage account: {0}", cloudAccount.Credentials.AccountName));
            body.AppendLine(string.Format(" - Storage key: {0}...", cloudAccount.Credentials.ExportBase64EncodedKey().Substring(0, 7)));
            body.AppendLine();
            body.AppendLine("Here's a blog post from Scott Hanselman on how to keep the connection string in Azure: http://bit.ly/1NCcAN6");
            body.AppendLine();
            body.AppendLine("You can respond to this email or contact me on twitter @sluu99 to unsubscribe from these messages in the future.");
            body.AppendLine();
            body.AppendLine("Disclaimer: This is a personal project and is not associated with Microsoft nor Microsoft Azure.");

            var credentials = new NetworkCredential("api", mailgunApiKey);
            var postBody = new Dictionary<string, string>
            {
                { "from", "sluu99@gmail.com" },
                { "to", toEmail },
                { "bcc", "sluu99@gmail.com" },
                { "subject", subject },
                { "text", body.ToString() },
            };
            string url = string.Format("https://api.mailgun.net/v3/{0}/messages", mailgunDomain);

            using (var httpHandler = new HttpClientHandler { Credentials = credentials })
            using (var httpClient = new HttpClient(httpHandler))
            using (var postContent = new FormUrlEncodedContent(postBody))
            using (var postResponse = await httpClient.PostAsync(url, postContent, cancellationToken))
            {                
            }

            Trace.TraceInformation("Email sent to {0}: {1}", toEmail, subject);
        }

        private Task InsertNotifiedAccountEntity(IdentifiedConnectionString connStr, CancellationToken cancellationToken)
        {
            var entity = new StringEntity
            {
                PartitionKey = GetPartitionKey(connStr),
                RowKey = GetRowKey(connStr),
                Value = JsonConvert.SerializeObject(connStr),
            };

            var insertOp = TableOperation.Insert(entity);
            return this.notifiedAccountsTable.ExecuteAsync(insertOp, cancellationToken);
        }

        private async Task<StringEntity> GetNotifiedAccountEntity(IdentifiedConnectionString connStr, CancellationToken cancellationToken)
        {
            var retrieveOp = TableOperation.Retrieve<StringEntity>(GetPartitionKey(connStr), GetRowKey(connStr));
            var retrieveResponse = await this.notifiedAccountsTable.ExecuteAsync(retrieveOp, cancellationToken);

            return retrieveResponse.Result as StringEntity;
        }

        private static string GetPartitionKey(IdentifiedConnectionString connStr)
        {
            return Sha1(connStr.Repository.ToLowerInvariant());
        }

        private static string GetRowKey(IdentifiedConnectionString connStr)
        {
            return Sha1(connStr.ConnectionString.ToLowerInvariant());
        }

        private static string Sha1(string str)
        {
            if (str == null)
            {
                return null;
            }

            byte[] data = Encoding.UTF8.GetBytes(str);

            using (SHA1Managed sha = new SHA1Managed())
            {
                byte[] hashBytes = sha.ComputeHash(data);

                return BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}
