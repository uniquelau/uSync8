using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Umbraco.Core;
using Umbraco.Core.Composing;
using Umbraco.Forms.Core.Models;
using uSync8.BackOffice;
using uSync8.BackOffice.Models;
using uSync8.Core;
using uSync8.Core.Dependency;
using uSync8.Core.Serialization;
using uSync8.Core.Tracking;
using uSync8.FormsEdition.Checkers;
using uSync8.FormsEdition.Serializers;
using uSync8.FormsEdition.Trackers;

namespace uSync8.FormsEdition
{
    public class uSyncForms : ISyncAddOn
    {
        public string Name => "Forms";
        public string Version => "8.3.0";

        public string Icon => "icon-form";

        public string View => string.Empty;

        public string Alias => "forms";

        public string DisplayName => "Forms";

        public int SortOrder => 11;
    }

    [ComposeAfter(typeof(uSyncCoreComposer))]
    [ComposeBefore(typeof(uSyncBackOfficeComposer))]
    public class uSyncFormsComposer : IUserComposer
    {
        public void Compose(Composition composition)
        {
            composition.Register<ISyncSerializer<Form>, FormsSerializer>();
            composition.Register<ISyncTracker<Form>, FormTracker>();
            composition.Register<ISyncDependencyChecker<Form>, FormChecker>();
        }
    }
}
