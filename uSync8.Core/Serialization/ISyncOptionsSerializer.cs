using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Umbraco.Core.Models.Entities;
using uSync8.Core.Models;

namespace uSync8.Core.Serialization
{
    /// <summary>
    ///  Serializer that can take SyncSerializer options, to control the serialization
    /// </summary>
    public interface ISyncOptionsSerializer<TObject> : ISyncSerializer<TObject>
        where TObject : IEntity
    {
        /// <summary>
        ///  Serialize an item into xml format
        /// </summary>
        /// <param name="item">Item to serialize</param>
        /// <param name="options">Options to change how serialization works</param>
        /// <returns>XML representation of item</returns>
        SyncAttempt<XElement> Serialize(TObject item, SyncSerializerOptions options);

        SyncAttempt<TObject> Deserialize(XElement node, SyncSerializerOptions options);

        SyncAttempt<TObject> DeserializeSecondPass(TObject item, XElement node, SyncSerializerOptions options);

        ChangeType IsCurrent(XElement node, SyncSerializerOptions options);
    }


    public class SyncSerializerOptions
    {
        /// <summary>
        ///  only add the item if it doesn't already exist
        /// </summary>
        public bool CreateOnly { get; set; }

        public SerializerFlags Flags { get; set; }

        /// <summary>
        ///  parameterized options
        /// </summary>
        public IDictionary<string, string> Settings { get; set; }
    }
}

