using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Table;

namespace CodeSearch.Notifier
{
    public class StringEntity : TableEntity
    {
        public string Value { get; set; }
    }
}
