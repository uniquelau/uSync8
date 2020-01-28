using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Umbraco.Forms.Core.Models;
using uSync8.Core.Serialization;
using uSync8.Core.Tracking;

namespace uSync8.FormsEdition.Trackers
{
    public class FormTracker : SyncBaseTracker<Form> , ISyncTracker<Form>
    {
        public FormTracker(ISyncSerializer<Form> serializer) : base(serializer)
        { }

        protected override TrackedItem TrackChanges()
        {
            return new TrackedItem(serializer.ItemType, true)
            {
                Children = new List<TrackedItem>()
                {
                    new TrackedItem("Data", "/Data", true)
                }
            };
        }
    }
}
