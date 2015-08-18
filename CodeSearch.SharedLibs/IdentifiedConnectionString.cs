using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace CodeSearch
{
    public class IdentifiedConnectionString
    {
        public string Repository { get; set; }
        public string ConnectionString { get; set; }

        public override bool Equals(object obj)
        {
            var x = obj as IdentifiedConnectionString;

            if (x == null)
            {
                return false;
            }

            return
                this.Repository == x.Repository &&
                this.ConnectionString == x.ConnectionString;
        }

        public override int GetHashCode()
        {
            int hash = string.Empty.GetHashCode();
            if (this.Repository != null)
            {
                hash ^= this.Repository.GetHashCode();
            }

            if (this.ConnectionString != null)
            {
                hash ^= this.ConnectionString.GetHashCode();
            }

            return hash;
        }

        [JsonIgnore]
        public bool HasAllFields
        {
            get
            {
                return
                    string.IsNullOrWhiteSpace(this.Repository) == false &&
                    string.IsNullOrWhiteSpace(this.ConnectionString) == false;
            }
        }
    }
}
