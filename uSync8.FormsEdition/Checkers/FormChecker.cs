using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Umbraco.Core.Models;
using Umbraco.Forms.Core.Models;
using uSync8.Core.Dependency;

namespace uSync8.FormsEdition.Checkers
{
    public class FormChecker : ISyncDependencyChecker<Form>
    {
        public UmbracoObjectTypes ObjectType => UmbracoObjectTypes.Unknown;

        public IEnumerable<uSyncDependency> GetDependencies(Form item, DependencyFlags flags)
        {
            uSyncDependency.FireUpdate(item.Name);
            return Enumerable.Empty<uSyncDependency>();
        }
    }
}
