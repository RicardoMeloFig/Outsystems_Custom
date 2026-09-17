using OutSystems.Model;
using OutSystems.Model.Logic;
using OutSystems.Model.Logic.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ModelAPITest.ToggleElements
{
    abstract class LogicGeneric<GAction> : ToggleableElement
        where GAction : IAction

    {
        public void GetAllElements(IESpace newe)
        {
            var listactions = newe.GetAllDescendantsOfType<GAction>();

            List<IKey> actionKeys = new List<IKey>();
            WriteHeader();
            foreach (GAction action in listactions.ToList())
            {
                Console.WriteLine(action);
                actionKeys.Add(action.ObjectKey);
            }

            if (actionKeys.Count() != 0)
            {
                ToggleElement(newe, actionKeys, "defaultfeature");
            }
        }

        public void GetAllElementsFromList(IESpace newe, List<string> elements, string feature)
        {
            var listactions = newe.GetAllDescendantsOfType<GAction>().Where(b => elements.Contains(b.Name)); ;

            List<IKey> actionKeys = new List<IKey>();
            WriteHeader();
            foreach (GAction action in listactions.ToList())
            {
                Console.WriteLine(action);
                actionKeys.Add(action.ObjectKey);
            }

            if (actionKeys.Count() != 0)
            {
                ToggleElement(newe, actionKeys, feature);
            }
        }

        public void GetDiffElements(IESpace old, IESpace newe, string newOrAltered)
        {
            var listOldActions = old.GetAllDescendantsOfType<GAction>();

            var listNewActions = newe.GetAllDescendantsOfType<GAction>();

            List<GAction> difActions = new List<GAction>();
            List<IKey> difActionKeys = new List<IKey>();

            foreach (GAction actions in listNewActions.ToList())
            {
                if (actions.Name != "GetFTValue")
                {
                    var skey = actions.ObjectKey;
                    var modDate = ((IFlow)actions).LastModifiedDate;
                    if (newOrAltered.Equals("new"))
                    {
                        var olds = listOldActions.SingleOrDefault(s => (s.ObjectKey.Equals(skey)));
                        if (olds == null)
                        {
                            difActions.Add(actions);
                            difActionKeys.Add(actions.ObjectKey);
                        }
                    }
                    else
                    {
                        var olds = listOldActions.SingleOrDefault(s => (s.ObjectKey.Equals(skey) && ((IFlow)s).LastModifiedDate.Equals(modDate)));
                        var olds2 = listOldActions.SingleOrDefault(s => (s.ObjectKey.Equals(skey)));
                        if (olds == null && olds2 != null)
                        {
                            difActions.Add(actions);
                            difActionKeys.Add(actions.ObjectKey);
                        }
                    }
                }
            }
            WriteHeader();
            if (newOrAltered.Equals("new")) { WriteHeader(); }
            else if (newOrAltered.Equals("altered")) { Console.WriteLine("\nAltered:"); }

            foreach (GAction actions in difActions)
            {
                Console.WriteLine(actions);
            }

            if (newOrAltered.Equals("new"))
            {
                if (difActionKeys.Count() != 0)
                {
                    ToggleElement(newe, difActionKeys, "defaultfeature");
                }
            }
        }

        public void ToggleElement(IESpace espace, List<IKey> keys, string feature)
        {
            var actions = espace.GetAllDescendantsOfType<IAction>().Where(s => keys.Contains(s.ObjectKey));
            ToggleManager manager = new ToggleManager();
            manager.GetToggleValueRetrievalAction(espace);
            var getToggleAction = manager.GetPlatformToggleRetrievalAction(espace);

            foreach (IAction sa in actions.ToList())
            {
                if (feature == "defaultfeature")
                {
                    feature = sa.Name;
                }

                var rec = manager.CreateToggleRecord(manager.GetToggleKey(espace.Name,feature), manager.GetToggleName(feature), espace);
                var newAction = CopyAction(espace, sa, manager);
                ResetAction(sa);
                CreateIntermediaryAction(sa, feature, espace, manager, newAction);
            }
        }
        private GAction CopyAction(IESpace espace, IAction sa, ToggleManager manager)
        {
            var newAction = (GAction)espace.Copy(sa);
            var oldname = sa.Name.ToString();
            sa.Name = manager.GetToggleName(oldname);
            newAction.Name = oldname;
            SetActionsPrivacy(newAction);
            return newAction;
        }

        private void ResetAction(IAction sa)
        {
            var nodes = sa.Nodes;
            foreach (IActionNode n in nodes.ToList())
            {
                n.Delete();
            }
            foreach (ILocalVariable l in sa.LocalVariables.ToList())
            {
                l.Delete();
            }
        }

        private void CreateIntermediaryAction(IAction sa, String feature, IESpace espace, ToggleManager manager, GAction newAction)
        {
            var start = sa.CreateNode<IStartNode>();
            var getToggle = CreateFetchToggleActionNode(sa, espace, feature, manager).Below(start);
            var ifToggle = sa.CreateNode<IIfNode>().Below(getToggle);
            ifToggle.SetCondition(manager.GetFeatureToggleIsOnOutputString(feature));
            var doAction = CreateCallActionNode(sa, newAction).ToTheRightOf(ifToggle);
            var end = sa.CreateNode<IEndNode>().Below(doAction);
            start.Target = getToggle;
            getToggle.Target = ifToggle;
            ifToggle.TrueTarget = doAction;
            ifToggle.FalseTarget = end;
            doAction.Target = end;
            if (newAction.OutputParameters.Count() != 0)
            {
                var assign = AssignOutputValues(sa, doAction);
                end.Below(assign);
                doAction.Target = assign;
                assign.Target = end;
            }
        }

        private IExecuteServerActionNode CreateFetchToggleActionNode(IAction sa, IESpace espace, String feature, ToggleManager manager)
        {
            var getToggleAction = manager.GetPlatformToggleRetrievalAction(espace);
            var actionName = manager.GetFeatureToggleIsOnActionString(feature);
            var toggleRecord = manager.GetToggleRecord(espace.Name, feature);
            var getToggle = sa.CreateNode<IExecuteServerActionNode>(actionName);
            getToggle.Action = getToggleAction;
            var keyParam = getToggleAction.InputParameters.Single(s => s.Name == "FeatureToggleKey");
            getToggle.SetArgumentValue(keyParam, toggleRecord);
            var modParam = getToggleAction.InputParameters.Single(s => s.Name == "ModuleName");
            getToggle.SetArgumentValue(modParam, "GetEntryEspaceName()");
            return getToggle;
        }

        private IExecuteServerActionNode CreateCallActionNode(IAction sa, GAction newAction)
        {
            var doAction = sa.CreateNode<IExecuteServerActionNode>();
            doAction.Action = newAction;
            foreach (IInputParameter i in newAction.InputParameters)
            {
                doAction.SetArgumentValue(i, i.Name);
            }
            return doAction;
        }

        private IAssignNode AssignOutputValues(IAction sa, IExecuteServerActionNode doAction)
        {
            var assign = sa.CreateNode<IAssignNode>().Below(doAction);
            foreach (IOutputParameter o in sa.OutputParameters)
            {
                assign.CreateAssignment(o.Name, $"{doAction.Name}.{o.Name}");
            }
            return assign;
        }

        protected abstract void SetActionsPrivacy(GAction action);
        protected abstract void WriteHeader();
    }
    
}
