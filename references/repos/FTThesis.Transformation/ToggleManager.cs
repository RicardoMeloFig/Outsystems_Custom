using ModelAPITest.ToggleElements;
using OutSystems.Model;
using OutSystems.Model.Data;
using OutSystems.Model.Logic;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ModelAPITest
{
    class ToggleManager
    {
        ToggleValueRetrieval toggleAction;
        ManagementPlatformRemoteToggleCreation toggleRemote;

        public ToggleManager(){
            toggleAction = new ToggleValueRetrieval();
            toggleRemote = new ManagementPlatformRemoteToggleCreation();
        }


        public IRecord CreateToggleRecord(String key, String label, IESpace espace)
        {
            var entity = ToggleEntity.GetTogglesEntity(espace);
            return ToggleEntity.CreateRecord(entity, key, label);

        }

        public String GetToggleRecord(String prefix, String element)
        {
            return $"Entities.FeatureToggles.FT_{prefix}_{element}";
        }

        public IAction GetToggleValueRetrievalAction(IESpace espace)
        {
            return toggleAction.GetToggleAction(espace);
        }

        public String GetToggleValueRetrievalActionString(String prefix, String element)
        {
            return $"GetFTValue(Entities.FeatureToggles.FT_{prefix}_{element})";
        }

        public String GetFeatureToggleIsOnActionString(String element)
        {
            return $"FT_{element}_IsOn";
        }

        public String GetFeatureToggleIsOnOutputString(String element)
        {
            return $"FT_{element}_IsOn.IsOn";
        }

        public IAction CreateActionToAddTogglesToMngPlat(IESpace espace)
        {
            return toggleRemote.GetToggleAction(espace);
        }

        public IServerActionSignature GetPlatformToggleRetrievalAction(IESpace espace)
        {
            var lib = espace.References.Single(a => a.Name == "FeatureToggle_Lib");
            var getToggleAction = (IServerActionSignature)lib.ServerActions.Single(a => a.Name == "FeatureToggle_IsOn");
            return getToggleAction;
        }

        public String GetToggleKey(String prefix, String element)
        {
            return $"FT_{prefix}_{element}";
        }

        public String GetToggleName(String element)
        {
            return $"FT_{element}";
        }

        public String GetIfWidgetName(String prefix, String element)
        {
            return $"If_FT_{prefix}_{element}";
        }

    }
}
