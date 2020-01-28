using System;
using System.IO;
using System.Security.Cryptography;
using System.Xml.Linq;

using Umbraco.Core;
using Umbraco.Core.Logging;

using uSync8.Core.Extensions;
using uSync8.Core.Models;

namespace uSync8.Core.Serialization
{
    /// <summary>
    ///  a class beneath the base class!
    /// </summary>
    /// <remarks>
    /// <para>
    ///   We did this because we needed to refactor some of the base
    ///   class methods out of a class that required the object be IEntity
    ///   (looking at you Umbraco Forms.
    ///  </para>
    ///  <para>
    ///   We didn't want to rename the classes, as that is a major release increment
    ///  </para>
    ///  <para>
    ///   This way none of the existing serializers need to change anything, the 
    ///   base now inherits the root. 
    ///  </para>
    /// </remarks>
    public abstract class SyncSerializerRoot<TObject>
    {
        protected readonly ILogger logger;
        public Guid Id { get; private set; }
        public string Name { get; private set; }
        public Type objectType => typeof(TObject);
        public bool IsTwoPass { get; private set; }


        public SyncSerializerRoot(ILogger logger)
        {
            this.logger = logger;

            // read the attribute
            var thisType = GetType();
            var meta = thisType.GetCustomAttribute<SyncSerializerAttribute>(false);
            if (meta == null)
                throw new InvalidOperationException($"the uSyncSerializer {thisType} requires a {typeof(SyncSerializerAttribute)}");

            Name = meta.Name;
            Id = meta.Id;
            ItemType = meta.ItemType;

            IsTwoPass = meta.IsTwoPass;

        }

        public string ItemType { get; set; }

        public abstract ChangeType IsCurrent(XElement node);

        protected string MakeHash(XElement node)
        {
            if (node == null) return string.Empty;
            node = CleanseNode(node);

            using (MemoryStream s = new MemoryStream())
            {
                node.Save(s);
                s.Position = 0;
                using (var md5 = MD5.Create())
                {
                    return BitConverter.ToString(
                        md5.ComputeHash(s)).Replace("-", "").ToLower();
                }
            }
        }

        protected SyncAttempt<XElement> SerializeEmpty(Guid key, SyncActionType change, string alias)
        {
            var node = XElementExtensions.MakeEmpty(key, change, alias);
            return SyncAttempt<XElement>.Succeed("Empty", node, ChangeType.Removed);

        }

        protected abstract SyncAttempt<XElement> SerializeCore(TObject item);
        public SyncAttempt<TObject> Deserialize(XElement node, SerializerFlags flags)
        {
            if (IsEmpty(node))
            {
                // new behavior when a node is 'empty' that is a marker for a delete or rename
                // so we process that action here, no more action file/folders
                return ProcessAction(node, flags);
            }

            if (!IsValid(node))
                throw new FormatException($"XML Not valid for type {ItemType}");


            if (flags.HasFlag(SerializerFlags.Force) || IsCurrent(node) > ChangeType.NoChange)
            {
                logger.Debug<TObject>("Base: Deserializing");
                var result = DeserializeCore(node);

                if (result.Success)
                {
                    if (!flags.HasFlag(SerializerFlags.DoNotSave))
                    {
                        // save 
                        SaveItem(result.Item);
                    }

                    if (flags.HasFlag(SerializerFlags.OnePass))
                    {
                        logger.Debug<TObject>("Base: Second Pass");
                        var secondAttempt = DeserializeSecondPass(result.Item, node, flags);
                        if (secondAttempt.Success)
                        {
                            // the secondPass is responsible for saving the item. 
                            /*
                            if (!flags.HasFlag(SerializerFlags.DoNotSave))
                            {
                                // save (again)
                                SaveItem(secondAttempt.Item);
                            }
                            */
                        }
                    }
                }

                return result;
            }

            return SyncAttempt<TObject>.Succeed(node.GetAlias(), default(TObject), ChangeType.NoChange);
        }

        public virtual SyncAttempt<TObject> DeserializeSecondPass(TObject item, XElement node, SerializerFlags flags)
        {
            return SyncAttempt<TObject>.Succeed(nameof(item), item, typeof(TObject), ChangeType.NoChange);
        }

        protected abstract SyncAttempt<TObject> DeserializeCore(XElement node);


        protected SyncAttempt<TObject> ProcessAction(XElement node, SerializerFlags flags)
        {
            if (!IsEmpty(node))
                throw new ArgumentException("Cannot process actions on a non-empty node");

            var actionType = node.Attribute("Change").ValueOrDefault<SyncActionType>(SyncActionType.None);

            var (key, alias) = FindKeyAndAlias(node);

            switch (actionType)
            {
                case SyncActionType.Delete:
                    return ProcessDelete(key, alias, flags);
                case SyncActionType.Rename:
                    return ProcessRename(key, alias, flags);
                case SyncActionType.Clean:
                    // we return a 'clean' success, but this is then picked up 
                    // in the handler, as something to clean, so the handler does it. 
                    return SyncAttempt<TObject>.Succeed(alias, ChangeType.Clean);
                default:
                    return SyncAttempt<TObject>.Succeed(alias, ChangeType.NoChange);
            }
        }

        protected abstract SyncAttempt<TObject> ProcessDelete(Guid key, string alias, SerializerFlags flags);
        protected abstract SyncAttempt<TObject> ProcessRename(Guid key, string alias, SerializerFlags flags);

        /// <summary>
        ///  is this a bit of valid xml 
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        public virtual bool IsValid(XElement node)
            => node.Name.LocalName == this.ItemType
                && node.GetKey() != Guid.Empty
                && node.GetAlias() != string.Empty;

        public bool IsEmpty(XElement node)
            => node.Name.LocalName == uSyncConstants.Serialization.Empty;

        public bool IsValidOrEmpty(XElement node)
            => IsEmpty(node) || IsValid(node);

        /// <summary>
        ///  cleans up the node, removing things that are not generic (like internal Ids)
        ///  so that the comparisions are like for like.
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        protected virtual XElement CleanseNode(XElement node) => node;

        protected (Guid key, string alias) FindKeyAndAlias(XElement node)
        {
            if (IsValidOrEmpty(node))
                return (
                        key: node.Attribute("Key").ValueOrDefault(Guid.Empty),
                        alias: node.Attribute("Alias").ValueOrDefault(string.Empty)
                       );

            return (key: Guid.Empty, alias: string.Empty);
        }
      
        protected ChangeType CalculateEmptyChange(XElement node, TObject item)
        {
            // this shouldn't happen, but check.
            if (item == null) return ChangeType.NoChange;

            // simple logic, if it's a delete we say so, 
            // renames are picked up by the check on the new file

            switch (node.GetEmptyAction())
            {
                case SyncActionType.Delete:
                    return ChangeType.Delete;
                case SyncActionType.Clean:
                    return ChangeType.Clean;
                default:
                    return ChangeType.NoChange;
            }
        }

        /// abstract worker functions 

        protected abstract TObject FindItem(Guid key);
        protected abstract TObject FindItem(string alias);
        protected abstract void SaveItem(TObject item);
        protected abstract void DeleteItem(TObject item);
        protected abstract string ItemAlias(TObject item);

    }
}
