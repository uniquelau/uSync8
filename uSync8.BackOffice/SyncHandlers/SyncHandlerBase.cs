using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

using Umbraco.Core;
using Umbraco.Core.Composing;
using Umbraco.Core.Events;
using Umbraco.Core.Logging;
using Umbraco.Core.Models;
using Umbraco.Core.Models.Entities;
using Umbraco.Core.Services;

using uSync8.BackOffice.Configuration;
using uSync8.BackOffice.Services;
using uSync8.Core;
using uSync8.Core.Dependency;
using uSync8.Core.Extensions;
using uSync8.Core.Models;
using uSync8.Core.Serialization;
using uSync8.Core.Tracking;

namespace uSync8.BackOffice.SyncHandlers
{
    public abstract class SyncHandlerBase<TObject, TService> : SyncHandlerRoot<TObject>
        where TObject : IEntity
        where TService : IService
    {
        protected readonly IEntityService entityService;

        public SyncHandlerBase(
            IEntityService entityService,
            IProfilingLogger logger,
            ISyncSerializer<TObject> serializer,
            ISyncTracker<TObject> tracker,
            SyncFileService syncFileService)
        : this(entityService, logger, serializer, tracker, null, syncFileService) { }


        public SyncHandlerBase(
            IEntityService entityService,
            IProfilingLogger logger,
            ISyncSerializer<TObject> serializer,
            ISyncTracker<TObject> tracker,
            ISyncDependencyChecker<TObject> dependencyChecker,
            SyncFileService syncFileService)
            : base(logger, serializer, tracker, dependencyChecker, syncFileService)
        {
            this.entityService = entityService;
        }

        #region Importing 

        // everything is now in the root class. 
        protected override int GetItemId(TObject item) => item.Id;

        protected override IEnumerable<uSyncAction> DeleteMissingItems(TObject parent, IEnumerable<Guid> keys, bool reportOnly)
        {
            var items = GetChildItems(parent.Id).ToList();
            var actions = new List<uSyncAction>();
            foreach (var item in items)
            {
                if (!keys.Contains(item.Key))
                {
                    var actualItem = GetFromService(item.Key);
                    var name = GetItemName(actualItem);

                    if (!reportOnly)
                        DeleteViaService(actualItem);

                    actions.Add(uSyncActionHelper<TObject>.SetAction(SyncAttempt<TObject>.Succeed(name, ChangeType.Delete), string.Empty));
                }
            }

            return actions;
        }

        #endregion

        #region Exporting

        public override IEnumerable<uSyncAction> ExportAll(int parent, string folder, HandlerSettings config, SyncUpdateCallback callback)
        {
            var actions = new List<uSyncAction>();

            if (itemContainerType != UmbracoObjectTypes.Unknown)
            {
                var containers = GetFolders(parent);
                foreach (var container in containers)
                {
                    actions.AddRange(ExportAll(container.Id, folder, config, callback));
                }
            }

            var items = GetChildItems(parent).ToList();
            foreach (var item in items.Select((Value, Index) => new { Value, Index }))
            {
                var concreateType = GetFromService(item.Value.Id);
                callback?.Invoke(GetItemName(concreateType), item.Index, items.Count);

                actions.AddRange(Export(concreateType, folder, config));
                actions.AddRange(ExportAll(item.Value.Id, folder, config, callback));
            }

            // callback?.Invoke("Done", 1, 1);
            return actions;
        }

        // almost everything does this - but languages can't so we need to 
        // let the language Handler override this. 
        protected virtual IEnumerable<IEntity> GetChildItems(int parent)
        {
            if (this.itemObjectType != UmbracoObjectTypes.Unknown)
                return entityService.GetChildren(parent, this.itemObjectType);

            return Enumerable.Empty<IEntity>();
        }


        protected virtual IEnumerable<IEntity> GetFolders(int parent)
        {
            if (this.itemContainerType != UmbracoObjectTypes.Unknown)
                return entityService.GetChildren(parent, this.itemContainerType);

            return Enumerable.Empty<IEntity>();
        }


        public bool HasChildren(int id)
            => GetFolders(id).Any() || GetChildItems(id).Any();


        #endregion


        #region Events 

        protected virtual void EventDeletedItem(IService sender, Umbraco.Core.Events.DeleteEventArgs<TObject> e)
        {
            if (uSync8BackOffice.eventsPaused) return;
            foreach (var item in e.DeletedEntities)
            {
                ExportDeletedItem(item, Path.Combine(rootFolder, this.DefaultFolder), DefaultConfig);
            }
        }

        protected virtual void EventSavedItem(IService sender, SaveEventArgs<TObject> e)
        {
            if (uSync8BackOffice.eventsPaused) return;

            foreach (var item in e.SavedEntities)
            {
                var attempts = Export(item, Path.Combine(rootFolder, this.DefaultFolder), DefaultConfig);

                // if we are using guid names and a flat structure then the clean doesn't need to happen
                if (!(this.DefaultConfig.GuidNames && this.DefaultConfig.UseFlatStructure))
                {
                    foreach (var attempt in attempts.Where(x => x.Success))
                    {
                        this.CleanUp(item, attempt.FileName, Path.Combine(rootFolder, this.DefaultFolder));
                    }
                }
            }
        }

        protected virtual void EventMovedItem(IService sender, MoveEventArgs<TObject> e)
        {
            if (uSync8BackOffice.eventsPaused) return;

            foreach (var item in e.MoveInfoCollection)
            {
                var attempts = Export(item.Entity, Path.Combine(rootFolder, this.DefaultFolder), DefaultConfig);

                if (!(this.DefaultConfig.GuidNames && this.DefaultConfig.UseFlatStructure))
                {
                    foreach (var attempt in attempts.Where(x => x.Success))
                    {
                        this.CleanUp(item.Entity, attempt.FileName, Path.Combine(rootFolder, this.DefaultFolder));
                    }
                }
            }
        }


        protected virtual void ExportDeletedItem(TObject item, string folder, HandlerSettings config)
        {
            if (item == null) return;
            var filename = GetPath(folder, item, config.GuidNames, config.UseFlatStructure);

            var attempt = serializer.SerializeEmpty(item, SyncActionType.Delete, string.Empty);
            if (attempt.Success)
                syncFileService.SaveXElement(attempt.Item, filename);
        }

        /// <summary>
        ///  cleans up the folder, so if someone renames a things
        ///  (and we are using the name in the file) this will
        ///  clean anything else in the folder that has that key
        /// </summary>
        protected virtual void CleanUp(TObject item, string newFile, string folder)
        {
            var physicalFile = syncFileService.GetAbsPath(newFile);

            var files = syncFileService.GetFiles(folder, "*.config");

            foreach (string file in files)
            {
                if (!file.InvariantEquals(physicalFile))
                {
                    var node = syncFileService.LoadXElement(file);
                    if (node.GetKey() == GetItemKey(item))
                    {
                        var attempt = serializer.SerializeEmpty(item, SyncActionType.Rename, node.GetAlias());
                        if (attempt.Success)
                        {
                            syncFileService.SaveXElement(attempt.Item, file);
                        }
                    }
                }
            }

            var folders = syncFileService.GetDirectories(folder);
            foreach (var children in folders)
            {
                CleanUp(item, newFile, children);
            }
        }

        #endregion

        protected override Guid GetItemKey(TObject item) => item.Key;




        #region ISyncHandler2 Methods 

        public SyncAttempt<XElement> GetElement(Udi udi)
        {
            if (udi is GuidUdi guidUdi)
            {
                var element = this.GetFromService(guidUdi.Guid);
                if (element == null)
                {
                    var entity = entityService.Get(guidUdi.Guid);
                    if (entity != null)
                        element = GetFromService(entity.Id);
                }

                if (element != null)
                    return this.serializer.Serialize(element);
            }

            return SyncAttempt<XElement>.Fail(udi.ToString(), ChangeType.Fail);
        }
        protected override IEnumerable<uSyncDependency> GetContainerDependencies(int id, DependencyFlags flags)
        {
            if (dependencyChecker == null) return Enumerable.Empty<uSyncDependency>();

            var dependencies = new List<uSyncDependency>();

            var containers = GetFolders(id);
            if (containers != null && containers.Any())
            {
                foreach (var container in containers)
                {
                    dependencies.AddRange(GetContainerDependencies(container.Id, flags));
                }
            }

            var children = GetChildItems(id);
            if (children != null && children.Any())
            {
                foreach (var child in children)
                {
                    var childItem = GetFromService(child.Id);
                    if (childItem != null)
                    {
                        dependencies.AddRange(dependencyChecker.GetDependencies(childItem, flags));
                    }
                }
            }

            return dependencies.DistinctBy(x => x.Udi.ToString()).OrderByDescending(x => x.Order);
        }
        #endregion

    }
}
