using OutSystems.Model;
using OutSystems.Model.Logic;
using OutSystems.Model.Logic.Nodes;
using OutSystems.Model.UI;
using OutSystems.Model.UI.Mobile;
using OutSystems.Model.UI.Mobile.Events;
using OutSystems.Model.UI.Mobile.Widgets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ModelAPITest {
    class BlockReative : BlockGeneric<IBlock, IMobileBlockInstanceWidget, IMobileScreen, IPlaceholderContentWidget>
    {
        private static String dataActionName = "GetToggles";

        protected override IKey GetObjectKey(IMobileBlockInstanceWidget s)
        {
            return s.SourceBlock.ObjectKey;
        }

        protected override string GetName(IMobileBlockInstanceWidget o)
        {
            return o.SourceBlock.Name.ToString();
        }

        protected override void EncapsulatedInIf(IPlaceholderContentWidget p, IMobileBlockInstanceWidget o, IESpace espace, String feature)
        {
            ToggleManager manager = new ToggleManager();
            var name = GetName(o);
            var screens = espace.GetAllDescendantsOfType<IMobileScreen>();
            foreach (IMobileScreen s in screens)
            {
                AddToggleToDataAction(espace, s, o, manager, feature);
            }
            var instanceIf = p.CreateWidget<IIfWidget>();
            instanceIf.SetCondition($"{dataActionName}.IsDataFetched and {dataActionName}.FT_{feature}");
            instanceIf.Name = manager.GetIfWidgetName(feature,name);
            instanceIf.TrueBranch.Copy(o);
        }

        private void AddToggleToDataAction(IESpace espace, IMobileScreen s, IMobileBlockInstanceWidget o, ToggleManager manager, String feature)
        {
            var exists = s.GetAllDescendantsOfType<IMobileBlockInstanceWidget>().SingleOrDefault(k => k.ObjectKey.Equals(o.ObjectKey));
            if (exists != default)
            {
                var action = s.GetAllDescendantsOfType<IDataAction>().SingleOrDefault(e => e.Name == dataActionName);
                if (action == default)
                {
                    CreateActionFromScratch(espace, s, manager, feature);
                }
                else
                {
                    AddToggleToExistingAction(espace, action, manager, feature);
                }
            }
        }

        private void AddToggleToExistingAction(IESpace espace, IDataAction action, ToggleManager manager, String feature)
        {
            var actionName = manager.GetFeatureToggleIsOnActionString(feature);
            var toggleName = manager.GetToggleName(feature);
            var existsFeature = action.GetAllDescendantsOfType<IExecuteServerActionNode>().SingleOrDefault(s => s.Name == actionName);
            if (existsFeature == default)
            {
                CreateOutputParameter(action, espace, toggleName);
                var start = action.GetAllDescendantsOfType<IStartNode>().Single();
                var getToggle = CreateExecuteActionNode(espace, action, manager, feature).Below(start);
                var assignVar = AssignOutputValues(action, manager, feature, true);

                var startTarget = start.Target;
                getToggle.Target = startTarget;
                start.Target = getToggle;
            }
        }

        private void CreateActionFromScratch(IESpace espace, IMobileScreen s, ToggleManager manager, String feature)
        {
            var toggleName = manager.GetToggleName(feature);
            var oninitaction = s.CreateDataAction(dataActionName);
            SetOutput(oninitaction, espace, toggleName);
            var start = oninitaction.CreateNode<IStartNode>();
            var getToggle = CreateExecuteActionNode(espace, oninitaction, manager, feature).Below(start);
            var assignVar = AssignOutputValues(oninitaction, manager, feature, false).Below(getToggle);
            var end = oninitaction.CreateNode<IEndNode>().Below(assignVar);

            start.Target = getToggle;
            getToggle.Target = assignVar;
            assignVar.Target = end;
        }

        private void SetOutput(IDataAction action, IESpace espace, String toggleName)
        {
            var out1 = action.GetAllDescendantsOfType<IOutputParameter>().SingleOrDefault(o => o.Name == "Out1");
            out1.Delete();
            CreateOutputParameter(action, espace, toggleName);
        }

        private void CreateOutputParameter(IDataAction action, IESpace espace, String toggleName)
        {
            var outputparam = action.CreateOutputParameter(toggleName);
            outputparam.DataType = espace.BooleanType;
        }

        private IExecuteServerActionNode CreateExecuteActionNode(IESpace espace, IDataAction action, ToggleManager manager, String feature)
        {
            var actionName = manager.GetFeatureToggleIsOnActionString(feature);
            var toggleRecord = manager.GetToggleRecord(espace.Name, feature);
            var getToggleAction = manager.GetPlatformToggleRetrievalAction(espace);

            var getToggle = action.CreateNode<IExecuteServerActionNode>(actionName);
            getToggle.Action = getToggleAction;
            var keyParam = getToggleAction.InputParameters.Single(s => s.Name == "FeatureToggleKey");
            getToggle.SetArgumentValue(keyParam, toggleRecord);
            var modParam = getToggleAction.InputParameters.Single(s => s.Name == "ModuleName");
            getToggle.SetArgumentValue(modParam, "GetEntryEspaceName()");
            return getToggle;
        }

        private IAssignNode AssignOutputValues(IDataAction action, ToggleManager manager, String feature, Boolean add)
        {
            var outputName = manager.GetFeatureToggleIsOnOutputString(feature);
            var toggleName = manager.GetToggleName(feature);
            IAssignNode assignVar = null;
            if (add)
            {
                assignVar = action.GetAllDescendantsOfType<IAssignNode>().Single();
            }
            else
            {
                assignVar = action.CreateNode<IAssignNode>();
            }
            assignVar.CreateAssignment(toggleName, outputName);
            return assignVar;
        }

    }

}
