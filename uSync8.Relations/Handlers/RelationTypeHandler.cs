using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Umbraco.Core;
using Umbraco.Core.Cache;
using Umbraco.Core.Logging;
using Umbraco.Core.Models;
using Umbraco.Core.Services;
using Umbraco.Core.Services.Implement;
using uSync8.BackOffice;
using uSync8.BackOffice.Configuration;
using uSync8.BackOffice.Services;
using uSync8.BackOffice.SyncHandlers;
using uSync8.Core.Dependency;
using uSync8.Core.Serialization;
using uSync8.Core.Tracking;
using static Umbraco.Core.Constants;

namespace uSync8.Relations.Handlers
{
    [SyncHandler("relationTypeHandler", "Relations",
        "RelationTypes", uSyncBackOfficeConstants.Priorites.RelationTypes,
        Icon = "icon-traffic usync-addon-icon",
        EntityType = UdiEntityType.RelationType, IsTwoPass = false)]
    public class RelationTypeHandler : SyncHandlerBase<IRelationType, IRelationService>,
        ISyncExtendedHandler
    {
        private readonly IRelationService relationService;

        public RelationTypeHandler(
            IEntityService entityService, 
            IProfilingLogger logger, 
            IRelationService relationService,
            ISyncSerializer<IRelationType> serializer, 
            ISyncTracker<IRelationType> tracker, 
            AppCaches appCaches, 
            ISyncDependencyChecker<IRelationType> dependencyChecker,
            SyncFileService syncFileService) 
            : base(entityService, logger, serializer, tracker, appCaches, dependencyChecker, syncFileService)
        {
            this.relationService = relationService;
        }

        public override IEnumerable<uSyncAction> ExportAll(int parent, string folder, HandlerSettings config, SyncUpdateCallback callback)
        {
            var actions = new List<uSyncAction>();

            var items = relationService.GetAllRelationTypes().ToList();

            foreach (var item in items.Select((relationType, index) => new { relationType, index }))
            {
                callback?.Invoke(item.relationType.Name, item.index, items.Count);
                actions.AddRange(Export(item.relationType, folder, config));
            }

            return actions;
        }


        protected override void DeleteViaService(IRelationType item)
            => relationService.Delete(item);

        protected override IRelationType GetFromService(int id)
            => relationService.GetRelationTypeById(id);

        protected override IRelationType GetFromService(Guid key)
            => relationService.GetAllRelationTypes().FirstOrDefault(x => x.Key == key);

        protected override IRelationType GetFromService(string alias)
            => relationService.GetRelationTypeByAlias(alias);

        protected override string GetItemName(IRelationType item)
            => item.Name;

        protected override string GetItemPath(IRelationType item, bool useGuid, bool isFlat)
            => useGuid ? item.Key.ToString() : item.Alias.ToSafeAlias();

        protected override void InitializeEvents(HandlerSettings settings)
        {
            RelationService.SavedRelationType += EventSavedItem;
            RelationService.DeletedRelationType += EventDeletedItem;
        }
    }
}
