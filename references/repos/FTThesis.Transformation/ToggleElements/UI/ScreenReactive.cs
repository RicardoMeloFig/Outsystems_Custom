using OutSystems.Model;
using OutSystems.Model.Logic.Nodes;
using OutSystems.Model.UI;
using OutSystems.Model.UI.Mobile;
using OutSystems.Model.UI.Mobile.Widgets;
using OutSystems.Model.UI.Mobile.Events;
using ServiceStudio.Plugin.NRWidgets;
using System;
using System.Collections.Generic;
using System.Linq;
using OutSystems.Model.Logic;
using OutSystems.Model.Types;
using ModelAPITest.ToggleElements;

namespace ModelAPITest
{

    class ScreenReactive : ScreenGeneric<ILink, IMobileScreen, IPlaceholderContentWidget, IContent>
    {
        private static String dataActionName = "GetToggles";
        private static String onAfterFetchName = "OnAfterFetch";

        protected override IObjectSignature GetDestination(ILink l)
        {
            return l.OnClick.Destination;
        }

        protected override void EncapsulatedInIf(IPlaceholderContentWidget p, ILink l, IESpace espace, String feature)
        {
            ToggleManager manager = new ToggleManager();
            var name = GetDestinationName(l);
            var screens = espace.GetAllDescendantsOfType<IMobileScreen>();
            foreach(IMobileScreen s in screens)
            {
                var exists = s.GetAllDescendantsOfType<ILink>().SingleOrDefault(k => k.ObjectKey.Equals(l.ObjectKey));
                if (exists != default)
                {
                    CreateDataActionScreen(espace, s, l, feature);
                }
            }

            var blocks = espace.GetAllDescendantsOfType<IMobileBlock>();
            foreach (IMobileBlock s in blocks)
            {
                var exists = s.GetAllDescendantsOfType<ILink>().SingleOrDefault(k => k.ObjectKey.Equals(l.ObjectKey));
                if (exists != default)
                {
                    CreateDataActionBlock(espace, s, l, feature);
                }
            }
            var instanceIf = p.CreateWidget<OutSystems.Model.UI.Mobile.Widgets.IIfWidget>();
            instanceIf.SetCondition($"{dataActionName}.IsDataFetched and {dataActionName}.FT_{feature}");
            instanceIf.Name = manager.GetIfWidgetName(feature,name);
            instanceIf.TrueBranch.Copy(l);
            l.Delete();
        }

        protected override void EncapsulatedInIf2(IContent p, ILink l, IESpace espace, String feature)
        {
            ToggleManager manager = new ToggleManager();
            var name = GetDestinationName(l);
            var screens = espace.GetAllDescendantsOfType<IMobileScreen>();
            foreach (IMobileScreen s in screens)
            {
                var exists = s.GetAllDescendantsOfType<ILink>().SingleOrDefault(k => k.ObjectKey.Equals(l.ObjectKey));
                if (exists != default)
                {
                    CreateDataActionScreen(espace, s, l, feature);
                }
            }
            var blocks = espace.GetAllDescendantsOfType<IMobileBlock>();
            foreach (IMobileBlock s in blocks)
            {
                var exists = s.GetAllDescendantsOfType<ILink>().SingleOrDefault(k => k.ObjectKey.Equals(l.ObjectKey));
                if (exists != default)
                {
                    CreateDataActionBlock(espace, s, l, feature);
                }
            }
            var instanceIf = p.CreateWidget<OutSystems.Model.UI.Mobile.Widgets.IIfWidget>();
            instanceIf.SetCondition($"{dataActionName}.IsDataFetched and {dataActionName}.FT_{feature}");
            instanceIf.Name = manager.GetIfWidgetName(feature, name);
            instanceIf.TrueBranch.Copy(l);
            l.Delete();
        }

        protected override void CreateScreenPrep(IESpace espace, List<IKey> screenskeys, String feature)
        {
            ToggleManager manager = new ToggleManager();

            var screens = espace.GetAllDescendantsOfType<IMobileScreen>().Where(s => screenskeys.Contains(s.ObjectKey));
            foreach (IMobileScreen sc in screens)
            {
                if (feature == "defaultfeature")
                {
                    feature = sc.Name;
                }
                var name = sc.Name;
                var rec = manager.CreateToggleRecord(manager.GetToggleKey(espace.Name,feature), manager.GetToggleName(feature), espace);
                var dataaction = CreateDataActionScreen(espace, sc, null, feature);
                var ongetdata = dataaction.GetAllDescendantsOfType<IUILifeCycleEvent>().Single(e => e.GetType().ToString().Contains(onAfterFetchName));
                var oninitaction = sc.CreateScreenAction();
                oninitaction.Name = onAfterFetchName;

                var start = oninitaction.CreateNode<IStartNode>();
                var ifToggle = oninitaction.CreateNode<IIfNode>().Below(start);
                ifToggle.SetCondition($"{dataActionName}.FT_{feature}");
                start.Target = ifToggle;
                var end = oninitaction.CreateNode<IEndNode>().Below(ifToggle);
                ifToggle.TrueTarget = end;
                var excep = CreateExceptionNode(oninitaction,espace).ToTheRightOf(ifToggle);
                ifToggle.FalseTarget = excep;

                ongetdata.Destination = oninitaction;
            }
        }

        private IDataAction CreateDataActionScreen(IESpace espace, IMobileScreen sc, ILink l, String feature)
        {
            var action = sc.GetAllDescendantsOfType<IDataAction>().SingleOrDefault(e => e.Name == dataActionName);
            if (action == default)
            {
                action = sc.CreateDataAction(dataActionName);
                ResetOutputParameters(action);
                CreateDataAction(espace, l, action, sc.Name, feature);
            }
            else
            {
                AddToDataAction(espace, l, action, sc.Name, feature);
            }
            return action;
        }

