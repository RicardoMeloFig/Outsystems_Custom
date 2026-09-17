using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OutSystems.Model;
using OutSystems.Model.Logic;
using OutSystems.Model.Logic.Nodes;
using OutSystems.Model.UI.Web;

namespace ModelAPITest.ToggleElements
{
    class ToggleValueRetrieval : ToggleAction
    {
        private const string TogglesEntity = "FeatureToggles";

        public IAction GetToggleAction(IESpace espace)
        {
            if (IsTraditional(espace))
            {
                var action = (IServerAction)espace.ServerActions.SingleOrDefault(s => s.Name == "GetFTValue");
                if (action == default)
                {
                    return CreateToggleAction(espace);
                }
                else
                {
                    return action;
                }
            }
            else
            {
                return null;
            }
        }
        private IAction CreateToggleAction(IESpace espace)
        {
                var action = espace.CreateServerAction("GetFTValue");
                action.Function = true;
                ConstructAction(espace, action);
                return action;
        }

        private void ConstructAction(IESpace espace, IAction action)
        {
            CreateInputParameters(espace, action);
            CreateOutputParameters(espace, action);
            var start = action.CreateNode<IStartNode>();
            var executeaction = CreateExecuteActionNode(espace, action).Below(start);
            start.Target = executeaction;
            var assign = AssignValues(action).Below(executeaction);
            executeaction.Target = assign;
            var end = action.CreateNode<IEndNode>().Below(assign);
            assign.Target = end;
        }

        private void CreateInputParameters(IESpace espace, IAction action)
        {
            var ftType = action.CreateInputParameter("FTType");
            ftType.DataType = espace.Entities.Single(s => s.Name == TogglesEntity).IdentifierType;
            ftType.IsMandatory = true;
            ftType.Description = "Feature Toggle Identifier";
        }

        private void CreateOutputParameters(IESpace espace, IAction action)
        {
            var isOn = action.CreateOutputParameter("IsOn");
            isOn.DataType = espace.BooleanType;
            isOn.SetDefaultValue("False");
            isOn.Description = "Feature Toggle Value";
        }

        private IExecuteServerActionNode CreateExecuteActionNode(IESpace espace, IAction action)
        {
            ToggleManager manager = new ToggleManager();
            var executeaction = action.CreateNode<IExecuteServerActionNode>();
            var getToggleAction = manager.GetPlatformToggleRetrievalAction(espace);
            executeaction.Action = getToggleAction;
            var keyParam = getToggleAction.InputParameters.Single(s => s.Name == "FeatureToggleKey");
            executeaction.SetArgumentValue(keyParam, "FTType");
            var modParam = getToggleAction.InputParameters.Single(s => s.Name == "ModuleName");
            executeaction.SetArgumentValue(modParam, "GetEntryEspaceName()");
            return executeaction;
        }

        private IAssignNode AssignValues(IAction action)
        {
            var assign = action.CreateNode<IAssignNode>();
            assign.CreateAssignment("IsOn", "FeatureToggle_IsOn.IsOn");
            return assign;
        }


            private bool IsTraditional(IESpace module)
        {
            var themes = module.GetAllDescendantsOfType<IWebTheme>();
            bool any = false;
            foreach (IWebTheme tm in themes)
            {
                any = true;
            }
            return any;
        }
    }
}
