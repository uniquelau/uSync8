using Umbraco.Core;
using Umbraco.Core.Composing;
using Umbraco.Core.Models;

using uSync8.BackOffice;
using uSync8.Core;
using uSync8.Core.Dependency;
using uSync8.Core.Serialization;
using uSync8.Core.Tracking;
using uSync8.Relations.Checkers;
using uSync8.Relations.Serializers;
using uSync8.Relations.Trackers;

namespace uSync8.Relations
{
    // ensure we register these items after the core
    [ComposeAfter(typeof(uSyncCoreComposer))]
    // but before the back office loads the hanlers.
    [ComposeBefore(typeof(uSyncBackOfficeComposer))]
    public class uSyncRelationComposer : IUserComposer
    {
        public void Compose(Composition composition)
        {
            composition.Register<ISyncSerializer<IRelationType>, RelationTypeSerializer>();
            composition.Register<ISyncTracker<IRelationType>, RelationTypeTracker>();
            composition.Register<ISyncDependencyChecker<IRelationType>, RelationTypeChecker>();
        }
    }
}
