using System;
using System.Collections.Generic;
using System.Xml.Linq;

using Umbraco.Core.Composing;
using Umbraco.Core.Logging;
using Umbraco.Core.Models.Entities;
using Umbraco.Core.Services;

using uSync8.Core.Extensions;
using uSync8.Core.Models;

namespace uSync8.Core.Serialization
{

    public abstract class SyncSerializerBase<TObject> : SyncSerializerRoot<TObject>,  IDiscoverable
        where TObject : IEntity
    {
        protected readonly IEntityService entityService;

        protected SyncSerializerBase(
            IEntityService entityService, ILogger logger) : base(logger)
        {
            // base services 
            this.entityService = entityService;

        }

        public SyncAttempt<XElement> Serialize(TObject item)
        {
            return SerializeCore(item);
        }
     

        /// <summary>
        ///  all xml items now have the same top line, this makes 
        ///  it eaiser for use to do lookups, get things like
        ///  keys and aliases for the basic checkers etc, 
        ///  makes the code simpler.
        /// </summary>
        /// <param name="item">Item we care about</param>
        /// <param name="alias">Alias we want to use</param>
        /// <param name="level">Level</param>
        /// <returns></returns>
        protected virtual XElement InitializeBaseNode(TObject item, string alias, int level = 0)
            => new XElement(ItemType,
                new XAttribute("Key", item.Key.ToString().ToLower()),
                new XAttribute("Alias", alias),
                new XAttribute("Level", level));


        protected override SyncAttempt<TObject> ProcessDelete(Guid key, string alias, SerializerFlags flags)
        {
            var item = this.FindItem(key);
            if (item == null && !string.IsNullOrWhiteSpace(alias))
            {
                // we need to build in some awareness of alias matching in the folder
                // because if someone deletes something in one place and creates it 
                // somewhere else the alias will exist, so we don't want to delete 
                // it from over there - this needs to be done at save time 
                // (bascially if a create happens) - turn any delete files into renames
                item = this.FindItem(alias);
            }

            if (item != null)
            {
                DeleteItem(item);
                return SyncAttempt<TObject>.Succeed(alias, ChangeType.Delete);
            }

            return SyncAttempt<TObject>.Succeed(alias, ChangeType.NoChange);
        }

        protected override SyncAttempt<TObject> ProcessRename(Guid key, string alias, SerializerFlags flags)
        {
            return SyncAttempt<TObject>.Succeed(alias, ChangeType.NoChange);
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

        public virtual SyncAttempt<XElement> SerializeEmpty(TObject item, SyncActionType change, string alias)
        {
            if (string.IsNullOrEmpty(alias))
                alias = ItemAlias(item);

            return SerializeEmpty(item.Key, change, alias);
        }


        #region Finders 
        // Finders - used on importing, getting things that are already there (or maybe not)

        /// <summary>
        ///  for bulk saving, some services do this, it causes less cache hits and 
        ///  so should be faster. 
        /// </summary>
        public virtual void Save(IEnumerable<TObject> items)
        {
            foreach(var item in items)
            {
                this.SaveItem(item);
            }
        }

        public virtual TObject FindItem(XElement node)
        {
            var (key, alias) = FindKeyAndAlias(node);

            logger.Debug<TObject>("Base: Find Item {0} [{1}]", key, alias);

            if (key != Guid.Empty)
            {
                var item = FindItem(key);
                if (item != null) return item;
            }

            if (!string.IsNullOrWhiteSpace(alias))
            {
                logger.Debug<TObject>("Base: Lookup by Alias: {0}", alias);
                return FindItem(alias);
            }

            return default(TObject);
        }
        #endregion

    }
}
