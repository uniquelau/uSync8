using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

using Umbraco.Core.Logging;
using Umbraco.Core.Models;
using Umbraco.Core.Services;

using uSync8.Core;
using uSync8.Core.Extensions;
using uSync8.Core.Models;
using uSync8.Core.Serialization;

namespace uSync8.Relations.Serializers
{
    [SyncSerializer("19FA7E6D-3B88-44AA-AED4-94634C90A5B4", "RelationTypeSerializer", uSyncConstants.Serialization.RelationType)]
    public class RelationTypeSerializer :
        SyncSerializerBase<IRelationType>, ISyncSerializer<IRelationType>
    {
        private readonly IRelationService relationService;

        public RelationTypeSerializer(
            IEntityService entityService,
            IRelationService relationService,
            ILogger logger) 
            : base(entityService, logger)
        {
            this.relationService = relationService;
        }
        protected override SyncAttempt<IRelationType> DeserializeCore(XElement node)
        {
            var key = node.GetKey();
            var alias = node.GetAlias();

            var name = node.Element("Name").ValueOrDefault(string.Empty);
            var parentType = node.Element("ParentType").ValueOrDefault(Guid.Empty);
            var childType = node.Element("ChildType").ValueOrDefault(Guid.Empty);
            var bidirectional = node.Element("Bidirectional").ValueOrDefault(false);

            // will attempt to find the item by key and then by alias
            var item = FindItem(node);

            if (item == null)
            {
                // create a new relation type.
                item = new RelationType(childType, parentType, alias);
            }

            var changes = new List<uSyncChange>();

            if (item.Key != key) 
                item.Key = key;

            if (item.Name != Name) 
                item.Name = name;

            if (item.Alias != alias) 
                item.Alias = alias;

            if (item.ParentObjectType != parentType) 
                item.ParentObjectType = parentType;

            if (item.ChildObjectType != childType)
                item.ChildObjectType = childType;

            if (item.IsBidirectional != bidirectional)
                item.IsBidirectional = bidirectional;

            changes.AddRange(DeserializeRelations(node, item));

            var attempt = SyncAttempt<IRelationType>.Succeed(item.Name, item, ChangeType.Import);
            attempt.Details = changes;

            return attempt;
        }

        private IEnumerable<uSyncChange> DeserializeRelations(XElement node, IRelationType relationType)
        {
            var changes = new List<uSyncChange>();

            var existingRelations = relationService
                .GetAllRelationsByRelationType(relationType.Id)
                .ToList();

            var relations = node.Element("Relations");
            if (relations == null) return changes;

            var newRelations = new List<string>();

            foreach(var relationNode in relations.Elements("Relation"))
            {
                var parentKey = relationNode.Element("Parent").ValueOrDefault(Guid.Empty);
                var childKey = relationNode.Element("Child").ValueOrDefault(Guid.Empty);

                if (parentKey == Guid.Empty || childKey == Guid.Empty) continue;
                var parentItem = entityService.Get(parentKey);
                var childItem = entityService.Get(childKey);

                if (parentItem == null || childItem == null) continue;

                if (!existingRelations.Any(x => x.ParentId == parentItem.Id && x.ChildId == childItem.Id))
                {
                    // missing from the current list... add it. 
                    logger.Debug<RelationTypeSerializer>("Adding : {parentId} {childId} {type} ", parentItem.Id, childItem.Id, relationType.Alias);
                    relationService.Save(new Relation(parentItem.Id, childItem.Id, relationType));
                    changes.Add(uSyncChange.Create(relationType.Alias, parentItem.Name, childItem.Name));
                }

                // a #hash# of both the parent and childid's for comparing below.
                newRelations.Add($"{parentItem.Id}_{childItem.Id}");
            }

            // the string value, makes this call much easier to read. 
            var obsoleteRelations = existingRelations.Where(x => !newRelations.Contains($"{x.ParentId}_{x.ChildId}"));

            foreach(var obsoleteRelation in obsoleteRelations)
            {
                logger.Debug<RelationTypeSerializer>("Removing : {parentId} {childId} {type} ", obsoleteRelation.ParentId, obsoleteRelation.ChildId, relationType.Alias);
                changes.Add(uSyncChange.Delete(relationType.Alias, obsoleteRelation.ParentId.ToString(), obsoleteRelation.ChildId.ToString()));

                relationService.Delete(obsoleteRelation);
            }

            return changes;
        }

        public override bool IsValid(XElement node)
        {
            if (node?.Element("Info")?.Element("Name") == null) return false;
            return base.IsValid(node);
        }


        protected override SyncAttempt<XElement> SerializeCore(IRelationType item)
        {
            var node = this.InitializeBaseNode(item, item.Alias);

            node.Add(new XElement("Info",
                new XElement("Name", item.Name),
                new XElement("ParentType", item.ParentObjectType),
                new XElement("ChildType", item.ChildObjectType),
                new XElement("Bidirectional", item.IsBidirectional)));

            node.Add(SerializeRelations(item));

            return SyncAttempt<XElement>.SucceedIf(
                node != null,
                item.Name,
                node,
                typeof(IRelationType),
                ChangeType.Export);
        }

        private XElement SerializeRelations(IRelationType item)
        {
            var relations = relationService.GetAllRelationsByRelationType(item.Id);

            var node = new XElement("Relations");

            foreach(var relation in relations)
            {
                var relationNode = new XElement("Relation");

                var entities = relationService.GetEntitiesFromRelation(relation);

                if (entities.Item1 != null)
                    relationNode.Add(new XElement("Parent", entities.Item1.Key));

                if (entities.Item2 != null)
                    relationNode.Add(new XElement("Child",entities.Item2.Key));

                node.Add(relationNode);
            }

            return node;
        }


        protected override void DeleteItem(IRelationType item)
            => relationService.Delete(item);


        protected override IRelationType FindItem(Guid key)
            => relationService.GetAllRelationTypes().FirstOrDefault(x => x.Key == key);

        protected override IRelationType FindItem(string alias)
            => relationService.GetRelationTypeByAlias(alias);

        protected override string ItemAlias(IRelationType item)
            => item.Alias;

        protected override void SaveItem(IRelationType item)
            => relationService.Save(item);
    }
}
