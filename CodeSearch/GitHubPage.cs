using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using HtmlAgilityPack;

namespace CodeSearch
{
    public static class GitHubPage
    {
        public static IEnumerable<IdentifiedConnectionString> IdentifyConnectionStrings(Stream pageStream)
        {
            var htmlDoc = new HtmlDocument();
            htmlDoc.Load(pageStream);

            var codeDivs = GetCodeDivs(htmlDoc);

            List<IdentifiedConnectionString> identifiedConnectionStrings = new List<IdentifiedConnectionString>();

            foreach (var codeDiv in codeDivs)
            {
                var connectionStrings = ExtractConnectionStrings(GetCodeContent(codeDiv));
                if (connectionStrings.Any() == false)
                {
                    continue;
                }

                var repoName = GetRepoName(codeDiv);

                identifiedConnectionStrings.AddRange(connectionStrings.Select(str =>
                    new IdentifiedConnectionString
                    {
                        Repository = repoName,
                        ConnectionString = str,
                    }));
            }

            return identifiedConnectionStrings.Where(x => x.HasAllFields).Distinct();
        }

        private static IEnumerable<HtmlNode> GetCodeDivs(HtmlDocument htmlDoc)
        {
            return
                from node in htmlDoc.DocumentNode.Descendants("div")
                where
                    node.Attributes["class"] != null &&
                    node.Attributes["class"].Value.Contains("code-list-item")
                select node;
        }

        private static string GetCodeContent(HtmlNode codeDiv)
        {
            var codeTds =
                from node in codeDiv.Descendants("td")
                where
                    node.Attributes["class"] != null &&
                    node.Attributes["class"].Value.Contains("blob-code")
                select node;

            if (codeTds.Any() == false)
            {
                return string.Empty;
            }

            return string.Join(Environment.NewLine, codeTds.Select(node => HttpUtility.HtmlDecode(node.InnerText.Trim())));
        }

        private static string GetRepoName(HtmlNode codeDiv)
        {
            var p = (
                from node in codeDiv.Descendants("p")
                where
                    node.Attributes["class"] != null &&
                    node.Attributes["class"].Value.Contains("title")
                select node)
                .FirstOrDefault();

            if (p == null)
            {
                return string.Empty;
            }

            var a = p.Descendants("a").FirstOrDefault();
            if (a == null)
            {
                return string.Empty;
            }

            return a.InnerText.Trim();
        }

        private static IEnumerable<string> ExtractConnectionStrings(string code)
        {
            List<string> results = new List<string>();

            int startSeachAt = 0;

            while (true)
            {
                int indexBegin = code.IndexOf("DefaultEndpointsProtocol", startSeachAt, StringComparison.OrdinalIgnoreCase);
                if (indexBegin == -1)
                {
                    break;
                }

                int indexEnd = code.IndexOf("==", startIndex: indexBegin + 1);
                if (indexEnd == -1)
                {
                    break;
                }

                // make sure there's not another "DefaultEndpointsProtocol" between the two
                int indexBeginInBetween = code.IndexOf("DefaultEndpointsProtocol", indexBegin + 1, StringComparison.OrdinalIgnoreCase);
                if (indexBeginInBetween != -1 && indexBeginInBetween < indexEnd)
                {
                    startSeachAt = indexBeginInBetween;
                    continue;
                }

                string connStr = code.Substring(indexBegin, indexEnd - indexBegin + 2);
                startSeachAt = indexBegin + connStr.Length;

                if (connStr.Contains('\r') || connStr.Contains('\n'))
                {
                    continue;
                }

                results.Add(connStr);
            }

            return results;
        }
    }
}
