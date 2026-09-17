using ModelAPITest.ToggleElements;
using OutSystems.Model;
using OutSystems.Model.Logic.Nodes;
using OutSystems.Model.UI;
using OutSystems.Model.UI.Web;
using OutSystems.Model.UI.Web.Widgets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ModelAPITest
{
    class ScreenTraditional : ScreenGeneric<ILinkWidget, IWebScreen, IPlaceholderContentWidget, IContainerWidget>
    {
        protected override IObjectSignature GetDestination(ILinkWidget l)
        {
            return l.OnClick.Destination;
        }

        protected override void EncapsulatedInIf(IPlaceholderContentWidget p, ILinkWidget l, IESpace espace, String feature)
        {
            ToggleManager manager = new ToggleManager();
            var instanceIf = p.CreateWidget<OutSystems.Model.UI.Web.Widgets.IIfWidget>();
            instanceIf.SetCondition(manager.GetToggleValueRetrievalActionString(espace.Name,feature));
            instanceIf.Name = manager.GetIfWidgetName(feature,GetDestinationName(l));
            instanceIf.TrueBranch.Copy(l);
            l.Delete();
        }

        protected override void EncapsulatedInIf2(IContainerWidget p, ILinkWidget l, IESpace espace, String feature)
        {
            ToggleManager manager = new ToggleManager();
            var instanceIf = p.CreateWidget<OutSystems.Model.UI.Web.Widgets.IIfWidget>();
            instanceIf.SetCondition(manager.GetToggleValueRetrievalActionString(espace.Name, feature));
            instanceIf.Name = manager.GetIfWidgetName(feature, GetDestinationName(l));
            instanceIf.TrueBranch.Copy(l);
            l.Delete();
        }

        protected override void CreateScreenPrep(IESpace espace, List<IKey> screenskeys, String feature)
        {
            ToggleManager manager = new ToggleManager();
            var screens = espace.GetAllDescendantsOfType<IWebScreen>().Where(s => screenskeys.Contains(s.ObjectKey));
            foreach (IWebScreen sc in screens)
            {
                if (feature == "defaultfeature")
                {
                    feature = sc.Name;
                }
                var rec = manager.CreateToggleRecord(manager.GetToggleKey(espace.Name,feature), manager.GetToggleName(feature), espace);
                var preparation = sc.CreatePreparation();
                var start = preparation.CreateNode<IStartNode>();
                var ifToggle = preparation.CreateNode<IIfNode>().Below(start);
                ifToggle.SetCondition(manager.GetToggleValueRetrievalActionString(espace.Name, feature));
                start.Target = ifToggle;
                var excep = preparation.CreateNode<IRaiseExceptionNode>().ToTheRightOf(ifToggle);
                excep.SetExceptionMessage("\"Screen not available\"");
                excep.Exception = espace.GetAllDescendantsOfType<OutSystems.Model.Logic.IException>().Single(sr => sr.ToString().Contains("Abort Activity Change Exception"));
                ifToggle.FalseTarget = excep;
                var end = preparation.CreateNode<IEndNode>().Below(ifToggle);
                ifToggle.TrueTarget = end;
            }
        }


        protected override IEnumerable<ILinkWidget> InsertIfplus(IESpace espace, List<IKey> keys, IEnumerable<ILinkWidget> links)
        {
            var links2 = espace.GetAllDescendantsOfType<ILinkWidget>().Where(s => s.OnClick.Destination is IGoToDestination);
            foreach (ILinkWidget link in links2)
            {
                var dest = (IGoToDestination)link.OnClick.Destination;
                if (keys.Contains(dest.Destination.ObjectKey))
                {
                    links = links.Append(link);
                }
            }
            return links;
        }

        protected override String GetDestinationName(ILinkWidget l)
        {
            return l.OnClick.Destination.ToString().Split(" (", 2)[0];
        }
    }
}
