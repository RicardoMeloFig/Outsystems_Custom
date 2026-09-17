using ModelAPITest.ToggleElements;
using OutSystems.Model;
using OutSystems.Model.UI.Web;
using OutSystems.Model.UI.Web.Widgets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ModelAPITest
{
    class BlockTraditional : BlockGeneric<IWebBlock, IWebBlockInstanceWidget, IWebScreen, IPlaceholderContentWidget>
    {
        protected override IKey GetObjectKey(IWebBlockInstanceWidget s) {
            return s.SourceBlock.ObjectKey;
        }

        protected override string GetName(IWebBlockInstanceWidget o) {
            return o.SourceBlock.Name;
        }

        protected override void EncapsulatedInIf(IPlaceholderContentWidget p, IWebBlockInstanceWidget o, IESpace eSpace, String feature)
        {
            ToggleManager manager = new ToggleManager();
            manager.GetToggleValueRetrievalAction(eSpace);
            var instanceIf = p.CreateWidget<IIfWidget>();
            instanceIf.SetCondition(manager.GetToggleValueRetrievalActionString(eSpace.Name, feature));
            instanceIf.Name = manager.GetIfWidgetName(feature, GetName(o));
            instanceIf.TrueBranch.Copy(o);
        }
    }
}
