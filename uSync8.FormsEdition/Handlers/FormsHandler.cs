using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Umbraco.Core;
using Umbraco.Core.Logging;
using Umbraco.Forms.Core.Data.Storage;
using Umbraco.Forms.Core.Models;
using Umbraco.Forms.Core.Services;
using Umbraco.Forms.Data.Storage;
using uSync8.BackOffice;
using uSync8.BackOffice.Configuration;
using uSync8.BackOffice.Services;
using uSync8.BackOffice.SyncHandlers;
using uSync8.Core;
using uSync8.Core.Dependency;
using uSync8.Core.Models;
using uSync8.Core.Serialization;
using uSync8.Core.Tracking;

namespace uSync8.FormsEdition.Handlers
{
    [SyncHandler("formsHandler", "Forms", "Forms",
        uSyncBackOfficeConstants.Priorites.USYNC_RESERVED_UPPER,
        Icon = "icon-umb-contour usync-addon-icon", EntityType = "Form")]
    public class FormsHandler : SyncHandlerRoot<Form>, ISyncHandler, ISyncExtendedHandler
    {
        public override string Group => "Forms";

        private IFormService formService;
        private IFormStorage formStorage;

        public FormsHandler(
            IProfilingLogger logger, 
            ISyncSerializer<Form> serializer, 
            ISyncTracker<Form> tracker, 
            ISyncDependencyChecker<Form> checker, 
            SyncFileService syncFileService,
            IFormService formService,
            IFormStorage formStorage) 
            : base(logger, serializer, tracker, checker, syncFileService)
        {
            this.formService = formService;
            this.formStorage = formStorage;
        }

        public override IEnumerable<uSyncAction> ExportAll(int parent, string folder, HandlerSettings config, SyncUpdateCallback callback)
        {
            var actions = new List<uSyncAction>();

            var allforms = formService.GetAllForms().ToList();

            foreach(var form in allforms.Select((Value, Index) => new { Value, Index}))
            {
                callback?.Invoke(GetItemName(form.Value), form.Index, allforms.Count);
                actions.AddRange(Export(form.Value, folder, config));
            }

            return actions;
        }

        public SyncAttempt<XElement> GetElement(Udi udi)
        {
            // forms don't have a UDI ?
            return SyncAttempt<XElement>.Fail("Forms", Core.ChangeType.Fail);
        }

        protected override IEnumerable<uSyncAction> DeleteMissingItems(Form parent, IEnumerable<Guid> keys, bool reportOnly)
        {
            var allforms = formService.GetAllForms().ToList();
            var actions = new List<uSyncAction>();

            if (parent == null)
            {

                foreach (var item in allforms)
                {
                    if (!keys.Contains(item.Id))
                    {
                        if (!reportOnly)
                            DeleteViaService(item);

                        actions.Add(uSyncActionHelper<Form>.SetAction(SyncAttempt<Form>.Succeed(item.Name, ChangeType.Delete), string.Empty));
                    }
                }
            }

            return actions;
        }

        protected override void DeleteViaService(Form item)
            => formStorage.DeleteFile(item.Id);

        protected override IEnumerable<uSyncDependency> GetContainerDependencies(int id, DependencyFlags flags)
            => Enumerable.Empty<uSyncDependency>();

        protected override Form GetFromService(int id)
            => null;

        protected override Form GetFromService(Guid key)
            => formService.GetForm(key);

        protected override Form GetFromService(string alias)
            => formService.GetAllForms()
            .FirstOrDefault(x => x.Name.ToSafeAlias().InvariantEquals(alias));

        protected override int GetItemId(Form item)
            => 0;

        protected override Guid GetItemKey(Form item)
            => item.Id;

        protected override string GetItemName(Form item)
            => item.Name;

        protected override string GetItemPath(Form item, bool useGuid, bool isFlat)
        {
            return item.Id.ToString();
        }

        protected override void InitializeEvents(HandlerSettings settings)
        {
            // No events, so will only work on proper exports :( 
            formStorage.Saved += FormStorage_Saved;
            formStorage.Deleted += FormStorage_Deleted;
        }

        private void FormStorage_Deleted(object sender, Umbraco.Forms.Core.FormEventArgs e)
        {
            
        }

        private void FormStorage_Saved(object sender, Umbraco.Forms.Core.FormEventArgs e)
        {

           if (uSync8BackOffice.eventsPaused) return;

            try
            {
                this.Export(e.Form, Path.Combine(rootFolder, this.DefaultFolder), DefaultConfig);
            }
            catch(Exception ex)
            {
                // exception.
                logger.Error<FormsHandler>(ex);
            }
        }
    }
}