        private IDataAction CreateDataActionBlock(IESpace espace, IMobileBlock sc, ILink l, String feature)
        {
            var action = sc.GetAllDescendantsOfType<IDataAction>().SingleOrDefault(e => e.Name == dataActionName);

            if (action == default)
            {
                action = sc.CreateDataAction(dataActionName);
                ResetOutputParameters(action);
                CreateDataAction(espace, l, action, sc.Name, feature);
            }
            else
            {
                AddToDataAction(espace, l, action, sc.Name, feature);
            }
            return action;
        }

        private void CreateDataAction(IESpace espace, ILink l, IDataAction oninitaction, String sname, String feature)
        {
            ToggleManager manager = new ToggleManager();
            var destname = sname;
            if (l != null)
            {
                destname = GetDestinationName(l);
            }
            if (destname != feature)
            {
                destname = feature;
            }
            var toggleName = manager.GetToggleName(destname);

            var start = oninitaction.CreateNode<IStartNode>();
            var getToggle = CreateExecuteActionNode(espace,oninitaction,manager,destname).Below(start);
            start.Target = getToggle;
            CreateOutputParameter(espace, oninitaction, toggleName);
            var assignVar = AssignOutputValues(oninitaction,manager,destname).Below(getToggle);
            getToggle.Target = assignVar;
            var end = oninitaction.CreateNode<IEndNode>().Below(assignVar);
            assignVar.Target = end;
        }

        private void AddToDataAction(IESpace espace, ILink l, IDataAction action, String sname, String feature)
        {
            ToggleManager manager = new ToggleManager();
            var destname = sname;
            if (l != null)
            {
                destname = GetDestinationName(l);
            }
            if (destname != feature)
            {
                destname = feature;
            }
            var toggleName = manager.GetToggleName(destname);
            var actionName = manager.GetFeatureToggleIsOnActionString(destname);

            var start = action.GetAllDescendantsOfType<IStartNode>().Single();
            var startTarget = start.Target;
            var existsFeature = action.GetAllDescendantsOfType<IExecuteServerActionNode>().SingleOrDefault(s => s.Name == actionName);
            if (existsFeature == default)
            {
                var getToggle = CreateExecuteActionNode(espace, action, manager, destname).Below(start);
                CreateOutputParameter(espace, action, toggleName);
                var assign = CreateAssign(action,manager,destname,startTarget,getToggle).Below(getToggle);
                start.Target = getToggle;
            }
        }

        private IAssignNode CreateAssign(IDataAction action, ToggleManager manager, String destname, IActionNode startTarget, IExecuteServerActionNode getToggle)
        {
            var toggleName = manager.GetToggleName(destname);
            var outputName = manager.GetFeatureToggleIsOnOutputString(destname);
            var assign = action.GetAllDescendantsOfType<IAssignNode>().Single();
            if (assign != null)
            {
                assign.CreateAssignment(toggleName, outputName);
                getToggle.Target = startTarget;
            }
            else
            {
                assign = action.CreateNode<IAssignNode>();
                assign.CreateAssignment(toggleName, outputName);
                getToggle.Target = assign;
                assign.Target = startTarget;
            }
            return assign;
        }

        private IAssignNode AssignOutputValues(IDataAction action, ToggleManager manager, String destname)
        {
            var outputName = manager.GetFeatureToggleIsOnOutputString(destname);
            var toggleName = manager.GetToggleName(destname);
            var assignVar = action.CreateNode<IAssignNode>();
            assignVar.CreateAssignment(toggleName, outputName);
            return assignVar;
        }

        private IExecuteServerActionNode CreateExecuteActionNode(IESpace espace, IDataAction action, ToggleManager manager, String destname)
        {
            var getToggleAction = manager.GetPlatformToggleRetrievalAction(espace);
            var toggleRecord = manager.GetToggleRecord(espace.Name, destname);
            var actionName = manager.GetFeatureToggleIsOnActionString(destname);

            var getToggle = action.CreateNode<IExecuteServerActionNode>();
            getToggle.Action = getToggleAction;
            var keyParam = getToggleAction.InputParameters.Single(s => s.Name == "FeatureToggleKey");
            var modParam = getToggleAction.InputParameters.Single(s => s.Name == "ModuleName");
            getToggle.SetArgumentValue(keyParam, toggleRecord);
            getToggle.SetArgumentValue(modParam, "GetEntryEspaceName()");
            getToggle.Name = actionName;
            return getToggle;
        }

        private void CreateOutputParameter(IESpace espace, IDataAction action, String toggleName)
        {
            var outputparam = action.CreateOutputParameter(toggleName);
            outputparam.DataType = espace.BooleanType;
        }

        private void ResetOutputParameters(IDataAction action)
        {
            var out1 = action.GetAllDescendantsOfType<IOutputParameter>().SingleOrDefault(o => o.Name == "Out1");
            out1.Delete();
        }

        private IRaiseExceptionNode CreateExceptionNode(IScreenAction action, IESpace espace)
        {
            var excep = action.CreateNode<IRaiseExceptionNode>();
            excep.SetExceptionMessage("\"Screen not available\"");
            excep.Exception = espace.GetAllDescendantsOfType<OutSystems.Model.Logic.IException>().Single(sr => sr.ToString().Contains("Abort Activity Change Exception"));
            return excep;
        }

        protected override IEnumerable<ILink> InsertIfplus(IESpace espace, List<IKey> keys, IEnumerable<ILink> links)
        {
            return links;
        }

        protected override string GetDestinationName(ILink l)
        {
            var screen = (IScreen)l.OnClick.Destination;
            return screen.Name.ToString();
        }
    }
}
