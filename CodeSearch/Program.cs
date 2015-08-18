using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Web;
using HtmlAgilityPack;
using Microsoft.WindowsAzure.Storage;

namespace CodeSearch
{
    class Program
    {
        static void Main(string[] args)
        {
            var worker = new CodeSearchWorker("DefaultEndpointsProtocol+AccountName+AccountKey");
            var workerProgram = new WorkerProgram("CodeSearch", worker);
            workerProgram.Run();
        }
    }
}
