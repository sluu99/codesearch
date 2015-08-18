using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeSearch.Notifier
{
    class Program
    {
        static void Main(string[] args)
        {
            var worker = new NotifierWorker();
            var workerProgram = new WorkerProgram("CodeSearch.Notifier", worker);
            workerProgram.Run();
        }
    }
}
