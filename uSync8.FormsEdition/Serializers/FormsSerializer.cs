using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using uSync8.Core.Serialization;
using Umbraco.Forms.Core.Models;
using uSync8.Core.Models;
using System.Xml.Linq;
using uSync8.Core;
using Umbraco.Forms.Core.Services;
using Umbraco.Core;
using Newtonsoft.Json;
using uSync8.Core.Extensions;
using Umbraco.Forms.Data.Storage;
using Umbraco.Core.Logging;
using Umbraco.Forms.Data.FileSystem;
using Umbraco.Forms.Core.Data.Storage;
using System.IO;

namespace uSync8.FormsEdition.Serializers
{
    [SyncSerializer("686C2435-BCC3-48C6-8021-8F9A081F594E", "Form Serializer", "UmbracoForm")]
    public class FormsSerializer : SyncSerializerRoot<Form>, ISyncSerializer<Form>
    {
        private readonly IFormService formService;
        private readonly IFormStorage formStorage;

        public FormsSerializer(ILogger logger, IFormService formService,
            IFormStorage formStorage) : base(logger)
        {
            this.formService = formService;
            this.formStorage = formStorage;



        }

        protected override SyncAttempt<Form> DeserializeCore(XElement node)
        {
            var data = node.Element("Data").ValueOrDefault(string.Empty);
            if (string.IsNullOrWhiteSpace(data))
                return SyncAttempt<Form>.Fail(node.GetAlias(), ChangeType.Fail, new Exception("No Form data in file"));

            var form = JsonConvert.DeserializeObject<Form>(data);
            if (form.Id != node.GetKey())
            {
                return SyncAttempt<Form>.Fail(node.GetAlias(), ChangeType.Fail,
                    new Exception("Key in Form data does not match key in file"));
            }

            var existing = FindItem(form.Id);
            if (existing != null)
            {
                formStorage.InsertForm(form);
            }
            else
            {
                formStorage.UpdateForm(form);
            }

            return SyncAttempt<Form>.Succeed(form.Name, form, ChangeType.Import);
        }

        public Form FindItem(XElement node)
        {
            var (key, alias) = FindKeyAndAlias(node);

            if (key != Guid.Empty)
            {
                var item = formService.GetForm(key);
                if (item != null) return item;
            }

            if (!string.IsNullOrEmpty(alias))
            {
                return formService.GetAllForms()
                    .Where(x => ItemAlias(x).InvariantEquals(alias))
                    .FirstOrDefault();
            }

            return null;

        }

        public override ChangeType IsCurrent(XElement node)
        {
            if (node == null) return ChangeType.Update;

            if (!IsValidOrEmpty(node)) throw new FormatException($"Invalid Xml File {node.Name.LocalName}");

            var item = FindItem(node);
            if (item == null)
            {
                if (IsEmpty(node))
                {
                    // we tell people it's a clean.
                    if (node.GetEmptyAction() == SyncActionType.Clean) return ChangeType.Clean;

                    // at this point its possible the file is for a rename or delete that has already happened
                    return ChangeType.NoChange;
                }
                else
                {
                    return ChangeType.Create;
                }
            }

            if (IsEmpty(node)) return CalculateEmptyChange(node, item);

            var newHash = MakeHash(node);

            var currentNode = Serialize(item);
            if (!currentNode.Success) return ChangeType.Create;

            var currentHash = MakeHash(currentNode.Item);
            if (string.IsNullOrEmpty(currentHash)) return ChangeType.Update;

            return currentHash == newHash ? ChangeType.NoChange : ChangeType.Update;
        }

        public void Save(IEnumerable<Form> items)
        {
            foreach (var item in items)
            {
                formStorage.SaveToFile(item, item.Id);
            }
        }

        public SyncAttempt<XElement> Serialize(Form item)
        {
            var node = new XElement(ItemType,
                new XAttribute("Key", item.Id),
                new XAttribute("Alias", ItemAlias(item)));

            var json = JsonConvert.SerializeObject(item, Formatting.Indented);
            if (json != null)
            {
                node.Add(new XElement("Data", new XCData(json)));
            }

            return SyncAttempt<XElement>.Succeed(item.Name, node, ChangeType.Export);

        }

        public SyncAttempt<XElement> SerializeEmpty(Form item, SyncActionType change, string alias)
        {
            if (string.IsNullOrEmpty(alias))
                alias = ItemAlias(item);

            return SerializeEmpty(item.Id, change, alias);
        }

        protected override string ItemAlias(Form item)
            => item.Name.ToSafeAlias();

        protected override Form FindItem(Guid key)
        {
            try
            {
                return formService.GetForm(key);
            }
            catch (FileNotFoundException ex)
            {
                return null;
            }
        }

        protected override Form FindItem(string alias)
            => formService.GetAllForms()
                .FirstOrDefault(x => ItemAlias(x).InvariantEquals(alias));

        protected override void SaveItem(Form item)
            => formStorage.SaveToFile(item, item.Id);

        protected override void DeleteItem(Form item)
            => formStorage.DeleteFile(item.Id);

        protected override SyncAttempt<XElement> SerializeCore(Form item)
        {
            return SyncAttempt<XElement>.Succeed(item.Name, new XElement("core"), ChangeType.NoChange);
        }

        protected override SyncAttempt<Form> ProcessDelete(Guid key, string alias, SerializerFlags flags)
        {
            formStorage.DeleteFile(key);
            return SyncAttempt<Form>.Succeed(alias, ChangeType.Delete);
        }

        protected override SyncAttempt<Form> ProcessRename(Guid key, string alias, SerializerFlags flags)
        {
            return SyncAttempt<Form>.Succeed(alias, ChangeType.NoChange);
        }
    }
}
