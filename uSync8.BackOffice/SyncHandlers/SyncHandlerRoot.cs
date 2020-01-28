using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Umbraco.Core;
using Umbraco.Core.Composing;
using Umbraco.Core.Logging;
using Umbraco.Core.Models;
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
    public abstract class SyncHandlerRoot<TObject>
    {
        protected readonly IProfilingLogger logger;
        protected readonly ISyncSerializer<TObject> serializer;
        protected readonly ISyncTracker<TObject> tracker;
        protected readonly ISyncDependencyChecker<TObject> dependencyChecker;
        protected readonly SyncFileService syncFileService;

        public string Alias { get; private set; }
        public string Name { get; private set; }
        public string DefaultFolder { get; private set; }
        public int Priority { get; private set; }
        public string Icon { get; private set; }

        protected bool IsTwoPass = false;

        public Type ItemType { get; protected set; } = typeof(TObject);

        /// settings can be loaded for these.
        public bool Enabled { get; set; } = true;
        public HandlerSettings DefaultConfig { get; set; }

        protected string rootFolder { get; set; }

        public string EntityType { get; protected set; }

        public string TypeName { get; protected set; }

        // handler things 
        // we calculate these now based on the entityType ? 
        protected UmbracoObjectTypes itemObjectType { get; set; } = UmbracoObjectTypes.Unknown;

        protected UmbracoObjectTypes itemContainerType = UmbracoObjectTypes.Unknown;


        public SyncHandlerRoot(
            IProfilingLogger logger,
            ISyncSerializer<TObject> serializer,
            ISyncTracker<TObject> tracker,
            ISyncDependencyChecker<TObject> checker,
            SyncFileService syncFileService)
        {

            this.logger = logger;
            this.serializer = serializer;
            this.tracker = tracker;
            this.dependencyChecker = checker;
            this.syncFileService = syncFileService;

            var thisType = GetType();
            var meta = thisType.GetCustomAttribute<SyncHandlerAttribute>(false);
            if (meta == null)
                throw new InvalidOperationException($"The Handler {thisType} requires a {typeof(SyncHandlerAttribute)}");

            Name = meta.Name;
            Alias = meta.Alias;
            DefaultFolder = meta.Folder;
            Priority = meta.Priority;
            IsTwoPass = meta.IsTwoPass;
            Icon = string.IsNullOrWhiteSpace(meta.Icon) ? "icon-umb-content" : meta.Icon;
            EntityType = meta.EntityType;

            TypeName = serializer.ItemType;

            this.itemObjectType = uSyncObjectType.ToUmbracoObjectType(EntityType);
            this.itemContainerType = uSyncObjectType.ToContainerUmbracoObjectType(EntityType);

            GetDefaultConfig(Current.Configs.uSync());
            uSyncConfig.Reloaded += BackOfficeConfig_Reloaded;

        }

        private void GetDefaultConfig(uSyncSettings setting)
        {
            var config = setting.DefaultHandlerSet().Handlers.Where(x => x.Alias.InvariantEquals(this.Alias))
                .FirstOrDefault();

            if (config != null)
                this.DefaultConfig = config;
            else
            {
                // handler isn't in the config, but need one ?
                this.DefaultConfig = new HandlerSettings(this.Alias, false)
                {
                    GuidNames = new OverriddenValue<bool>(setting.UseGuidNames, false),
                    UseFlatStructure = new OverriddenValue<bool>(setting.UseFlatStructure, false),
                };
            }

            rootFolder = setting.RootFolder;
        }

        private void BackOfficeConfig_Reloaded(uSyncSettings settings)
        {
            GetDefaultConfig(settings);
        }

        #region Import 

        public virtual SyncAttempt<TObject> Import(string filePath, HandlerSettings config, SerializerFlags flags)
        {
            try
            {
                syncFileService.EnsureFileExists(filePath);
                using (var stream = syncFileService.OpenRead(filePath))
                {
                    var node = XElement.Load(stream);
                    if (ShouldImport(node, config))
                    {
                        var attempt = serializer.Deserialize(node, flags);
                        return attempt;
                    }
                    else
                    {
                        return SyncAttempt<TObject>.Succeed(Path.GetFileName(filePath), default(TObject), ChangeType.NoChange, "Not Imported (Based on config)");
                    }
                }
            }
            catch (FileNotFoundException notFoundException)
            {
                return SyncAttempt<TObject>.Fail(Path.GetFileName(filePath), ChangeType.Fail, $"File not found {notFoundException.Message}");
            }
            catch (Exception ex)
            {
                return SyncAttempt<TObject>.Fail(Path.GetFileName(filePath), ChangeType.Fail, $"Import Fail: {ex.Message}");
            }
        }

        virtual public SyncAttempt<TObject> ImportSecondPass(string file, TObject item, HandlerSettings config, SyncUpdateCallback callback)
        {
            if (IsTwoPass)
            {
                try
                {
                    syncFileService.EnsureFileExists(file);

                    var flags = SerializerFlags.None;
                    if (config.BatchSave)
                        flags |= SerializerFlags.DoNotSave;

                    using (var stream = syncFileService.OpenRead(file))
                    {
                        var node = XElement.Load(stream);
                        var attempt = serializer.DeserializeSecondPass(item, node, flags);
                        stream.Dispose();
                        return attempt;
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn<TObject>($"Second Import Failed: {ex.Message}");
                    return SyncAttempt<TObject>.Fail(GetItemId(item).ToString(), ChangeType.Fail, ex);
                }
            }

            return SyncAttempt<TObject>.Succeed(GetItemId(item).ToString(), ChangeType.NoChange);
        }

        protected virtual IEnumerable<uSyncAction> ImportFolder(string folder, HandlerSettings config, Dictionary<string, TObject> updates, bool force, SyncUpdateCallback callback)
        {
            List<uSyncAction> actions = new List<uSyncAction>();
            var files = GetImportFiles(folder);

            var flags = SerializerFlags.None;
            if (force) flags |= SerializerFlags.Force;
            if (config.BatchSave) flags |= SerializerFlags.DoNotSave;

            var cleanMarkers = new List<string>();

            int count = 0;
            int total = files.Count();
            foreach (string file in files)
            {
                count++;

                callback?.Invoke($"Importing {Path.GetFileNameWithoutExtension(file)}", count, total);

                var attempt = Import(file, config, flags);
                if (attempt.Success)
                {
                    if (attempt.Change == ChangeType.Clean)
                    {
                        cleanMarkers.Add(file);
                    }
                    else if (attempt.Item != null)
                    {
                        updates.Add(file, attempt.Item);
                    }
                }

                var action = uSyncActionHelper<TObject>.SetAction(attempt, file, this.Alias, IsTwoPass);
                if (attempt.Details != null && attempt.Details.Any())
                    action.Details = attempt.Details;

                if (attempt.Change != ChangeType.Clean)
                    actions.Add(action);
            }

            // bulk save ..
            if (flags.HasFlag(SerializerFlags.DoNotSave) && updates.Any())
            {
                // callback?.Invoke($"Saving {updates.Count()} changes", 1, 1);
                serializer.Save(updates.Select(x => x.Value));
            }

            var folders = syncFileService.GetDirectories(folder);
            foreach (var children in folders)
            {
                actions.AddRange(ImportFolder(children, config, updates, force, callback));
            }

            if (actions.All(x => x.Success))
            {
                foreach (var cleanFile in cleanMarkers)
                {
                    actions.AddRange(CleanFolder(cleanFile, false));
                }
                // remove the actual cleans (they will have been replaced by the deletes
                actions.RemoveAll(x => x.Change == ChangeType.Clean);
            }

            // callback?.Invoke("", 1, 1);

            return actions;
        }

        public IEnumerable<uSyncAction> ImportAll(string folder, HandlerSettings config, bool force, SyncUpdateCallback callback = null)
        {
            logger.Info<uSync8BackOffice>("Running Import: {0}", Path.GetFileName(folder));

            var actions = new List<uSyncAction>();
            var updates = new Dictionary<string, TObject>();

            actions.AddRange(ImportFolder(folder, config, updates, force, callback));

            if (updates.Any())
            {
                ProcessSecondPasses(updates, config, callback);
            }

            callback?.Invoke("Done", 3, 3);
            return actions;
        }

        private void ProcessSecondPasses(IDictionary<string, TObject> updates, HandlerSettings config, SyncUpdateCallback callback = null)
        {
            List<TObject> updatedItems = new List<TObject>();
            foreach (var item in updates.Select((update, Index) => new { update, Index }))
            {
                callback?.Invoke($"Second Pass {Path.GetFileName(item.update.Key)}", item.Index, updates.Count);
                var attempt = ImportSecondPass(item.update.Key, item.update.Value, config, callback);
                if (attempt.Success && attempt.Change > ChangeType.NoChange)
                {
                    updatedItems.Add(attempt.Item);
                }
            }

            if (config.BatchSave)
            {
                callback?.Invoke($"Saving {updatedItems.Count} Second Pass Items", 2, 3);
                serializer.Save(updatedItems);
            }


        }


        /// <summary>
        ///  given a folder we calculate what items we can remove, becuase they are 
        ///  not in one the the files in the folder.
        /// </summary>
        /// <param name="cleanFile"></param>
        /// <returns></returns>
        protected virtual IEnumerable<uSyncAction> CleanFolder(string cleanFile, bool reportOnly)
        {
            var folder = Path.GetDirectoryName(cleanFile);

            var parent = GetCleanParent(cleanFile);
            if (parent == null) return Enumerable.Empty<uSyncAction>();

            // get the keys for every item in this folder. 

            // this would works on the flat folder stucture too, 
            // there we are being super defensive, so if an item
            // is anywhere in the folder it won't get removed
            // even if the folder is wrong
            // be a little slower (not much though)
            var keys = new List<Guid>();
            var files = syncFileService.GetFiles(folder, "*.config");
            foreach (var file in files)
            {
                var node = XElement.Load(file);
                var key = node.GetKey();
                if (!keys.Contains(key))
                    keys.Add(key);
            }

            return DeleteMissingItems(parent, keys, reportOnly);
        }

        protected abstract IEnumerable<uSyncAction> DeleteMissingItems(TObject parent, IEnumerable<Guid> keys, bool reportOnly);

        protected TObject GetCleanParent(string file)
        {
            var node = XElement.Load(file);
            var key = node.GetKey();
            if (key == Guid.Empty) return default;

            return GetFromService(key);
        }

        protected virtual IEnumerable<string> GetImportFiles(string folder)
            => syncFileService.GetFiles(folder, "*.config");

        #endregion

        #region export 

        virtual public IEnumerable<uSyncAction> ExportAll(string folder, HandlerSettings config, SyncUpdateCallback callback)
        {
            // we dont clean the folder out on an export all. 
            // because the actions (renames/deletes) live in the folder
            //
            // there will have to be a diffrent clean option
            ///
            // syncFileService.CleanFolder(folder);

            return ExportAll(-1, folder, config, callback);
        }

        abstract public IEnumerable<uSyncAction> ExportAll(int parent, string folder, HandlerSettings config, SyncUpdateCallback callback);

        virtual public IEnumerable<uSyncAction> Export(TObject item, string folder, HandlerSettings config)
        {
            if (item == null)
                return uSyncAction.Fail(nameof(item), typeof(TObject), ChangeType.Fail, "Item not set").AsEnumerableOfOne();

            var filename = GetPath(folder, item, config.GuidNames, config.UseFlatStructure);

            var attempt = serializer.Serialize(item);
            if (attempt.Success)
            {
                if (ShouldExport(attempt.Item, config))
                {
                    // only write the file to disk if it should be exported.
                    syncFileService.SaveXElement(attempt.Item, filename);
                }
                else
                {
                    return uSyncAction.SetAction(true, filename, type: typeof(TObject), change: ChangeType.NoChange, message: "Not Exported (Based on config)").AsEnumerableOfOne();
                }
            }

            return uSyncActionHelper<XElement>.SetAction(attempt, filename).AsEnumerableOfOne();
        }
        #endregion

        #region reporting 

        public IEnumerable<uSyncAction> Report(string folder, HandlerSettings config, SyncUpdateCallback callback)
        {
            var actions = new List<uSyncAction>();
            callback?.Invoke("Checking Actions", 0, 1);
            actions.AddRange(ReportFolder(folder, config, callback));
            callback?.Invoke("Done", 1, 1);
            return actions;
        }

        public virtual IEnumerable<uSyncAction> ReportFolder(string folder, HandlerSettings config, SyncUpdateCallback callback)
        {
            List<uSyncAction> actions = new List<uSyncAction>();

            var files = GetImportFiles(folder);

            int count = 0;
            int total = files.Count();
            foreach (string file in files)
            {
                count++;
                callback?.Invoke(Path.GetFileNameWithoutExtension(file), count, total);

                actions.AddRange(ReportItem(file, config));
            }

            foreach (var children in syncFileService.GetDirectories(folder))
            {
                actions.AddRange(ReportFolder(children, config, callback));
            }

            return actions;
        }

        public IEnumerable<uSyncAction> ReportElement(XElement node)
            => ReportElement(node, string.Empty, null);

        protected virtual IEnumerable<uSyncAction> ReportElement(XElement node, string filename, HandlerSettings config)
        {
            try
            {
                var actions = new List<uSyncAction>();

                var change = serializer.IsCurrent(node);
                var action = uSyncActionHelper<TObject>
                        .ReportAction(change, node.GetAlias(), !string.IsNullOrWhiteSpace(filename) ? filename : node.GetAlias(), node.GetKey(), this.Alias);

                action.Message = "";

                if (action.Change == ChangeType.Clean)
                {
                    actions.AddRange(CleanFolder(filename, true));
                }
                else if (action.Change > ChangeType.NoChange)
                {
                    action.Details = tracker.GetChanges(node);
                    if (action.Change != ChangeType.Create && (action.Details == null || action.Details.Count() == 0))
                    {
                        action.Message = "Change details not calculated";
                    }
                    else
                    {
                        action.Message = $"{action.Change.ToString()}";
                    }
                    actions.Add(action);
                }
                else
                {
                    actions.Add(action);
                }

                return actions;
            }
            catch (FormatException fex)
            {
                return uSyncActionHelper<TObject>
                    .ReportActionFail(Path.GetFileName(node.GetAlias()), $"format error {fex.Message}")
                    .AsEnumerableOfOne();
            }
        }

        protected IEnumerable<uSyncAction> ReportItem(string file, HandlerSettings config)
        {
            try
            {
                var node = syncFileService.LoadXElement(file);
                return ReportElement(node, file, config);
            }
            catch (Exception ex)
            {
                return uSyncActionHelper<TObject>
                    .ReportActionFail(Path.GetFileName(file), $"Reporing error {ex.Message}")
                    .AsEnumerableOfOne();
            }

        }

        #endregion

        #region events
        public void Initialize(HandlerSettings settings)
        {
            InitializeEvents(settings);
        }
        protected abstract void InitializeEvents(HandlerSettings settings);

        #endregion

        #region ISyncHandler2 Methods
        public virtual string Group { get; protected set; } = uSyncBackOfficeConstants.Groups.Settings;

        virtual public IEnumerable<uSyncAction> Import(string file, HandlerSettings config, bool force)
        {
            var flags = SerializerFlags.OnePass;
            if (force) flags |= SerializerFlags.Force;

            var attempt = Import(file, config, flags);
            return uSyncActionHelper<TObject>.SetAction(attempt, file, this.Alias, IsTwoPass)
                .AsEnumerableOfOne();
        }

        virtual public IEnumerable<uSyncAction> ImportElement(XElement node, bool force)
        {
            var flags = SerializerFlags.OnePass;
            if (force) flags |= SerializerFlags.Force;

            var attempt = serializer.Deserialize(node, flags);
            return uSyncActionHelper<TObject>.SetAction(attempt, node.GetAlias(), this.Alias, IsTwoPass)
                .AsEnumerableOfOne();
        }

        public IEnumerable<uSyncAction> Report(string file, HandlerSettings config)
            => ReportItem(file, config);


        public IEnumerable<uSyncAction> Export(int id, string folder, HandlerSettings settings)
        {
            var item = this.GetFromService(id);
            return this.Export(item, folder, settings);
        }

        public IEnumerable<uSyncAction> Export(Udi udi, string folder, HandlerSettings settings)
        {
            if (udi is GuidUdi guidUdi)
            {
                var item = this.GetFromService(guidUdi.Guid);
                if (item != null)
                    return Export(item, folder, settings);
            }

            return uSyncAction.Fail(nameof(udi), typeof(TObject), ChangeType.Fail, "Item not found")
                .AsEnumerableOfOne();
        }


        public IEnumerable<uSyncDependency> GetDependencies(Guid key, DependencyFlags flags)
        {
            var item = this.GetFromService(key);
            return GetDependencies(item, flags);
        }

        public IEnumerable<uSyncDependency> GetDependencies(int id, DependencyFlags flags)
        {
            var item = this.GetFromService(id);
            if (item == null) return GetContainerDependencies(id, flags);
            return GetDependencies(item, flags);
        }

        protected abstract IEnumerable<uSyncDependency> GetContainerDependencies(int id, DependencyFlags flags);

        protected IEnumerable<uSyncDependency> GetDependencies(TObject item, DependencyFlags flags)
        {
            if (dependencyChecker == null) return Enumerable.Empty<uSyncDependency>();
            if (item == null) return Enumerable.Empty<uSyncDependency>();

            return dependencyChecker.GetDependencies(item, flags);
        }


        #endregion

        /// checkers 

        /// <summary>
        ///  check to see if this element should be imported as part of the process.
        /// </summary>
        virtual protected bool ShouldImport(XElement node, HandlerSettings config) => true;

        /// <summary>
        ///  Check to see if this elment should be exported. 
        /// </summary>
        virtual protected bool ShouldExport(XElement node, HandlerSettings config) => true;

        /// finders and things 
        virtual protected string GetPath(string folder, TObject item, bool GuidNames, bool isFlat)
        {
            if (isFlat && GuidNames) return $"{folder}/{GetItemKey(item)}.config";
            var path = $"{folder}/{this.GetItemPath(item, GuidNames, isFlat)}.config";

            // if this is flat but not using guid filenames, then we check for clashes.
            if (isFlat && !GuidNames) return CheckAndFixFileClash(path, GetItemKey(item));
            return path;
        }
        /// 

        private string CheckAndFixFileClash(string path, Guid key)
        {
            if (syncFileService.FileExists(path))
            {
                var node = syncFileService.LoadXElement(path);
                if (node != null && node.GetKey() != key)
                {
                    // clash, we should append something
                    var append = key.ToShortKeyString(8); // (this is the shortened guid like media folders do)
                    return Path.Combine(Path.GetDirectoryName(path),
                        Path.GetFileNameWithoutExtension(path) + "_" + append + Path.GetExtension(path));
                }

            }
            return path;
        }


        abstract protected Guid GetItemKey(TObject item);

        virtual public uSyncAction Rename(TObject item) => new uSyncAction();


        /// implimentations 
        abstract protected TObject GetFromService(int id);
        abstract protected TObject GetFromService(Guid key);
        abstract protected TObject GetFromService(string alias);
        abstract protected void DeleteViaService(TObject item);

        abstract protected string GetItemPath(TObject item, bool useGuid, bool isFlat);
        abstract protected string GetItemName(TObject item);

        abstract protected int GetItemId(TObject item);

    }
}
